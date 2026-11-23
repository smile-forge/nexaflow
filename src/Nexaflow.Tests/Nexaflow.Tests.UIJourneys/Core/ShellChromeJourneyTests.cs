using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;
using FlaUI.Core.Input;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UI;

/// <summary>
/// One-pass UI journey for the always-present <b>shell chrome</b> — the window frame that surrounds
/// every tab: the ribbon, the AI input bar, the breadcrumb bar, the tab strip, the Messages
/// (Notifications) inbox, the Ribbon editor, and the modal Options overlay. Amortises the ~20s app
/// launch by exercising every frame control in a single pass with <b>soft</b> checks, so one missing
/// or broken control doesn't abort coverage of the rest.
/// <para>
/// The chrome lives in <c>Nexaflow.Core</c>, but this journey drives the <i>running</i> app via FlaUI
/// (by AutomationId), so it needs no Core project reference and belongs alongside the other
/// FlaUI journeys in Tests.Features.
/// </para>
/// <para>
/// <b>Invoke vs present-only.</b> The three overlays that appear over the frame (Messages inbox,
/// Ribbon editor, Options) are each opened <i>and closed</i> by re-invoking the same chrome button:
/// their commands are pure toggles — <c>ToggleNotifications</c>, <c>ToggleEdit</c>, <c>ToggleOptions</c>
/// — so the second invoke reliably dismisses the overlay (Options is MODAL, so leaving it open would
/// block the rest of the pass). The ribbon, AI box, breadcrumb bar, tab strip and the default tab are
/// present-only (opening/closing extra tabs and splitting panes is already covered by Core's TabTests).
/// </para>
/// <para>
/// <b>Out of scope</b> (deliberately not driven here): the AI response overlay, plan-approval and
/// tool-approval surfaces — they require a live, non-deterministic LLM run. Toast/confirmation/prompt
/// overlays are transient and event-driven, so they have no always-present control to key off.
/// <para>
/// The caption buttons <i>are</i> app chrome — custom buttons bound to app commands, not the OS frame —
/// so maximize/restore is driven here. Minimize and Close are present-checked, and for a reason that is
/// about this journey rather than about risk: the app runs against a throwaway config dir, so config a
/// control writes does not matter, but a minimized window is not one anything in-app can restore, and a
/// closed one ends the pass. The same logic decides the rest: what is skipped is skipped because it takes
/// the pass somewhere it cannot continue from, or because it reaches outside the app (a model turn, audio
/// capture, a download) where the isolated config buys nothing.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("chrome-frame")]
public class ShellChromeJourneyTests : UiJourneyTestBase
{
    /// <summary>The window frame, then the help pane, the tab strip and the split panes — everything the shell does around a page, in one launch.</summary>
    [TestMethod]
    [CoversNode("chrome-notifications-panel")]
    [CoversNode("chrome-ai-input")]
    [CoversNode("help-ui")]
    [CoversNode("ribbon-button-full")]
    [CoversNode("aibar-slash-palette")]
    [CoversNode("chrome-content-area")]
    [CoversNode("dual-pane")]
    [CoversNode("win-cux-split-empty")]
    [CoversNode("win-cux-close-pane")]
    public void Shell_Journey()
    {
        RunStep("the frame: ribbon, AI bar, caption buttons, overlays, overflow", ShellFrame);
        RunStep("the help pane opens beside the page, searches and closes", HelpPane);
        RunStep("a ribbon item opens a tab, and clicking a tab activates it", RibbonItemOpensATab);
        RunStep("the slash palette opens the matched page, and focus returns to the AI input", SlashPaletteOpensAPage);
        RunStep("Split from the empty strip area creates a second pane", SplitFromTheEmptyStripArea);
        RunStep("Split, then Close pane, collapses back with its content", SplitThenClosePane);
        RunStep("Split after closing the left pane keeps the survivor's tabs", SplitAfterClosingTheLeftPane);
        AssertJourney();
    }

    private void ShellFrame()
    {
        // The shell always launches a default FileSystem tab; wait on its file browser so the frame
        // is fully realised before we probe the chrome around it.
        Assert.IsNotNull(WaitForId("DirectoryTree", 15), "Default FileSystem tab did not load.");

        Check("The window has a title", () => !string.IsNullOrWhiteSpace(MainWindow.Title));

        // ── Always-present frame — present-only (these never open a modal) ──
        CheckPresent("Ribbon",          "RibbonControl");
        CheckPresent("AI input box",    "AiInputBox");
        CheckPresent("Breadcrumb bar",  "Chrome_BreadcrumbBar");
        CheckPresent("Tab strip",       "TabStrip");

        // The default FileSystem tab item lives in the strip (tab items are tagged TabItem_<PageKind>).
        CheckPresent("Default file-system tab", "TabItem_FileSystem");

        // ── Window caption buttons ──
        // Maximize is a plain toggle, pressed and pressed back. It goes through the UIA Invoke pattern and
        // never a coordinate click, which matters here: SnapLayoutHook answers WM_NCHITTEST over this button
        // with HTMAXBUTTON so Windows 11 offers Snap Layouts, and that OS flyout appears on hover after
        // about a second. Invoke moves no mouse, so nothing ever dwells there.
        CheckInvoke("Maximize", "Chrome_MaximizeRestore");
        CheckInvoke("Restore",  "Chrome_MaximizeRestore");

        // Not pressed, and not because of the config: nothing inside the app can undo a minimize, so the
        // rest of the pass would run against a window that isn't on screen — and Close ends the run.
        CheckPresent("Minimize (only the OS could bring it back)", "Chrome_Minimize");
        CheckPresent("Close (would end the journey)",              "Chrome_Close");

        // ── AI input bar ──
        // Clear is disabled on an empty box, so the journey types first — and typing is also what proves
        // the bar is live. Nothing is sent.
        Check("typing into the AI box enables Clear", () =>
        {
            var box = WaitForId("AiInputBox", 5);
            if (box is null) return false;
            box.Focus();
            box.AsTextBox().Text = "journey probe";
            Wait.UntilInputIsProcessed();
            System.Threading.Thread.Sleep(200);
            return WaitForId("AiBar_Clear", 4)?.IsEnabled == true;
        });
        CheckDoes("Clear empties the AI box", "AiBar_Clear",
                  () => string.IsNullOrEmpty(WaitForId("AiInputBox", 3)?.AsTextBox().Text));

        // Send starts a model turn and Mic starts audio capture (and, on a fresh config, the voice-model
        // download). Both reach outside the app, which the throwaway config dir does not make harmless.
        CheckPresent("Send (would start a model turn)",  "AiBar_Send");
        CheckPresent("Mic (would start capture)",        "AiBar_Mic");
        CheckPresent("Context toggle",                   "AiBar_ContextToggle");

        // ── Messages / Notifications inbox — open then dismiss via the same toggle ──
        // ToggleNotificationsCommand flips NotificationsOpen. The panel itself is a Border (no reliable
        // UIA peer in WPF), so we verify the toggle button responds rather than probing the Border.
        CheckInvoke("Messages button (open)",  "NotificationsButton");
        CheckInvoke("Messages button (close)", "NotificationsButton");

        // ── Ribbon editor — open via the ribbon Edit button, verify, close via the same toggle ──
        // Chrome_RibbonEdit fires ToggleEditCommand (IsEditOpen = !IsEditOpen); re-invoking closes it.
        CheckInvoke("Ribbon Edit button (open)",  "Chrome_RibbonEdit");
        CheckPresent("Ribbon editor overlay",     "Chrome_RibbonEditor");

        // Everything in here edits a draft that Cancel discards, and the app is running against a throwaway
        // config dir anyway — so these are pressed rather than merely looked at.
        CheckInvoke("Add a ribbon page",  "RibbonEditor_AddPage");

        // Reset first, then add: reset discards the draft, so a separator added before it would be gone by
        // the time the pass tries to select one — which is exactly what happened the first time.
        CheckInvoke("Reset to defaults",  "RibbonEditor_ResetDefaults");
        CheckInvoke("Add a separator",    "RibbonEditor_AddSeparator");

        // Cards are list items, numbered as the draft stood when it was built (the reset restarts the count), so
        // a card is selected like any row and the panel for it appears.
        CheckPresent("Ribbon card strip",   "RibbonEditor_Strip");
        CheckPresent("A card's button",     "RibbonEditor_CardPreview");
        CheckPresent("Add-page catalogue",  "RibbonEditor_PageCatalog");

        Check("Select the separator card", () => SelectRadio("RibbonEditor_Separator1"));
        CheckInvoke("Move the separator right", "RibbonEditor_SeparatorRight");
        CheckInvoke("Move the separator left",  "RibbonEditor_SeparatorLeft");
        CheckInvoke("Delete the separator",     "RibbonEditor_DeleteSeparator");

        Check("Select the This PC card", () => SelectRadio("RibbonEditor_Card2"));
        CheckPresent("Selected button preview", "RibbonEditor_Preview");
        CheckPresent("Name box",                "RibbonEditor_Name");
        CheckInvoke("Size toggle",              "RibbonEditor_SizeToggle");
        CheckInvoke("Move right",               "RibbonEditor_MoveRight");
        CheckInvoke("Move left",                "RibbonEditor_MoveLeft");

        // Icon: search the Fluent set and pick one.
        CheckPresent("Look sections", "RibbonEditor_Sections");
        Check("Search icons for 'home'", () => TypeInto("RibbonEditor_Icons_Search", "home"));
        CheckInvoke("Pick the filled Fluent home", "RibbonEditor_Icons_Icon_FluentFilled_home");

        // Colours: a theme swatch on the background, then an outline.
        Check("Colours section",  () => SelectRadio("RibbonEditor_SectionColours"));
        CheckPresent("Colour slots", "RibbonEditor_Slots");
        Check("Background slot",  () => SelectRadio("RibbonEditor_SlotBackground"));
        CheckInvoke("Teal swatch",   "RibbonEditor_Colour_SwatchTeal");
        CheckInvoke("Reuse a colour on this ribbon", "RibbonEditor_UsedColour");
        CheckPresent("Hex entry",    "RibbonEditor_Colour_Hex");
        Check("Border slot",      () => SelectRadio("RibbonEditor_SlotBorder"));
        CheckPresent("Outline weights", "RibbonEditor_Weights");
        Check("Thick outline",    () => SelectRadio("RibbonEditor_WeightThick"));

        // Shape: the gallery draws the button in each; pick the circle badge.
        Check("Shape section",    () => SelectRadio("RibbonEditor_SectionShape"));
        CheckPresent("Shape gallery", "RibbonEditor_Shapes");
        CheckPresent("A shape tile's button", "RibbonEditor_ShapePreview");
        Check("Circle shape",     () => SelectRadio("RibbonEditor_ShapeCircle"));

        CheckInvoke("Reset the button's look", "RibbonEditor_ResetLook");
        CheckInvoke("Delete the button",       "RibbonEditor_DeleteItem");

        // Done commits the draft and closes; the X closes without committing. Both would end the editor,
        // and Cancel below is the one whose closing this pass asserts — so these two are checked present.
        CheckPresent("Done (commits the layout)", "RibbonEditor_Done");
        CheckPresent("Close X",                   "RibbonEditor_CloseX");

        CheckDoes("Cancel leaves the ribbon editor", "RibbonEditor_Cancel",
                  () => WaitForId("Chrome_RibbonEditor", 3) is null);

        // ── Help — the ? above Options opens this page's help beside it, and the same button closes it ──
        // What the pane itself does (search, results, the index, F1) is HelpJourneyTests'; here it is chrome.
        CheckInvoke("Help button (open)",  "Chrome_HelpButton");
        CheckPresent("Help search box",    "Help_SearchBox");
        CheckInvoke("Help button (close)", "Chrome_HelpButton");

        // ── Options overlay (MODAL) — open, verify, close via the same toggle so it can't block ──
        // Chrome_OptionsButton fires ToggleOptionsCommand (OptionsOpen = !OptionsOpen); re-invoking closes it.
        // Opened and closed from the chrome button only — what is *inside* the panel belongs to the
        // config journey (OptionsJourneyTests), which is already the thing that walks its sections.
        CheckInvoke("Options button (open)",  "Chrome_OptionsButton");
        CheckPresent("Options overlay",       "Chrome_OptionsPanel");
        CheckInvoke("Options button (close)", "Chrome_OptionsButton");

        // ── Overflow — each ••• exists only while something does not fit ──
        // The window is narrowed to its minimum width for this and put back afterwards. The ribbon stops offering an
        // overflow below a floor of its own, and at the window's MinWidth it has to be above it — otherwise the last
        // ribbon buttons are simply cut off.
        var size = MainWindow.BoundingRectangle;
        Check("at its minimum width the window spills the ribbon", () =>
            ResizeWindow(600, size.Height) && WaitForFs(() => Exists("Chrome_RibbonOverflow"), 3));
        CheckDoes("Ribbon overflow lists the buttons that did not fit", "Chrome_RibbonOverflow",
                  () => WaitForFs(() => WithIdPrefix("RibbonOverflow_").Length > 0, 3));
        Check("a ribbon overflow entry closes the list", () => PressFirstAndWaitGone("RibbonOverflow_"));

        Check("opening pages until the tab strip spills", OpenTabsUntilTheStripSpills);
        CheckDoes("Tab overflow lists the tabs that did not fit", "Chrome_TabOverflow",
                  () => WaitForFs(() => WithIdPrefix("TabOverflow_").Length > 0, 3));
        Check("picking a hidden tab closes the list", () => PressFirstAndWaitGone("TabOverflow_"));
        Check("the window goes back to its size", () => ResizeWindow(size.Width, size.Height));
    }

    // ── Help ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The Help pane — Core's own page, opened beside the page in use by the chrome's ? button and by F1. Walks it the way a
    /// reader would: open the help for the default File System tab (the window splits, help on the right), search it for "pane" —
    /// a word the File System and Help showcases both use — open the match from the other help page, list every help page, then F1
    /// to point help back at the page in use and F1 again to close it. The showcases come out of the English language pack beside
    /// the app, so this also proves the pack shipped with the build and that pages load from it.
    /// </summary>
    private void HelpPane()
    {
        Assert.IsNotNull(WaitForId("DirectoryTree", 15), "Default FileSystem tab did not load.");

        CheckDoes("? opens help beside the page in use", "Chrome_HelpButton",
                  () => WaitForId("Help_Document", Affordable(8)) is not null && TabStrips() == 2);
        CheckPresent("the Help tab", "TabItem_Help");

        Check("typing searches the page shown, then every other help page", () =>
        {
            var box = WaitForId("Help_SearchBox", Affordable(5));
            if (box is null) return false;
            box.Focus();
            box.AsTextBox().Text = "pane";
            Wait.UntilInputIsProcessed();
            return WaitForId("Help_SearchStatus", Affordable(6)) is not null
                && WaitForId("Help_OtherResults", Affordable(8)) is not null;
        });
        CheckPresent("the match count", "Help_SearchMatchCount");
        CheckInvoke("next match",       "Help_SearchNext");
        CheckInvoke("previous match",   "Help_SearchPrevious");

        Check("a match on another help page opens it, with the search carried over", () =>
        {
            var first = WaitForId("Help_OtherResults", Affordable(5))
                ?.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem));
            if (first is null) return false;
            first.AsListBoxItem().Select();
            Wait.UntilInputIsProcessed();
            Thread.Sleep(500);
            return WaitForId("Help_Document", Affordable(3)) is not null
                && WaitForId("Help_SearchStatus", Affordable(3)) is not null;
        });

        CheckDoes("clearing ends the search", "Help_SearchClear",
                  () => WaitForId("Help_SearchStatus", 1) is null);
        CheckDoes("the index button lists every help page", "Help_IndexButton",
                  () => WaitForId("Help_Document", Affordable(3)) is not null);

        // Help's own page is where locate: links are shown off — the one kind of link that points at a control on screen
        // instead of navigating. Reaching it from the index is the index doing its job.
        Check("an index entry opens that help page", () => InvokeLink(link => Named(link, "Help")));
        // Picked by where it points rather than by its words, which are the help page's to reword.
        Check("a locate: link lassoes the control it names", () =>
            InvokeLink(link => GoingTo(link, "locate:Chrome_HelpButton"))
            && WaitForId("Locate_Lasso", Affordable(4)) is not null);
        Check("and a click takes the lasso down", () =>
        {
            WaitForId("Help_SearchBox", Affordable(3))?.Click();
            Wait.UntilInputIsProcessed();
            Thread.Sleep(600);
            return MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Locate_Lasso")) is null;
        });

        // The reader moved within help (a result, the index), so the pane stayed where they took it: F1 points it back
        // at the page in use, and pressed again while it shows that page, closes it.
        Check("F1 points help back at the page in use", () =>
        {
            Keyboard.Press(VirtualKeyShort.F1);
            Wait.UntilInputIsProcessed();
            Thread.Sleep(400);
            return WaitForId("Help_Document", Affordable(3)) is not null;
        });
        Check("F1 again closes help, and the split with it", () =>
        {
            Keyboard.Press(VirtualKeyShort.F1);
            Wait.UntilInputIsProcessed();
            Thread.Sleep(500);
            return WaitForId("Help_Document", 1) is null && TabStrips() == 1;
        });
    }

    /// <summary>Follows a link in the help document. Each link is a Hyperlink in the document's automation tree, named by
    /// its words and valued by where it goes; callers pick by whichever says what they mean.</summary>
    private bool InvokeLink(Func<AutomationElement, bool> matches)
    {
        var link = WaitForId("Help_Document", Affordable(3))
            ?.FindAllDescendants(cf => cf.ByControlType(ControlType.Hyperlink))
            .FirstOrDefault(matches);
        if (link?.Patterns.Invoke.PatternOrDefault is not { } invoke) return false;

        invoke.Invoke();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(400);
        return true;
    }

    private static bool Named(AutomationElement link, string name) => link.Name == name;

    private static bool GoingTo(AutomationElement link, string target) =>
        link.Patterns.Value.PatternOrDefault?.Value.ValueOrDefault == target;

    private int TabStrips() => MainWindow.FindAllDescendants(cf => cf.ByAutomationId("TabStrip")).Length;

    // ── Tabs ──────────────────────────────────────────────────────────────────

    /// <summary>The first ribbon button, which opens a page — null when the ribbon is empty in this build.</summary>
    private AutomationElement? FirstRibbonButton() =>
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("RibbonControl"))
                  ?.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button));

    /// <summary>Opens the page the first ribbon button opens, so a pane holds realised page content.</summary>
    private bool OpenAPageFromTheRibbon()
    {
        if (FirstRibbonButton() is not { } button) return false;
        button.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(500);
        return true;
    }

    private void RibbonItemOpensATab()
    {
        if (!OpenAPageFromTheRibbon()) { Assert.Inconclusive("No ribbon buttons found — ribbon may be empty in this build."); return; }

        var tabs = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("TabStrip"))
                             ?.FindAllDescendants()
                             .Where(e => e.Properties.AutomationId.ValueOrDefault?.StartsWith("TabItem_") == true)
                             .ToArray() ?? [];
        Assert.IsTrue(tabs.Length >= 1, "Expected at least one tab in the strip after clicking a ribbon item.");

        tabs[0].Click();
        Wait.UntilInputIsProcessed();
        Assert.IsFalse(App.HasExited, "App crashed after clicking a tab.");
    }

    /// <summary>
    /// A "/" query opens a page deterministically through the palette (proven to open the Services page), and focus lands
    /// back on the AI input once the tab is open — the Services view realizes and could take focus itself.
    /// </summary>
    private void SlashPaletteOpensAPage()
    {
        var ai = WaitForId("AiInputBox", 5);
        Assert.IsNotNull(ai, "AI input box not found.");
        ai!.Focus();
        Thread.Sleep(2500);   // let the (Debug) feature catalog warm before it drives the palette candidates

        // "Services" is a default-openable page — /serv should match it, auto-select it, and Enter opens it.
        Keyboard.Type("/serv");
        Wait.UntilInputIsProcessed();
        Thread.Sleep(500);    // the palette rebuild is debounced off the keystroke
        Keyboard.Press(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed();

        Assert.IsNotNull(WaitForId("TabItem_SystemServices", 8),
            "Typing /serv and pressing Enter did not open the Services tab — the palette didn't match/route.");
        Thread.Sleep(600);    // focus is handed to the AI bar at Background priority, after the view realizes
        Assert.IsTrue(ai.Properties.HasKeyboardFocus.ValueOrDefault, "Opening a tab did not leave keyboard focus on the AI input.");
        Assert.IsFalse(App.HasExited, "The app crashed while opening a tab from the palette.");
    }

    // ── Split panes — each step leaves the window as one pane ─────────────────

    private void SplitFromTheEmptyStripArea()
    {
        Assert.AreEqual(1, CountTabStrips(), "Expected a single pane (one TabStrip) before the split.");
        try
        {
            SplitFromTheStrip();
            Assert.AreEqual(2, CountTabStrips(), "Expected two panes (two TabStrips) after Split.");
            Assert.IsFalse(App.HasExited, "App crashed while splitting.");
        }
        finally { CollapseSplit(); }
    }

    private void SplitThenClosePane()
    {
        // Open a tab so a pane holds realised page content — this is what gets re-parented on collapse.
        if (!OpenAPageFromTheRibbon()) { Assert.Inconclusive("No ribbon buttons available to open a tab."); return; }
        try
        {
            SplitFromTheStrip();
            Assert.AreEqual(2, CountTabStrips(), "Expected two panes after Split.");

            // Collapse via the right (empty) pane's "Close pane" — the surviving pane's live content
            // is re-hosted in a fresh PaneView, which must not throw the WPF "element already has a parent".
            ClosePaneOf(rightMost: true);
            Assert.AreEqual(1, CountTabStrips(), "Expected one pane after Close pane.");
            Assert.IsFalse(App.HasExited, "App crashed while collapsing the split.");
        }
        finally { CollapseSplit(); }
    }

    private void SplitAfterClosingTheLeftPane()
    {
        // Closing the LEFT pane leaves the old right pane as the root. Splitting again used to build the new split
        // from the first pane the window was created with — long since emptied and detached — so every open tab
        // dropped out of the window. Help splits through the same path, so this is the case it would hit first.
        if (!OpenAPageFromTheRibbon()) { Assert.Inconclusive("No ribbon buttons available to open a tab."); return; }
        try
        {
            SplitFromTheStrip();
            Assert.AreEqual(2, CountTabStrips(), "Expected two panes after Split.");
            var tabsBefore = CountTabs();
            Assert.IsTrue(tabsBefore >= 1, "Expected at least one open tab before collapsing.");

            // Close the LEFT pane: its tabs move into the right one, which becomes the window's only pane.
            ClosePaneOf(rightMost: false);
            Assert.AreEqual(1, CountTabStrips(), "Expected one pane after closing the left pane.");

            SplitFromTheStrip();
            Assert.AreEqual(2, CountTabStrips(), "Expected two panes after splitting again.");
            Assert.AreEqual(tabsBefore, CountTabs(), "Splitting after the left pane closed lost the open tabs.");
            Assert.IsFalse(App.HasExited, "App crashed while re-splitting.");
        }
        finally { CollapseSplit(); }
    }

    private int CountTabStrips() => TabStrips();

    /// <summary>Every tab in every pane — tab items are tagged TabItem_&lt;PageKind&gt;.</summary>
    private int CountTabs()
        => MainWindow.FindAllDescendants(cf => cf.ByAutomationId("TabStrip"))
                     .Sum(s => s.FindAllDescendants()
                                .Count(e => e.Properties.AutomationId.ValueOrDefault?.StartsWith("TabItem_") == true));

    /// <summary>The strip's own context menu on its empty area, and its "Split".</summary>
    private void SplitFromTheStrip()
    {
        RightClickStrip(rightMost: false);
        var split = FindMenuItem("Split");
        Assert.IsNotNull(split, "'Split' item not found on the empty tab-strip context menu.");
        split!.AsMenuItem().Invoke();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(600);
    }

    private void ClosePaneOf(bool rightMost)
    {
        RightClickStrip(rightMost);
        var closePane = FindMenuItem("Close pane");
        Assert.IsNotNull(closePane, "'Close pane' was not offered while split.");
        closePane!.AsMenuItem().Invoke();
        Wait.UntilInputIsProcessed();
        Thread.Sleep(500);
    }

    /// <summary>Back to one pane, whatever a failed step left: the next step starts from a single pane.</summary>
    private void CollapseSplit()
    {
        for (var attempts = 0; attempts < 3 && CountTabStrips() > 1; attempts++)
        {
            try { ClosePaneOf(rightMost: true); }
            catch (AssertFailedException) { return; }
        }
    }

    /// <summary>Right-click near the right edge of a strip, clear of any tabs (which pack from the left), so the strip's own context menu shows rather than a tab's.</summary>
    private void RightClickStrip(bool rightMost)
    {
        var strips = MainWindow.FindAllDescendants(cf => cf.ByAutomationId("TabStrip"))
                               .OrderBy(s => s.BoundingRectangle.X).ToArray();
        var strip = rightMost ? strips[^1] : strips[0];
        var r = strip.BoundingRectangle;
        Mouse.MoveTo(new System.Drawing.Point(r.Right - 25, r.Y + r.Height / 2));
        Mouse.Click(MouseButton.Right);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(400);
    }

    private AutomationElement? FindMenuItem(string name)
    {
        for (int i = 0; i < 30; i++)
        {
            var hit = Automation.GetDesktop()
                .FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem))
                .FirstOrDefault(e => e.Name == name);
            if (hit is not null) return hit;
            Thread.Sleep(100);
        }
        return null;
    }

    private bool ResizeWindow(double width, double height)
    {
        var transform = MainWindow.Patterns.Transform.PatternOrDefault;
        if (transform is null || !transform.CanResize.ValueOrDefault) return false;
        transform.Resize(width, height);
        Wait.UntilInputIsProcessed();
        System.Threading.Thread.Sleep(400);
        return true;
    }

    /// <summary>
    /// Controls in any of the app's windows whose id starts with <paramref name="prefix"/>. UIA may report a Popup's
    /// content under the shell or as a window of its own, so every window is searched.
    /// </summary>
    private AutomationElement[] WithIdPrefix(string prefix) =>
        Automation.GetDesktop()
                  .FindAllChildren(cf => cf.ByProcessId(App.ProcessId))
                  .SelectMany(w => w.FindAllDescendants())
                  .Where(e => e.Properties.AutomationId.ValueOrDefault?.StartsWith(prefix, StringComparison.Ordinal) == true)
                  .ToArray();

    private bool PressFirstAndWaitGone(string prefix)
    {
        var entry = WithIdPrefix(prefix).FirstOrDefault();
        if (entry is null) return false;
        entry.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => WithIdPrefix(prefix).Length == 0, 3);
    }

    /// <summary>Presses the ribbon's page buttons in turn until the strip has more tabs than it can show.</summary>
    private bool OpenTabsUntilTheStripSpills()
    {
        var ribbon = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("RibbonControl"));
        if (ribbon is null) return false;
        var pages = ribbon.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button))
                          .Where(b => b.Properties.AutomationId.ValueOrDefault?.StartsWith("Ribbon_", StringComparison.Ordinal) == true);
        foreach (var page in pages)
        {
            if (Exists("Chrome_TabOverflow")) return true;
            try { page.AsButton().Invoke(); } catch { continue; }
            Wait.UntilInputIsProcessed();
            System.Threading.Thread.Sleep(600);
        }
        return WaitForFs(() => Exists("Chrome_TabOverflow"), 2);
    }
}

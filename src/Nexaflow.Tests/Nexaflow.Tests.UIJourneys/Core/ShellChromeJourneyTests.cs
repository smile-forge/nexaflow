using FlaUI.Core.AutomationElements;
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
[NoCoverage("shell-chrome journey")]
public class ShellChromeJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    public void ShellChrome_Controls_RespondInOnePass()
    {
        // The shell always launches a default FileSystem tab; wait on its file browser so the frame
        // is fully realised before we probe the chrome around it.
        Assert.IsNotNull(WaitForId("DirectoryTree", 15), "Default FileSystem tab did not load.");

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

        AssertJourney();
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

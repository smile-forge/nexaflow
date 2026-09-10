using System;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;
using FlaUI.Core.WindowsAPI;

namespace Nexaflow.Tests.Features.WindowsFileSystem.UI;

/// <summary>
/// The one UI journey for the file browser: the tab loads, the create overlay makes each kind of thing it
/// offers, renaming a file can be started and then cancelled without touching it, and a template defined in
/// Options turns up in that overlay and produces a file with the right content.
/// <para>
/// This was three classes and seven test methods, each launching its own Nexaflow to assert one or two
/// things about the same view. The launches were the runtime. Running them in sequence against one app also
/// puts the templated-create case where it belongs — after the ordinary create cases, using the overlay they
/// just proved works, rather than re-establishing all of it from scratch.
/// </para>
/// <para>
/// Checks are soft (<see cref="UiJourneyTestBase.Check"/> and friends) so one broken control still reports
/// the rest; the sequences that depend on a previous step opening something are guarded, so a failure early
/// skips what it invalidates instead of cascading into noise.
/// </para>
/// <para>
/// The timing harness next door stays separate: it is a measurement, categorised Interactive, not a journey.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("win-file-system")]
public class FileSystemJourneyTests : UiJourneyTestBase
{
    private const string TemplateBody = "TEMPLATE-CONTENT-XYZ";

    private string _folder = null!;
    private string _sourceTemplate = null!;

    protected override void OnUISetup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "nexaflow-fs-journey-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "existing.txt"), "x");   // for the existence warning

        // Enough rows that a band can cross several and still leave empty space to start the drag in.
        for (int i = 1; i <= 5; i++) File.WriteAllText(Path.Combine(_folder, $"bulk-{i}.txt"), "x");

        _sourceTemplate = Path.Combine(Path.GetTempPath(),
            "nexaflow-tplsrc-" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(_sourceTemplate, TemplateBody);
    }

    [TestCleanup]
    public void CleanupFolder()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); } catch { }
        try { if (File.Exists(_sourceTemplate)) File.Delete(_sourceTemplate); } catch { }
    }

    /// <summary>
    /// Opens the create overlay and picks a type. False when either step doesn't happen, so the caller can
    /// skip the rest of that flow rather than assert against an overlay that never opened.
    /// <para>
    /// Gated on the filename box rather than the overlay: <c>CreateOverlay</c> is a Border, and WPF gives a
    /// Border no automation peer — waiting for it would succeed by finding nothing.
    /// </para>
    /// </summary>
    private bool OpenCreateOverlay(string typeName)
    {
        var newBtn = WaitForId("New", 8);
        if (newBtn is null) return false;
        newBtn.AsButton().Invoke();

        if (WaitForId("CreateFileNameBox", 6) is null) return false;

        var typeBtn = WaitForId(typeName, 6);
        if (typeBtn is null) return false;
        typeBtn.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
        return true;
    }

    /// <summary>
    /// Closes the create overlay with its own Cancel button — the way out when a flow leaves it open.
    /// <para>
    /// It used to press Escape in the filename box, which works and proves nothing about the button
    /// beside it. Cancel is bound to <c>CancelCreateCommand</c> and is the control a user actually
    /// reaches for, so the flow that has to dismiss the overlay anyway is the one that should press it.
    /// </para>
    /// </summary>
    private void DismissCreateOverlay()
    {
        if (WaitForId("CreateFileNameBox", 4) is null) return;

        CheckDoes("Cancel closes the create overlay", "FileSystem_CreateCancel",
                  () => WaitForGone("CreateFileNameBox", 4));
    }

    private void Confirm()
    {
        WaitForId("FileSystem_CreateConfirm")?.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
    }

    /// <summary>
    /// Band-selects rows by dragging from the empty space below them, and reports how many ended up
    /// selected — <c>-1</c> when the gesture could not be posed at all (no list, no rows, or the rows
    /// fill the list so there is no empty space to start in), which is a skip rather than a failure.
    /// </summary>
    private int BandSelectUpwards(int rowsToCover, bool holdCtrl = false)
    {
        var list = WaitForId("FileListView", 6);
        if (list is null) return -1;

        var rows = list.FindAllChildren(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.ListItem))
                       .OrderBy(r => r.BoundingRectangle.Top)
                       .ToArray();
        if (rows.Length <= rowsToCover) return -1;

        var listRect = list.BoundingRectangle;
        var last     = rows[^1].BoundingRectangle;
        var target   = rows[^rowsToCover].BoundingRectangle;

        int startY = last.Bottom + 10;
        if (startY >= listRect.Bottom - 4) return -1;   // the rows reach the bottom: nowhere to press

        int x      = target.Left + target.Width / 2;
        var start  = new System.Drawing.Point(x, startY);
        int endY   = target.Top + target.Height / 2;

        FlaUI.Core.Input.Mouse.Position = start;
        if (holdCtrl) Keyboard.Press(VirtualKeyShort.CONTROL);
        FlaUI.Core.Input.Mouse.Down(FlaUI.Core.Input.MouseButton.Left);
        try
        {
            // Several steps rather than one jump: the band has to arm on a move that clears the drag
            // threshold and then track, which is two separate things happening to the same gesture.
            for (int step = 1; step <= 6; step++)
            {
                FlaUI.Core.Input.Mouse.Position =
                    new System.Drawing.Point(x, startY + (endY - startY) * step / 6);
                Wait.UntilInputIsProcessed();
                System.Threading.Thread.Sleep(30);
            }
        }
        finally
        {
            FlaUI.Core.Input.Mouse.Up(FlaUI.Core.Input.MouseButton.Left);
            if (holdCtrl) Keyboard.Release(VirtualKeyShort.CONTROL);
        }

        Wait.UntilInputIsProcessed();
        System.Threading.Thread.Sleep(250);

        return list.Patterns.Selection.Pattern.Selection.Value.Length;
    }

    [TestMethod]
    [CoversNode("win-file-system-ui")]
    [CoversNode("winfs-create")]
    [CoversNode("winfs-create-template")]
    [CoversNode("winfs-act-rename")]
    [CoversNode("winfs-marquee-select")]
    public void FileSystem_Controls_RespondInOnePass()
    {
        // ── The tab loads with its primary elements ──────────────────────────────────────
        var tree = TryOpenTabWithElement("DirectoryTree");
        if (tree is null)
            Assert.Inconclusive("Could not open a FileSystem tab via any ribbon button.");

        Check("Directory tree is on screen", () => !tree!.IsOffscreen);
        Check("A file list or a drive list is present", () =>
            MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("FileListView")) is not null ||
            MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("DriveListView")) is not null);
        Check("The app survives loading a file browser tab", () => !App.HasExited);

        // ── Creating a folder ────────────────────────────────────────────────────────────
        NavigateFileBrowserTo(_folder);

        var opened = OpenCreateOverlay("Folder");
        Check("Create overlay opens for a folder", () => opened);
        if (opened)
        {
            var box = CheckPresent("Filename box", "CreateFileNameBox");
            if (box is not null)
            {
                box.AsTextBox().Text = "MadeByTest";
                Confirm();
                Check("A new folder is created",
                      () => WaitForFs(() => Directory.Exists(Path.Combine(_folder, "MadeByTest"))));
            }
        }

        // ── Creating a text file, with the name prefilled ────────────────────────────────
        opened = OpenCreateOverlay("Text File");
        Check("Create overlay opens for a text file", () => opened);
        if (opened)
        {
            var box = CheckPresent("Filename box (text file)", "CreateFileNameBox");
            Check("The text-file name is prefilled", () => box?.AsTextBox().Text == "New File.txt");

            if (box is not null)
            {
                box.AsTextBox().Text = "hello.txt";
                Confirm();
                Check("A new text file is created",
                      () => WaitForFs(() => File.Exists(Path.Combine(_folder, "hello.txt"))));
            }
        }

        // ── A name that already exists is refused, visibly ───────────────────────────────
        opened = OpenCreateOverlay("Text File");
        Check("Create overlay opens for the existing-name case", () => opened);
        if (opened)
        {
            var box = CheckPresent("Filename box (existing name)", "CreateFileNameBox");
            if (box is not null)
            {
                box.AsTextBox().Text = "existing";        // resolves to the seeded existing.txt
                CheckPresent("Name-clash warning", "CreateNameWarning", 4);
                Check("Create is disabled for a name that already exists", () =>
                    MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("FileSystem_CreateConfirm"))
                        is { } create && !create.Properties.IsEnabled.ValueOrDefault);
            }

            DismissCreateOverlay();   // nothing was created, and the next flow needs the overlay closed
        }

        // ── Rename: cancelling the prompt leaves the file alone and the page usable ──────
        // The reported bug: starting a rename and changing your mind. Cancel has to close the
        // prompt, leave the name on disk untouched, and leave a second rename possible.
        var row = WaitForName("existing.txt", 8);
        Check("The seeded file is listed", () => row is not null);
        if (row is not null)
        {
            row.Click();                    // select → the strip lists this file's actions
            Wait.UntilInputIsProcessed();
            System.Threading.Thread.Sleep(200);

            CheckInvoke("Rename action", "Rename", 6);

            var box = CheckPresent("Rename prompt", "ShellPromptBox", 5);
            Check("The rename prompt is seeded with the current name",
                  () => box?.AsTextBox().Text == "existing.txt");

            if (box is not null)
            {
                box.AsTextBox().Text = "changed-my-mind.txt";   // typed, then thought better of it
                Wait.UntilInputIsProcessed();
            }

            CheckInvoke("Rename prompt Cancel", "ShellPromptCancel", 5);

            Check("Cancel closes the rename prompt",
                  () => WaitForGone("ShellPromptBox", 4));
            Check("Cancel renames nothing", () =>
                File.Exists(Path.Combine(_folder, "existing.txt")) &&
                !File.Exists(Path.Combine(_folder, "changed-my-mind.txt")));

            // …and the page is still live: the same rename runs again, this time to completion.
            CheckInvoke("Rename action after a cancel", "Rename", 6);
            var box2 = CheckPresent("Rename prompt reopens after a cancel", "ShellPromptBox", 5);
            if (box2 is not null)
            {
                box2.AsTextBox().Text = "renamed.txt";
                Wait.UntilInputIsProcessed();
                CheckInvoke("Rename prompt OK", "ShellPromptOk", 5);
                Check("Confirming the prompt renames the file",
                      () => WaitForFs(() => File.Exists(Path.Combine(_folder, "renamed.txt"))));
            }
        }

        // ── Band-select: press below the rows and drag up across them ────────────────────
        // The gesture the list had no answer for: click, Ctrl-click and Shift-click all worked, but a
        // drag starting in the empty space did nothing at all.
        int banded = BandSelectUpwards(rowsToCover: 3);
        if (banded < 0)
        {
            Check("Band-select could not be posed (no empty space below the rows)", () => true);
        }
        else
        {
            Check("Dragging a band up over three rows selects exactly those three", () => banded == 3);

            int withCtrl = BandSelectUpwards(rowsToCover: 2, holdCtrl: true);
            Check("Ctrl+band adds to the selection instead of replacing it",
                  () => withCtrl < 0 || withCtrl > 3);
        }

        // ── A template defined in Options reaches the create overlay ─────────────────────
        var optionsBtn = WaitForName("Options", 6);
        if (optionsBtn is null)
        {
            AssertJourney();
            Assert.Inconclusive("Could not find the Options button; the templated-create half was not run.");
        }

        optionsBtn!.AsButton().Invoke();

        // Found by name: the Options sections are list entries, not controls carrying an automation id.
        var section = WaitForName("Templated Create", 6);
        Check("Options lists a Templated Create section", () => section is not null);
        if (section is not null)
        {
            section.Click();
            Wait.UntilInputIsProcessed();
        }

        var add = CheckPresent("Add-template button", "AddTemplateButton", 5);
        if (add is not null)
        {
            add.AsButton().Invoke();
            Wait.UntilInputIsProcessed();

            SetText("TemplateNameBox", "My Note");
            SetText("TemplateExtBox", ".md");
            SetText("TemplateIconBox", "📝");
            SetText("TemplateSourceBox", _sourceTemplate);

            var save = WaitForName("Save", 5);
            Check("Options offers a Save", () => save is not null);
            save?.AsButton().Invoke();
            Wait.UntilInputIsProcessed();

            // The template file is copied into the isolated config dir, content intact.
            var tplDir = Path.Combine(ConfigDir, "templatedcreate", "templates");
            var copied = WaitForFs(() => Directory.Exists(tplDir) && Directory.GetFiles(tplDir).Length > 0, 8);
            Check("The template file is copied into appdata", () => copied);
            Check("The copied template keeps its content", () =>
                copied && File.ReadAllText(Directory.GetFiles(tplDir).First()) == TemplateBody);
        }

        // The tab is still on _folder, so the overlay should now offer the new type.
        opened = OpenCreateOverlay("My Note");
        Check("The new template is offered in the create overlay", () => opened);
        if (opened)
        {
            SetText("CreateFileNameBox", "from-template.md");
            Confirm();

            var outFile = Path.Combine(_folder, "from-template.md");
            var made = WaitForFs(() => File.Exists(outFile), 8);
            Check("A file is created from the template", () => made);
            Check("The created file has the template's content",
                  () => made && File.ReadAllText(outFile) == TemplateBody);
        }

        RunDefineNewWizardPass();

        AssertJourney();
    }

    /// <summary>
    /// The "Define New…" wizard: the surface that associates a file, an extension or a glob with an
    /// internal viewer or an external app. Run as its own pass at the end of the journey because it is
    /// the one part with no launcher button — see <see cref="OpenDefineNewWizard"/>.
    /// <para>
    /// It is three pages for every target (pick what → pick/define it → pick the scope), and only the
    /// third one commits: <c>Advance</c> calls <c>Commit()</c> when <c>IsLastPage</c>. So the pass walks
    /// both branches that have controls of their own, stops <i>on</i> the scope page, and leaves by
    /// Cancel — the wizard's writes land in the throwaway config dir either way, but a Finish would end
    /// the wizard and there would be nothing left to check.
    /// </para>
    /// </summary>
    private void RunDefineNewWizardPass()
    {
        if (!OpenDefineNewWizard("hello.txt"))
        {
            Check("the Define New wizard opens from the action strip", () => false);
            return;
        }

        // ── Page 1 of 3: what to associate the file with ──
        CheckPresent("Wizard: an internal viewer", "DefineNew_TargetInternal");
        // Present-checked, never chosen: it is IsEnabled-bound to HasExistingApps, and a journey's
        // config dir has no external apps in it — and CheckInvoke counts a disabled control as a failure.
        CheckPresent("Wizard: an existing external app", "DefineNew_TargetExistingApp");
        CheckPresent("Wizard: a new external app", "DefineNew_TargetNewApp");

        // ── The new-external-app branch, which is where the Browse buttons live ──
        Choose("A new external app", "DefineNew_TargetNewApp");
        CheckDoes("Next reaches the app-details page", "DefineNew_Advance",
                  () => WaitForId("DefineNew_BrowseAppPath", 6) is not null);

        // Both Browse buttons open a modal shell picker that is a window of its own, so FlaUI cannot
        // see or dismiss it from MainWindow — pressing either would hang the rest of the pass with a
        // dialog nothing in the test can close. Present-checked on purpose.
        CheckPresent("Browse for the app (a modal picker, so not pressed)", "DefineNew_BrowseAppPath");
        CheckInvoke("Expand the advanced fields", "DefineNew_ToggleAdvanced");
        CheckPresent("Browse for an icon (the same modal picker)", "DefineNew_BrowseIconPath");

        CheckDoes("Back returns to the first page", "DefineNew_Back",
                  () => WaitForId("DefineNew_TargetInternal", 6) is not null);

        // ── The internal-viewer branch, through to the scope page ──
        Choose("An internal viewer", "DefineNew_TargetInternal");
        CheckDoes("Next reaches the viewer picker", "DefineNew_Advance",
                  () => WaitForId("DefineNew_InternalPicker", 6) is not null);

        // Page 2 will not advance until an experience is selected (CanAdvance → SelectedExperience is
        // not null), so the pass picks the first one rather than assuming one is preselected.
        Check("the picker offers a viewer to choose", ChooseFirstInternalViewer);

        CheckDoes("Next reaches the scope page", "DefineNew_Advance",
                  () => WaitForId("DefineNew_ScopeThisFile", 6) is not null);

        // Each scope is a plain view-model flip that only BuildCriteria() reads, and BuildCriteria is
        // only called by Commit — so all four can be moved through freely.
        Choose("Scope: this file only",        "DefineNew_ScopeThisFile");
        Choose("Scope: the extension here",    "DefineNew_ScopeExtInFolder");
        Choose("Scope: the extension anywhere","DefineNew_ScopeExtAnywhere");
        Choose("Scope: custom globs",          "DefineNew_ScopeCustomGlobs");

        CheckDoes("Cancel closes the wizard", "DefineNew_Cancel",
                  () => WaitForGone("DefineNew_ScopeCustomGlobs", 5));
    }

    /// <summary>
    /// Opens the wizard the only way the app offers: a right-click on EMPTY action-strip space, which
    /// raises a runtime ContextMenu holding a single "Define New…" item. False when any step does not
    /// happen, so the caller can report one failure rather than a cascade of missing wizard controls.
    /// <para>
    /// It selects a file first, and that is not incidental: <c>CanOpenDefineNewWizard</c> requires exactly
    /// one selected non-directory item, so with nothing selected the command cannot execute and the handler
    /// raises no menu at all — and the strip has no action buttons to measure from either.
    /// </para>
    /// <para>
    /// The strip is a <c>Border</c>, and WPF gives a Border no automation peer, so its position is read
    /// from a button inside it instead. "Empty" is the space <i>below</i> the buttons: the strip is a
    /// fixed 120px column whose actions fill a two-column grid from the top, and the handler walks up
    /// from whatever was hit and bails if it finds a Button, so a click on one raises nothing.
    /// </para>
    /// </summary>
    private bool OpenDefineNewWizard(string fileName)
    {
        var list = WaitForId("FileListView", 8);
        if (list is null) return false;

        var row = WaitFor(() => list.FindFirstDescendant(cf => cf.ByName(fileName)), 8);
        if (row is null) return false;

        row.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        row.Click();
        Wait.UntilInputIsProcessed();
        System.Threading.Thread.Sleep(250);

        var anchor = WaitForId("Copy", 8) ?? WaitForId("Rename", 8) ?? WaitForId("New", 8);
        if (anchor is null) return false;

        var x = anchor.BoundingRectangle.Left + (anchor.BoundingRectangle.Width / 2);
        var y = list.BoundingRectangle.Bottom - 24;
        if (y <= anchor.BoundingRectangle.Bottom + 8) return false;   // no empty strip to right-click

        Mouse.RightClick(new System.Drawing.Point(x, y));
        Wait.UntilInputIsProcessed();
        System.Threading.Thread.Sleep(300);

        var item = FindDefineNewItem();
        if (item is null) return false;

        item.Click();
        Wait.UntilInputIsProcessed();
        return WaitForId("DefineNew_TargetInternal", 8) is not null;
    }

    /// <summary>
    /// Selects one of the wizard's radio options and records whether the selection took. Radios answer
    /// the SelectionItem pattern rather than Invoke, so this asks directly instead of going through
    /// <see cref="UiJourneyTestBase.CheckInvoke"/>, which would fall back to a coordinate click.
    /// </summary>
    private void Choose(string label, string automationId)
    {
        Check($"{label} can be chosen", () =>
        {
            var option = WaitForId(automationId, 5);
            var pattern = option?.Patterns.SelectionItem.PatternOrDefault;
            if (pattern is null) return false;

            pattern.Select();
            Wait.UntilInputIsProcessed();
            System.Threading.Thread.Sleep(120);
            return pattern.IsSelected.ValueOrDefault;
        });
    }

    /// <summary>Selects the first viewer in the wizard's internal-viewer list, so Next becomes available.</summary>
    private bool ChooseFirstInternalViewer()
    {
        var picker = WaitForId("DefineNew_InternalPicker", 5);
        var first = picker?.FindAllChildren().FirstOrDefault();
        if (first is null) return false;

        first.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView();
        first.Patterns.SelectionItem.PatternOrDefault?.Select();
        Wait.UntilInputIsProcessed();
        System.Threading.Thread.Sleep(150);
        return true;
    }

    /// <summary>
    /// Finds the "Define New…" menu item. A WPF ContextMenu is hosted in a Popup with its own HWND, and
    /// whether that shows up under the shell window or as a top-level window of the process depends on
    /// how it was raised — this one is built in code-behind rather than declared in XAML — so both are
    /// searched rather than assuming the one the other journeys happen to hit.
    /// </summary>
    private AutomationElement? FindDefineNewItem()
    {
        const string Header = "Define New…";

        var inWindow = WaitForName(Header, 3);
        if (inWindow is not null) return inWindow;

        return WaitFor(() =>
        {
            foreach (var w in App.GetAllTopLevelWindows(Automation))
            {
                if (w.FindFirstDescendant(cf => cf.ByName(Header)) is { } hit) return hit;
            }
            return null;
        }, 3);
    }
}

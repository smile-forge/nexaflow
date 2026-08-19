using System;
using System.IO;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem.UI;

/// <summary>
/// The one UI journey for the file browser: the tab loads, the create overlay makes each kind of thing it
/// offers, and a template defined in Options turns up in that overlay and produces a file with the right
/// content.
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

    /// <summary>Escape in the filename box cancels the overlay — the way out when a flow leaves it open.</summary>
    private void DismissCreateOverlay()
    {
        var box = WaitForId("CreateFileNameBox", 4);
        if (box is null) return;

        box.Click();
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed();
    }

    private void Confirm()
    {
        WaitForId("CreateConfirmButton")?.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
    }

    [TestMethod]
    [CoversNode("win-file-system-ui")]
    [CoversNode("winfs-create")]
    [CoversNode("winfs-create-template")]
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
                    MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("CreateConfirmButton"))
                        is { } create && !create.Properties.IsEnabled.ValueOrDefault);
            }

            DismissCreateOverlay();   // nothing was created, and the next flow needs the overlay closed
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

        AssertJourney();
    }
}

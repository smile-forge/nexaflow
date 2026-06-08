using System;
using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;

namespace Nexaflow.Tests.Features.WindowsFileSystem.UI;

/// <summary>
/// Drives the new-file create flow against a real temp folder, reached by typing the folder
/// path into the AI bar (the file browser's existing path-navigation route). Requires an
/// interactive desktop — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
public class FileSystemCreateTests : FileSystemUiTestBase
{
    private string _folder = null!;

    protected override void OnUISetup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "nexaflow-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "existing.txt"), "x");   // for the existence-warning test
        NavigateFileBrowserTo(_folder);
    }

    [TestCleanup]
    public void CleanupFolder()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); } catch { }
    }

    [TestMethod]
    public void NewFolder_CreatesDirectory()
    {
        OpenCreateOverlayAndPick("Folder");

        var box = WaitForId("CreateFileNameBox");
        Assert.IsNotNull(box, "Filename box missing.");
        box!.AsTextBox().Text = "MadeByTest";

        WaitForId("CreateConfirmButton")!.AsButton().Invoke();
        Wait.UntilInputIsProcessed();

        Assert.IsTrue(WaitForFs(() => Directory.Exists(Path.Combine(_folder, "MadeByTest"))),
            "Folder was not created.");
    }

    [TestMethod]
    public void NewTextFile_PrefillsName_AndCreatesFile()
    {
        OpenCreateOverlayAndPick("Text File");

        var box = WaitForId("CreateFileNameBox");
        Assert.IsNotNull(box, "Filename box missing.");
        Assert.AreEqual("New File.txt", box!.AsTextBox().Text);

        box.AsTextBox().Text = "hello.txt";
        WaitForId("CreateConfirmButton")!.AsButton().Invoke();
        Wait.UntilInputIsProcessed();

        Assert.IsTrue(WaitForFs(() => File.Exists(Path.Combine(_folder, "hello.txt"))),
            "Text file was not created.");
    }

    [TestMethod]
    public void ExistingName_ShowsWarning_AndDisablesCreate()
    {
        OpenCreateOverlayAndPick("Text File");

        var box = WaitForId("CreateFileNameBox");
        Assert.IsNotNull(box, "Filename box missing.");
        box!.AsTextBox().Text = "existing";   // resolves to the seeded existing.txt

        var warning = WaitForId("CreateNameWarning", 4);
        Assert.IsNotNull(warning, "Warning should be visible for an existing name.");

        var create = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("CreateConfirmButton"));
        Assert.IsNotNull(create);
        Assert.IsFalse(create!.Properties.IsEnabled.ValueOrDefault,
            "Create should be disabled for a name that already exists.");
    }

    private void OpenCreateOverlayAndPick(string typeName)
    {
        var newBtn = WaitForId("New", 8);
        Assert.IsNotNull(newBtn, "The 'New' button did not appear in the action strip.");
        newBtn!.AsButton().Invoke();

        Assert.IsNotNull(WaitForId("CreateOverlay", 6), "Create overlay did not open.");

        var typeBtn = WaitForId(typeName, 6);
        Assert.IsNotNull(typeBtn, $"Create type '{typeName}' not found.");
        typeBtn!.AsButton().Invoke();
        Wait.UntilInputIsProcessed();
    }
}

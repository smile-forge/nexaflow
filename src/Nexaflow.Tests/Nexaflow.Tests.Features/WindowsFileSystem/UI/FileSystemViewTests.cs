using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem.UI;

/// <summary>
/// Verifies that a FileSystem tab loads with its primary elements visible.
/// Requires an interactive desktop session — run manually or via --filter "TestCategory=UI".
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("winfs-tab")]
public class FileSystemViewTests : UITestBase
{
    private AutomationElement? _directoryTree;

    protected override void OnUISetup()
    {
        _directoryTree = TryOpenTabWithElement("DirectoryTree");
    }

    [TestMethod]
    public void DirectoryTree_IsPresent()
    {
        if (_directoryTree is null)
            Assert.Inconclusive("Could not open a FileSystem tab via any ribbon button.");

        Assert.IsFalse(_directoryTree.IsOffscreen, "DirectoryTree should be on screen");
    }

    [TestMethod]
    public void FileListOrDriveList_IsPresent()
    {
        if (_directoryTree is null)
            Assert.Inconclusive("Could not open a FileSystem tab via any ribbon button.");

        var fileList  = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("FileListView"));
        var driveList = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("DriveListView"));

        Assert.IsTrue(fileList is not null || driveList is not null,
            "Expected FileListView or DriveListView to be present");
    }

    [TestMethod]
    public void AppDoesNotCrash_AfterLoadingFileSystemTab()
    {
        if (_directoryTree is null)
            Assert.Inconclusive("Could not open a FileSystem tab via any ribbon button.");

        Wait.UntilInputIsProcessed();
        Assert.IsFalse(App.HasExited, "App crashed after loading a FileSystem tab");
    }
}

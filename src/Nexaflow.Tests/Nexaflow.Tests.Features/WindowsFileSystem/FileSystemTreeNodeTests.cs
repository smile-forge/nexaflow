using System.ComponentModel;
using Nexaflow.Features.WindowsFileSystem.ViewModels;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
public class FileSystemTreeNodeTests
{
    // A path that does not exist — guarantees no filesystem side-effects.
    private const string FakePath = @"Z:\DoesNotExist\";

    // ── Construction ─────────────────────────────────────────────────────────

    [TestMethod]
    public void FolderNode_NonExistentPath_NoChildren()
    {
        var node = new FileSystemTreeNode("docs", FakePath);

        // Non-existent path → no subdir check succeeds → no Dummy child
        Assert.AreEqual(0, node.Children.Count);
    }

    [TestMethod]
    public void FolderNode_Kind_IsFolder()
    {
        var node = new FileSystemTreeNode("docs", FakePath);

        Assert.AreEqual(TreeNodeKind.Folder, node.Kind);
    }

    [TestMethod]
    public void DriveNode_Kind_IsDrive()
    {
        var node = new FileSystemTreeNode("C:", @"C:\", TreeNodeKind.Drive);

        Assert.AreEqual(TreeNodeKind.Drive, node.Kind);
    }

    [TestMethod]
    public void DriveNode_InitialStatus_IsLoading()
    {
        var node = new FileSystemTreeNode("C:", @"C:\", TreeNodeKind.Drive);

        Assert.AreEqual(DriveStatus.Loading, node.DriveStatus);
    }

    [TestMethod]
    public void ThisPcNode_Kind_IsThisPc()
    {
        var node = new FileSystemTreeNode("This PC", string.Empty, TreeNodeKind.ThisPc);

        Assert.AreEqual(TreeNodeKind.ThisPc, node.Kind);
    }

    [TestMethod]
    public void FolderNode_NameAndFullPath_Stored()
    {
        var node = new FileSystemTreeNode("docs", FakePath);

        Assert.AreEqual("docs", node.Name);
        Assert.AreEqual(FakePath, node.FullPath);
    }

    // ── IsExpanded guard on drive nodes ──────────────────────────────────────

    [TestMethod]
    public void IsExpanded_DriveLoading_BlocksExpansion()
    {
        var node = new FileSystemTreeNode("C:", @"C:\", TreeNodeKind.Drive);
        // DriveStatus starts as Loading

        node.IsExpanded = true;

        Assert.IsFalse(node.IsExpanded);
    }

    [TestMethod]
    public void IsExpanded_DriveUnavailable_BlocksExpansion()
    {
        var node = new FileSystemTreeNode("D:", @"D:\", TreeNodeKind.Drive);
        node.DriveStatus = DriveStatus.Unavailable;

        node.IsExpanded = true;

        Assert.IsFalse(node.IsExpanded);
    }

    [TestMethod]
    public void IsExpanded_DriveReady_AllowsExpansion()
    {
        var node = new FileSystemTreeNode("C:", @"C:\", TreeNodeKind.Drive);
        node.DriveStatus = DriveStatus.Ready;

        node.IsExpanded = true;

        Assert.IsTrue(node.IsExpanded);
    }

    [TestMethod]
    public void IsExpanded_FolderNode_AllowsExpansion()
    {
        var node = new FileSystemTreeNode("docs", FakePath);

        node.IsExpanded = true;

        Assert.IsTrue(node.IsExpanded);
    }

    [TestMethod]
    public void IsExpanded_SetSameValue_NoChangeEvent()
    {
        var node  = new FileSystemTreeNode("docs", FakePath);
        var fired = new List<string?>();
        ((INotifyPropertyChanged)node).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        node.IsExpanded = false; // already false

        Assert.IsFalse(fired.Contains(nameof(FileSystemTreeNode.IsExpanded)));
    }

    // ── PropertyChanged ───────────────────────────────────────────────────────

    [TestMethod]
    public void Name_Set_FiresPropertyChanged()
    {
        var node  = new FileSystemTreeNode("old", FakePath);
        var fired = new List<string?>();
        ((INotifyPropertyChanged)node).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        node.Name = "new";

        CollectionAssert.Contains(fired, nameof(FileSystemTreeNode.Name));
    }

    [TestMethod]
    public void DriveStatus_Set_FiresPropertyChanged()
    {
        var node  = new FileSystemTreeNode("C:", @"C:\", TreeNodeKind.Drive);
        var fired = new List<string?>();
        ((INotifyPropertyChanged)node).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        node.DriveStatus = DriveStatus.Ready;

        CollectionAssert.Contains(fired, nameof(FileSystemTreeNode.DriveStatus));
    }

    [TestMethod]
    public void IsSelected_Set_FiresPropertyChanged()
    {
        var node  = new FileSystemTreeNode("docs", FakePath);
        var fired = new List<string?>();
        ((INotifyPropertyChanged)node).PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        node.IsSelected = true;

        CollectionAssert.Contains(fired, nameof(FileSystemTreeNode.IsSelected));
    }
}

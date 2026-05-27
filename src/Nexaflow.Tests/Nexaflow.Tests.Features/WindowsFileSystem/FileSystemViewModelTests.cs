using System.IO;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ViewModels;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
public class FileSystemViewModelTests
{
    // ── Factory helpers ───────────────────────────────────────────────────────

    private static (IShellServices Shell, IAIService Ai, IReadOnlyDictionary<Type, IFeatureConfig> Configs) Deps()
    {
        var shell = Substitute.For<IShellServices>();
        var ai    = Substitute.For<IAIService>();

        // Registry calls DiscoverImplementations for each contract type.
        shell.DiscoverImplementations<IFileAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFileCreateAction>().Returns(Array.Empty<Type>());
        shell.DiscoverImplementations<IFolderViewlet>().Returns(Array.Empty<Type>());

        return (shell, ai, new Dictionary<Type, IFeatureConfig>());
    }

    private static FileSystemViewModel ThisPc()
    {
        var (shell, ai, configs) = Deps();
        return FileSystemViewModel.CreateThisPc(shell, ai, configs);
    }

    private static FileSystemViewModel AtPath(string path)
    {
        var (shell, ai, configs) = Deps();
        return new FileSystemViewModel(path, shell, ai, configs);
    }

    // ── CreateThisPc ─────────────────────────────────────────────────────────

    [TestMethod]
    public void CreateThisPc_IsThisPcMode_True()
    {
        var vm = ThisPc();

        Assert.IsTrue(vm.IsThisPcMode);
    }

    [TestMethod]
    public void CreateThisPc_CurrentPath_Empty()
    {
        var vm = ThisPc();

        Assert.AreEqual(string.Empty, vm.CurrentPath);
    }

    [TestMethod]
    public void CreateThisPc_TreeRoots_ContainsThisPcNode()
    {
        var vm = ThisPc();

        Assert.AreEqual(1, vm.TreeRoots.Count);
        Assert.AreEqual(TreeNodeKind.ThisPc, vm.TreeRoots[0].Kind);
    }

    [TestMethod]
    public void CreateThisPc_Entries_ContainsDrives()
    {
        var vm = ThisPc();

        Assert.IsTrue(vm.Entries.Count > 0, "Expected at least one drive entry");
        Assert.IsTrue(vm.Entries.All(e => e.IsDrive));
    }

    // ── Path constructor ──────────────────────────────────────────────────────

    [TestMethod]
    public void PathConstructor_SetsCurrentPath()
    {
        var path = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var vm   = AtPath(path);

        Assert.AreEqual(path, vm.CurrentPath.TrimEnd(Path.DirectorySeparatorChar),
            StringComparer.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void PathConstructor_IsThisPcMode_False()
    {
        var vm = AtPath(Path.GetTempPath());

        Assert.IsFalse(vm.IsThisPcMode);
    }

    // ── IPageViewModel – GetContext ────────────────────────────────────────────

    [TestMethod]
    public void GetContext_ThisPcMode_MentionsDrives()
    {
        var vm = ThisPc();

        StringAssert.Contains(vm.GetContext(), "This PC");
    }

    [TestMethod]
    public void GetContext_PathMode_ContainsCurrentPath()
    {
        var path = Path.GetTempPath();
        var vm   = AtPath(path);

        StringAssert.Contains(vm.GetContext(), vm.CurrentPath);
    }

    // ── IPageViewModel – GetAvailableActions ──────────────────────────────────

    [TestMethod]
    public void GetAvailableActions_ContainsNavigate()
    {
        var vm = ThisPc();

        Assert.IsTrue(vm.GetAvailableActions().Any(a => a.Name == "navigate"));
    }

    [TestMethod]
    public void GetAvailableActions_ContainsGotoRoot()
    {
        var vm = ThisPc();

        Assert.IsTrue(vm.GetAvailableActions().Any(a => a.Name == "gotoRoot"));
    }

    // ── NavigateTo ────────────────────────────────────────────────────────────

    [TestMethod]
    public void NavigateTo_ValidPath_SetsCurrentPath()
    {
        var vm   = ThisPc();
        var path = Path.GetTempPath();

        vm.NavigateTo(path);

        Assert.AreEqual(path, vm.CurrentPath,
            StringComparer.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void NavigateTo_ValidPath_IsThisPcMode_False()
    {
        var vm = ThisPc();

        vm.NavigateTo(Path.GetTempPath());

        Assert.IsFalse(vm.IsThisPcMode);
    }

    [TestMethod]
    public void NavigateTo_NonExistentPath_DoesNotChangeCurrentPath()
    {
        var vm = ThisPc();

        vm.NavigateTo(@"Z:\DoesNotExist\NeverWillExist");

        Assert.AreEqual(string.Empty, vm.CurrentPath);
    }

    // ── OnSelectionChanged ────────────────────────────────────────────────────

    [TestMethod]
    public void OnSelectionChanged_Empty_ClearsSelectedEntry()
    {
        var vm = ThisPc();

        vm.OnSelectionChanged([]);

        Assert.IsNull(vm.SelectedEntry);
    }

    [TestMethod]
    public void OnSelectionChanged_SingleEntry_SetsSelectedEntry()
    {
        var vm    = ThisPc();
        var entry = new FileSystemEntry { Name = "test.txt", FullPath = @"C:\test.txt" };

        vm.OnSelectionChanged([entry]);

        Assert.AreSame(entry, vm.SelectedEntry);
    }

    [TestMethod]
    public void OnSelectionChanged_MultipleEntries_SelectedEntry_IsNull()
    {
        var vm     = ThisPc();
        var entry1 = new FileSystemEntry { Name = "a.txt", FullPath = @"C:\a.txt" };
        var entry2 = new FileSystemEntry { Name = "b.txt", FullPath = @"C:\b.txt" };

        vm.OnSelectionChanged([entry1, entry2]);

        Assert.IsNull(vm.SelectedEntry);
    }

    [TestMethod]
    public void OnSelectionChanged_Empty_EntryCountLabel_ZeroItems()
    {
        var vm = ThisPc();

        vm.OnSelectionChanged([]);

        Assert.IsNotNull(vm.EntryCountLabel);
    }

    // ── Confirmation overlay ──────────────────────────────────────────────────

    [TestMethod]
    public void ShowConfirmation_SetsVisibleTrue()
    {
        var vm = ThisPc();

        vm.ShowConfirmation("Delete?", () => { }, () => { });

        Assert.IsTrue(vm.ConfirmationVisible);
    }

    [TestMethod]
    public void ShowConfirmation_SetsPrompt()
    {
        var vm = ThisPc();

        vm.ShowConfirmation("Are you sure you want to delete?", () => { }, () => { });

        StringAssert.Contains(vm.ConfirmationPrompt, "delete");
    }

    [TestMethod]
    public void ConfirmAction_InvokesCallbackAndHidesOverlay()
    {
        var vm       = ThisPc();
        bool invoked = false;
        vm.ShowConfirmation("Delete?", () => invoked = true, () => { });

        vm.ConfirmActionCommand.Execute(null);

        Assert.IsTrue(invoked, "Confirm callback was not invoked");
        Assert.IsFalse(vm.ConfirmationVisible);
    }

    [TestMethod]
    public void CancelConfirmation_InvokesCancelCallbackAndHidesOverlay()
    {
        var vm          = ThisPc();
        bool cancelled  = false;
        vm.ShowConfirmation("Delete?", () => { }, () => cancelled = true);

        vm.CancelConfirmationCommand.Execute(null);

        Assert.IsTrue(cancelled, "Cancel callback was not invoked");
        Assert.IsFalse(vm.ConfirmationVisible);
    }

    // ── Input prompt overlay ──────────────────────────────────────────────────

    [TestMethod]
    public void ShowInputPrompt_SetsVisibleTrue()
    {
        var vm = ThisPc();

        vm.ShowInputPrompt("Rename", "New name:", "old.txt", _ => { }, () => { });

        Assert.IsTrue(vm.InputPromptVisible);
    }

    [TestMethod]
    public void ShowInputPrompt_SetsInitialValue()
    {
        var vm = ThisPc();

        vm.ShowInputPrompt("Rename", "New name:", "old.txt", _ => { }, () => { });

        Assert.AreEqual("old.txt", vm.InputPromptValue);
    }

    [TestMethod]
    public void ConfirmInputPrompt_InvokesCallbackWithValue()
    {
        var vm            = ThisPc();
        string? received  = null;
        vm.ShowInputPrompt("Rename", "New name:", "old.txt", v => received = v, () => { });
        vm.InputPromptValue = "new.txt";

        vm.ConfirmInputPromptCommand.Execute(null);

        Assert.AreEqual("new.txt", received);
        Assert.IsFalse(vm.InputPromptVisible);
    }

    [TestMethod]
    public void CancelInputPrompt_InvokesCancelCallbackAndHidesOverlay()
    {
        var vm         = ThisPc();
        bool cancelled = false;
        vm.ShowInputPrompt("Rename", "New name:", "old.txt", _ => { }, () => cancelled = true);

        vm.CancelInputPromptCommand.Execute(null);

        Assert.IsTrue(cancelled);
        Assert.IsFalse(vm.InputPromptVisible);
    }

    // ── NavigationChanged event ───────────────────────────────────────────────

    [TestMethod]
    public void NavigateTo_ValidPath_FiresNavigationChanged()
    {
        var vm    = ThisPc();
        bool fired = false;
        vm.NavigationChanged += _ => fired = true;

        vm.NavigateTo(Path.GetTempPath());

        Assert.IsTrue(fired, "NavigationChanged was not raised");
    }

    // ── ResetRootToCurrentPath ────────────────────────────────────────────────

    [TestMethod]
    public void ResetRootToCurrentPath_UpdatesRootPath()
    {
        var path = Path.GetTempPath();
        var vm   = AtPath(path);

        vm.ResetRootToCurrentPath();

        Assert.AreEqual(path, vm.RootPath,
            StringComparer.OrdinalIgnoreCase);
    }
}

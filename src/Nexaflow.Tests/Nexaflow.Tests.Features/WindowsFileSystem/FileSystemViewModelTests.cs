using System.IO;
using System.Text.Json.Nodes;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.WindowsFileSystem.ClientTools;
using Nexaflow.Features.WindowsFileSystem.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.WindowsFileSystem;

[TestClass]
[CoversNode("win-file-system-ai-act")]
public class FileSystemViewModelTests
{
    // A fresh empty folder per test method (MSTest builds a new class instance for each). These tests used
    // to browse the machine's %TEMP% directly — thousands of entries that every other test creates and
    // deletes, so the streaming loader published different counts between two GetContext() calls.
    private string _scratch = string.Empty;

    [TestInitialize]
    public void CreateScratch() => _scratch = TempDir();

    [TestCleanup]
    public void RemoveScratch() { try { Directory.Delete(_scratch, recursive: true); } catch { } }

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
        Assert.IsTrue(vm.Entries.All(e => e.IsThisPcItem));
    }

    // ── Path constructor ──────────────────────────────────────────────────────

    [TestMethod]
    public void PathConstructor_SetsCurrentPath()
    {
        var path = _scratch;
        var vm   = AtPath(path);

        Assert.AreEqual(path, vm.CurrentPath.TrimEnd(Path.DirectorySeparatorChar),
            StringComparer.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void PathConstructor_IsThisPcMode_False()
    {
        var vm = AtPath(_scratch);

        Assert.IsFalse(vm.IsThisPcMode);
    }

    // ── IPageViewModel – GetContext ────────────────────────────────────────────

    [TestMethod]
    [CoversNode("win-file-system-ai-context")]
    public void GetContext_ThisPcMode_MentionsDrives()
    {
        var vm = ThisPc();

        StringAssert.Contains(vm.GetContext(), "This PC");
    }

    [TestMethod]
    public void GetContext_PathMode_ContainsCurrentPath()
    {
        var path = _scratch;
        var vm   = AtPath(path);

        StringAssert.Contains(vm.GetContext(), vm.CurrentPath);
    }

    // ── IPageViewModel – GetClientTools ───────────────────────────────────────

    [TestMethod]
    public void GetClientTools_ContainsCreateTextFile()
    {
        var vm = ThisPc();

        Assert.IsTrue(vm.GetClientTools().Any(t => t.Name == "create_text_file"));
    }

    [TestMethod]
    public void GetClientTools_ContainsGetFileList()
    {
        var vm = ThisPc();

        Assert.IsTrue(vm.GetClientTools().Any(t => t.Name == "get_file_list"));
    }

    // ── IPageViewModel – viewlet AI surfaces ──────────────────────────────────

    private sealed class StubViewletSurface(string? context, params IClientTool[] tools) : IViewletAiSurface
    {
        public string? GetContext() => context;
        public IReadOnlyList<IClientTool> GetClientTools() => tools;
    }

    private static IClientTool StubTool(string name) =>
        new DelegateClientTool(name, "stub", [], ToolSafety.SafeOperation,
            (_, _) => Task.FromResult(ToolResult.Ok("ok")));

    [TestMethod]
    public void GetContext_WithViewletSurface_AppendsItsLine()
    {
        var vm = AtPath(_scratch);
        vm.SetActiveViewletSurfaces([new StubViewletSurface("STUB-VIEWLET-LINE")]);

        StringAssert.Contains(vm.GetContext(), "STUB-VIEWLET-LINE");
    }

    [TestMethod]
    public void GetContext_BlankSurfaceContext_AddsNothing()
    {
        var vm   = AtPath(_scratch);
        var bare = vm.GetContext();

        vm.SetActiveViewletSurfaces([new StubViewletSurface(null), new StubViewletSurface("   ")]);

        Assert.AreEqual(bare, vm.GetContext());
    }

    [TestMethod]
    public void GetContext_ThisPcMode_IgnoresSurfaces()
    {
        var vm = ThisPc();
        vm.SetActiveViewletSurfaces([new StubViewletSurface("STUB-VIEWLET-LINE")]);

        Assert.IsFalse(vm.GetContext().Contains("STUB-VIEWLET-LINE"));
    }

    [TestMethod]
    public void GetClientTools_WithViewletSurface_IncludesSurfaceAndFileTools()
    {
        var vm = AtPath(_scratch);
        vm.SetActiveViewletSurfaces([new StubViewletSurface(null, StubTool("stub_tool"))]);

        var names = vm.GetClientTools().Select(t => t.Name).ToList();
        CollectionAssert.Contains(names, "stub_tool");        // contributed by the viewlet
        CollectionAssert.Contains(names, "get_file_list");    // existing file tools still present
    }

    [TestMethod]
    public void GetClientTools_NoSurfaces_ExcludesSurfaceTools()
    {
        var vm = AtPath(_scratch);

        Assert.IsFalse(vm.GetClientTools().Any(t => t.Name == "stub_tool"));
    }

    // ── NavigateTo ────────────────────────────────────────────────────────────

    [TestMethod]
    public void NavigateTo_ValidPath_SetsCurrentPath()
    {
        var vm   = ThisPc();
        var path = _scratch;

        vm.NavigateTo(path);

        Assert.AreEqual(path, vm.CurrentPath,
            StringComparer.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void NavigateTo_ValidPath_IsThisPcMode_False()
    {
        var vm = ThisPc();

        vm.NavigateTo(_scratch);

        Assert.IsFalse(vm.IsThisPcMode);
    }

    [TestMethod]
    public void NavigateTo_NonExistentPath_DoesNotChangeCurrentPath()
    {
        var vm = ThisPc();

        vm.NavigateTo(@"Z:\DoesNotExist\NeverWillExist");

        Assert.AreEqual(string.Empty, vm.CurrentPath);
    }

    // ── NearestExistingAncestor (re-home a tab after its folder is deleted) ─────

    [TestMethod]
    public void NearestExistingAncestor_LeafGone_ReturnsDeepestExistingParent()
    {
        var root     = Path.Combine(Path.GetTempPath(), "nexanav_" + Guid.NewGuid().ToString("N"));
        var existing = Path.Combine(root, "a", "b");
        Directory.CreateDirectory(existing);
        try
        {
            // Several levels are gone (the worktree + anything under it) — walk up to the surviving one.
            var gone = Path.Combine(existing, "gone", "deeper");
            Assert.AreEqual(existing, FileSystemViewModel.NearestExistingAncestor(gone));
        }
        finally { Directory.Delete(root, recursive: true); }
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

        vm.NavigateTo(_scratch);

        Assert.IsTrue(fired, "NavigationChanged was not raised");
    }

    // ── ResetRootToCurrentPath ────────────────────────────────────────────────

    [TestMethod]
    public void ResetRootToCurrentPath_UpdatesRootPath()
    {
        var path = _scratch;
        var vm   = AtPath(path);

        vm.ResetRootToCurrentPath();

        Assert.AreEqual(path, vm.RootPath,
            StringComparer.OrdinalIgnoreCase);
    }

    // ── Client tools (invocation) ──────────────────────────────────────────────

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexafs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public async Task CreateTextFileTool_WritesFileWithContent()
    {
        var dir = TempDir();
        try
        {
            var vm     = AtPath(dir);
            var result = await new CreateTextFileTool(vm).InvokeAsync(
                new JsonObject { ["name"] = "note.txt", ["content"] = "hello world" }, default);

            Assert.IsTrue(result.Success);
            var path = Path.Combine(dir, "note.txt");
            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual("hello world", File.ReadAllText(path));
            Assert.IsNotNull(result.Attachments);
            Assert.IsTrue(result.Attachments!.Any(p => p.EndsWith("note.txt")), "created file should be reported as an attachment");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public async Task CreateTextFileTool_RefusesOverwriteWithoutFlag()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "exists.txt"), "old");
            var vm     = AtPath(dir);
            var result = await new CreateTextFileTool(vm).InvokeAsync(
                new JsonObject { ["name"] = "exists.txt", ["content"] = "new" }, default);

            Assert.IsTrue(result.IsError);
            Assert.AreEqual("old", File.ReadAllText(Path.Combine(dir, "exists.txt")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public async Task FindFilesByNameTool_MatchesPattern()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "george-list.txt"), "x");
            File.WriteAllText(Path.Combine(dir, "other.txt"), "x");
            var vm     = AtPath(dir);
            var result = await new FindFilesByNameTool(vm).InvokeAsync(
                new JsonObject { ["pattern"] = "george*" }, default);

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.ModelText, "george-list.txt");
            Assert.IsFalse(result.ModelText.Contains("other.txt"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public async Task GetFileContentsTool_ReadsText()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "data.txt"), "file body here");
            var vm     = AtPath(dir);
            var result = await new GetFileContentsTool(vm).InvokeAsync(
                new JsonObject { ["name"] = "data.txt" }, default);

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.ModelText, "file body here");
            Assert.IsNotNull(result.Attachments);
            Assert.IsTrue(result.Attachments!.Any(p => p.EndsWith("data.txt")), "read file should be reported as an attachment");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    public async Task GetFileContentsTool_RejectsTraversal()
    {
        var dir = TempDir();
        try
        {
            var vm     = AtPath(dir);
            var result = await new GetFileContentsTool(vm).InvokeAsync(
                new JsonObject { ["name"] = @"..\secret.txt" }, default);

            Assert.IsTrue(result.IsError);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Write/act tools (invocation) ───────────────────────────────────────────

    [TestMethod]
    [CoversNode("win-file-system-ai-act-rename-file")]
    public async Task RenameFileTool_RenamesFileOnDisk()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "old.txt"), "body");
            var vm     = AtPath(dir);
            var result = await new RenameTool(vm).InvokeAsync(
                new JsonObject { ["name"] = "old.txt", ["new_name"] = "new.txt" }, default);

            Assert.IsTrue(result.Success);
            Assert.IsFalse(File.Exists(Path.Combine(dir, "old.txt")), "old name should be gone");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "new.txt")), "new name should exist");
            Assert.AreEqual("body", File.ReadAllText(Path.Combine(dir, "new.txt")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("win-file-system-ai-act-rename-file")]
    public async Task RenameFileTool_RefusesExistingName()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "a");
            File.WriteAllText(Path.Combine(dir, "b.txt"), "b");
            var vm     = AtPath(dir);
            var result = await new RenameTool(vm).InvokeAsync(
                new JsonObject { ["name"] = "a.txt", ["new_name"] = "b.txt" }, default);

            Assert.IsTrue(result.IsError);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "a.txt")), "source must be untouched");
            Assert.AreEqual("b", File.ReadAllText(Path.Combine(dir, "b.txt")), "target must be untouched");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("win-file-system-ai-act-copy-file")]
    public async Task CopyFileTool_CopiesFileLeavingSource()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "src.txt"), "payload");
            var vm     = AtPath(dir);
            var result = await new CopyTool(vm).InvokeAsync(
                new JsonObject { ["source"] = "src.txt", ["destination"] = "copy.txt" }, default);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "src.txt")), "source should remain");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "copy.txt")), "copy should exist");
            Assert.AreEqual("payload", File.ReadAllText(Path.Combine(dir, "copy.txt")));
            Assert.IsNotNull(result.Attachments);
            Assert.IsTrue(result.Attachments!.Any(p => p.EndsWith("copy.txt")), "copy should be reported as an attachment");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("win-file-system-ai-act-move-file")]
    public async Task MoveFileTool_MovesFileOnDisk()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "src.txt"), "payload");
            var vm     = AtPath(dir);
            var result = await new MoveTool(vm).InvokeAsync(
                new JsonObject { ["source"] = "src.txt", ["destination"] = "moved.txt" }, default);

            Assert.IsTrue(result.Success);
            Assert.IsFalse(File.Exists(Path.Combine(dir, "src.txt")), "source should be gone");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "moved.txt")), "destination should exist");
            Assert.AreEqual("payload", File.ReadAllText(Path.Combine(dir, "moved.txt")));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("win-file-system-ai-act-delete-file")]
    public async Task DeleteFileTool_RemovesFileFromDisk()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "trash.txt");
            File.WriteAllText(path, "junk");
            var vm     = AtPath(dir);
            var result = await new DeleteTool(vm).InvokeAsync(
                new JsonObject { ["name"] = "trash.txt" }, default);

            Assert.IsTrue(result.Success);
            Assert.IsFalse(File.Exists(path), "file should be removed from its original location (recycled)");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("win-file-system-ai-act-delete-file")]
    public async Task DeleteFileTool_MissingFile_Errors()
    {
        var dir = TempDir();
        try
        {
            var vm     = AtPath(dir);
            var result = await new DeleteTool(vm).InvokeAsync(
                new JsonObject { ["name"] = "does-not-exist.txt" }, default);

            Assert.IsTrue(result.IsError);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("win-file-system-ai-act-create-directory")]
    public async Task CreateDirectoryTool_CreatesFolderOnDisk()
    {
        var dir = TempDir();
        try
        {
            var vm     = AtPath(dir);
            var result = await new CreateDirectoryTool(vm).InvokeAsync(
                new JsonObject { ["name"] = "newfolder" }, default);

            Assert.IsTrue(result.Success);
            Assert.IsTrue(Directory.Exists(Path.Combine(dir, "newfolder")), "folder should exist on disk");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("win-file-system-ai-act-get-file-stats")]
    public async Task GetFileStatsTool_ReportsFileSizeAndType()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "data.bin"), new string('x', 42));
            var vm     = AtPath(dir);
            var result = await new GetFileStatsTool(vm).InvokeAsync(
                new JsonObject { ["name"] = "data.bin" }, default);

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.ModelText, "data.bin");
            StringAssert.Contains(result.ModelText, "file");
            StringAssert.Contains(result.ModelText, "42");   // exact byte count is rendered in the stats
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("win-file-system-ai-act-get-line-count")]
    public async Task GetLineCountTool_CountsLines()
    {
        var dir = TempDir();
        try
        {
            File.WriteAllLines(Path.Combine(dir, "lines.txt"), ["line1", "line2", "line3"]);
            var vm     = AtPath(dir);
            var result = await new GetLineCountTool(vm).InvokeAsync(
                new JsonObject { ["name"] = "lines.txt" }, default);

            Assert.IsTrue(result.Success);
            StringAssert.Contains(result.ModelText, "3 line");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [TestMethod]
    [CoversNode("win-file-system-ai-act-get-line-count")]
    public async Task GetLineCountTool_MissingFile_Errors()
    {
        var dir = TempDir();
        try
        {
            var vm     = AtPath(dir);
            var result = await new GetLineCountTool(vm).InvokeAsync(
                new JsonObject { ["name"] = "nope.txt" }, default);

            Assert.IsTrue(result.IsError);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Footer counts ─────────────────────────────────────────────────────────

    /// <summary>
    /// A selection change reuses the totals the loader published and never re-scans <c>Entries</c>. The
    /// re-scan raced the fire-and-forget loader, which appends on its own continuation — under the parallel
    /// test runner that surfaced as "Collection was modified" thrown from the constructor.
    /// <para>
    /// The probe appends to <c>Entries</c> without publishing new totals, which is exactly what the loader
    /// is doing mid-stream. A re-scanning implementation reports the appended entry; the correct one
    /// reports the last published counts.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task OnSelectionChanged_UsesPublishedTotals_WithoutRescanningEntries()
    {
        Directory.CreateDirectory(Path.Combine(_scratch, "alpha"));
        Directory.CreateDirectory(Path.Combine(_scratch, "beta"));
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
            File.WriteAllText(Path.Combine(_scratch, name), "x");

        var vm = AtPath(_scratch);
        await WaitForLoadAsync(vm);

        Assert.AreEqual("2 folders", vm.FolderCountText);
        Assert.AreEqual("3 files",   vm.FileCountText);

        var selected = vm.Entries.First(e => !e.IsDirectory);
        vm.Entries.Add(new FileSystemEntry { Name = "ghost", FullPath = @"X:\ghost", IsDirectory = true });

        vm.OnSelectionChanged([selected]);

        Assert.AreEqual("2 folders", vm.FolderCountText, "selecting must not re-scan Entries");
        Assert.AreEqual("3 files",   vm.FileCountText,   "selecting must not re-scan Entries");
        StringAssert.Contains(vm.SelectionSummary, "1 selected");
    }

    /// <summary>The constructor kicks the folder load off fire-and-forget; wait for it to settle.</summary>
    private static async Task WaitForLoadAsync(FileSystemViewModel vm)
    {
        for (var i = 0; i < 200 && (vm.IsLoadingEntries || vm.Entries.Count == 0); i++)
            await Task.Delay(25);
    }
}

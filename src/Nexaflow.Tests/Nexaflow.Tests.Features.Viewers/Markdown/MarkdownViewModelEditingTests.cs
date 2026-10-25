using Nexaflow.IO.Common;
using Nexaflow.Features.Common;
using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using System.IO;

namespace Nexaflow.Tests.Features.Markdown;

/// <summary>
/// Tests for <see cref="MarkdownViewModel"/>. The view-model now holds the whole
/// document as a single <see cref="MarkdownViewModel.Markdown"/> string (two-way
/// bound to the shared inline editor); these cover load, dirty-tracking, save and
/// the source-only view-mode flag. The inline edit/selection behaviour itself lives
/// in the shared InlineMarkdownEditor and is covered under Tests.Core.
/// </summary>
[TestClass]
[CoversNode("markdown-dirty-tracking")]
public class MarkdownViewModelEditingTests
{
    private readonly List<string> _tempFiles = [];

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var path in _tempFiles)
            try { File.Delete(path); } catch { }
    }

    // ── Factory helpers ───────────────────────────────────────────────────────

    private async Task<MarkdownViewModel> MakeAsync(string content)
    {
        var path = TestTemp.CreateFile();
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        var vm = new MarkdownViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance);
        await vm.Loaded;
        return vm;
    }

    private const string ThreeBlock = "# Heading\n\nA paragraph.\n\n- item one\n- item two\n";

    // ── Initial load ──────────────────────────────────────────────────────────

    [TestMethod]
    public async Task InitialLoad_Markdown_MatchesFileContent()
    {
        var vm = await MakeAsync(ThreeBlock);

        Assert.AreEqual(ThreeBlock, vm.Markdown);
    }

    [TestMethod]
    public async Task InitialLoad_IsDirty_False()
    {
        var vm = await MakeAsync(ThreeBlock);

        Assert.IsFalse(vm.IsDirty);
    }

    [TestMethod]
    public async Task InitialLoad_ViewMode_DefaultsToRendered()
    {
        var vm = await MakeAsync(ThreeBlock);

        Assert.AreEqual(MarkdownViewMode.Rendered, vm.ViewMode);
    }

    [TestMethod]
    public async Task MissingFile_LoadsEmpty_NotDirty()
    {
        var path = Path.Combine(TestTemp.Root, Path.GetRandomFileName() + ".md");
        var vm = new MarkdownViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance);   // file does not exist
        await vm.Loaded;

        Assert.AreEqual(string.Empty, vm.Markdown);
        Assert.IsFalse(vm.IsDirty);
    }

    // ── Dirty tracking ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task EditingMarkdown_SetsDirty()
    {
        var vm = await MakeAsync(ThreeBlock);

        vm.Markdown = "# Changed";

        Assert.IsTrue(vm.IsDirty);
    }

    [TestMethod]
    public async Task RevertingToSavedText_ClearsDirty()
    {
        var vm = await MakeAsync(ThreeBlock);

        vm.Markdown = "# Changed";
        vm.Markdown = ThreeBlock;   // back to the loaded baseline

        Assert.IsFalse(vm.IsDirty);
    }

    [TestMethod]
    public async Task ChangingTheViewMode_DoesNotMarkDirty()
    {
        var vm = await MakeAsync(ThreeBlock);

        vm.ViewMode = MarkdownViewMode.Split;
        vm.ViewMode = MarkdownViewMode.Source;

        Assert.IsFalse(vm.IsDirty);
        Assert.AreEqual(MarkdownViewMode.Source, vm.ViewMode);
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Save_WritesEditedContent()
    {
        var path = TestTemp.CreateFile();
        _tempFiles.Add(path);
        File.WriteAllText(path, ThreeBlock);
        var vm = new MarkdownViewModel(path, Substitute.For<IShellServices>(), VirtualFileSystem.Instance);
        await vm.Loaded;

        vm.Markdown = "# Saved Heading";
        await vm.SaveCommand.ExecuteAsync(null);

        StringAssert.Contains(File.ReadAllText(path), "# Saved Heading");
    }

    [TestMethod]
    public async Task Save_ClearsDirtyFlag()
    {
        var vm = await MakeAsync(ThreeBlock);
        vm.Markdown = "# Modified";

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.IsFalse(vm.IsDirty);
    }

    [TestMethod]
    public async Task SaveCommand_CanExecute_FollowsDirty()
    {
        var vm = await MakeAsync(ThreeBlock);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));

        vm.Markdown = "# Modified";
        Assert.IsTrue(vm.SaveCommand.CanExecute(null));

        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task AfterSave_EditingBackToSavedValue_IsClean()
    {
        var vm = await MakeAsync(ThreeBlock);
        vm.Markdown = "# Saved";
        await vm.SaveCommand.ExecuteAsync(null);

        vm.Markdown = "# Different";
        vm.Markdown = "# Saved";   // matches the newly-saved baseline

        Assert.IsFalse(vm.IsDirty);
    }

    // ── Windows CRLF line endings ─────────────────────────────────────────────

    /// <summary>
    /// Real markdown files on Windows use \r\n line endings. The view-model normalises
    /// to \n on load so the editor never sees embedded carriage returns (which the reading
    /// would otherwise carry into the words and WPF would render as artifacts).
    /// </summary>
    [TestMethod]
    public async Task CrLfFile_LoadsWithLfLineEndings()
    {
        const string crlf = "# Heading\r\n\r\nA paragraph.\r\n\r\n- item one\r\n- item two\r\n";
        var vm = await MakeAsync(crlf);

        Assert.IsFalse(vm.Markdown.Contains('\r'), "Markdown still contains carriage returns after load.");
        Assert.AreEqual(crlf.ReplaceLineEndings("\n"), vm.Markdown);
    }
}

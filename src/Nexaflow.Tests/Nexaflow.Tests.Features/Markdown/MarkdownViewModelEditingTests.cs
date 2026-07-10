using Nexaflow.Features.Markdown.ViewModels;
using Nexaflow.Tests.Fixtures;
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

    private MarkdownViewModel Make(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return new MarkdownViewModel(path);
    }

    private const string ThreeBlock = "# Heading\n\nA paragraph.\n\n- item one\n- item two\n";

    // ── Initial load ──────────────────────────────────────────────────────────

    [TestMethod]
    public void InitialLoad_Markdown_MatchesFileContent()
    {
        var vm = Make(ThreeBlock);

        Assert.AreEqual(ThreeBlock, vm.Markdown);
    }

    [TestMethod]
    public void InitialLoad_IsDirty_False()
    {
        var vm = Make(ThreeBlock);

        Assert.IsFalse(vm.IsDirty);
    }

    [TestMethod]
    public void InitialLoad_SourceOnly_DefaultsFalse()
    {
        var vm = Make(ThreeBlock);

        Assert.IsFalse(vm.SourceOnly);
    }

    [TestMethod]
    public void MissingFile_LoadsEmpty_NotDirty()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".md");
        var vm   = new MarkdownViewModel(path);   // file does not exist

        Assert.AreEqual(string.Empty, vm.Markdown);
        Assert.IsFalse(vm.IsDirty);
    }

    // ── Dirty tracking ────────────────────────────────────────────────────────

    [TestMethod]
    public void EditingMarkdown_SetsDirty()
    {
        var vm = Make(ThreeBlock);

        vm.Markdown = "# Changed";

        Assert.IsTrue(vm.IsDirty);
    }

    [TestMethod]
    public void RevertingToSavedText_ClearsDirty()
    {
        var vm = Make(ThreeBlock);

        vm.Markdown = "# Changed";
        vm.Markdown = ThreeBlock;   // back to the loaded baseline

        Assert.IsFalse(vm.IsDirty);
    }

    [TestMethod]
    public void TogglingSourceOnly_DoesNotMarkDirty()
    {
        var vm = Make(ThreeBlock);

        vm.SourceOnly = true;

        Assert.IsFalse(vm.IsDirty);
        Assert.IsTrue(vm.SourceOnly);
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Save_WritesEditedContent()
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);
        File.WriteAllText(path, ThreeBlock);
        var vm = new MarkdownViewModel(path);

        vm.Markdown = "# Saved Heading";
        vm.SaveCommand.Execute(null);

        StringAssert.Contains(File.ReadAllText(path), "# Saved Heading");
    }

    [TestMethod]
    public void Save_ClearsDirtyFlag()
    {
        var vm = Make(ThreeBlock);
        vm.Markdown = "# Modified";

        vm.SaveCommand.Execute(null);

        Assert.IsFalse(vm.IsDirty);
    }

    [TestMethod]
    public void SaveCommand_CanExecute_FollowsDirty()
    {
        var vm = Make(ThreeBlock);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));

        vm.Markdown = "# Modified";
        Assert.IsTrue(vm.SaveCommand.CanExecute(null));

        vm.SaveCommand.Execute(null);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
    }

    [TestMethod]
    public void AfterSave_EditingBackToSavedValue_IsClean()
    {
        var vm = Make(ThreeBlock);
        vm.Markdown = "# Saved";
        vm.SaveCommand.Execute(null);

        vm.Markdown = "# Different";
        vm.Markdown = "# Saved";   // matches the newly-saved baseline

        Assert.IsFalse(vm.IsDirty);
    }

    // ── Windows CRLF line endings ─────────────────────────────────────────────

    /// <summary>
    /// Real markdown files on Windows use \r\n line endings. The view-model normalises
    /// to \n on load so the editor never sees embedded carriage returns (which Markdig
    /// would otherwise carry into LiteralInlines and WPF would render as artifacts).
    /// </summary>
    [TestMethod]
    public void CrLfFile_LoadsWithLfLineEndings()
    {
        const string crlf = "# Heading\r\n\r\nA paragraph.\r\n\r\n- item one\r\n- item two\r\n";
        var vm = Make(crlf);

        Assert.IsFalse(vm.Markdown.Contains('\r'), "Markdown still contains carriage returns after load.");
        Assert.AreEqual(crlf.ReplaceLineEndings("\n"), vm.Markdown);
    }
}

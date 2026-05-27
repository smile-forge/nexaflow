using Nexaflow.Features.Markdown.ViewModels;
using System.IO;

namespace Nexaflow.Tests.Features.Markdown;

/// <summary>
/// Tests for <see cref="MarkdownViewModel"/> editing behaviour — specifically
/// that editing one block does not corrupt the raw markdown or parsed state of
/// subsequent blocks.
/// </summary>
[TestClass]
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

    // Three unambiguous top-level blocks: heading, paragraph, list
    private const string ThreeBlock = "# Heading\n\nA paragraph.\n\n- item one\n- item two\n";

    // ── Initial load ──────────────────────────────────────────────────────────

    [TestMethod]
    public void InitialLoad_ThreeTopLevelBlocks_BlockCountIsThree()
    {
        var vm = Make(ThreeBlock);

        Assert.AreEqual(3, vm.Blocks.Count);
    }

    [TestMethod]
    public void InitialLoad_BlockRawMarkdown_MatchesSourceContent()
    {
        var vm = Make(ThreeBlock);

        StringAssert.Contains(vm.Blocks[0].RawMarkdown, "Heading");
        StringAssert.Contains(vm.Blocks[1].RawMarkdown, "paragraph");
        StringAssert.Contains(vm.Blocks[2].RawMarkdown, "item one");
    }

    // ── Edit + blur round-trip ────────────────────────────────────────────────

    [TestMethod]
    public void EditFirstBlock_Blur_UpdatesBlockRawMarkdown()
    {
        var vm    = Make(ThreeBlock);
        var block = vm.Blocks[0];
        vm.BeginEdit(block);
        vm.OnBlockTextEdited(block, "# New Heading");
        vm.OnBlockBlurred(block);

        Assert.AreEqual("# New Heading", vm.Blocks[0].RawMarkdown);
    }

    [TestMethod]
    public void MultipleKeystrokes_Blur_OnlyLastValuePersists()
    {
        var vm    = Make(ThreeBlock);
        var block = vm.Blocks[0];
        vm.BeginEdit(block);
        vm.OnBlockTextEdited(block, "# H");
        vm.OnBlockTextEdited(block, "# He");
        vm.OnBlockTextEdited(block, "# Hel");
        vm.OnBlockTextEdited(block, "# Hello");
        vm.OnBlockBlurred(block);

        Assert.AreEqual("# Hello", vm.Blocks[0].RawMarkdown);
    }

    // ── Subsequent-block integrity after editing ──────────────────────────────

    [TestMethod]
    public void EditFirstBlock_SameLength_ParagraphContentPreserved()
    {
        var vm       = Make(ThreeBlock);
        var origPara = vm.Blocks[1].RawMarkdown;
        var block    = vm.Blocks[0];
        vm.BeginEdit(block);
        vm.OnBlockTextEdited(block, "# Changed");
        vm.OnBlockBlurred(block);

        Assert.AreEqual(origPara, vm.Blocks[1].RawMarkdown,
            "Paragraph block content changed after editing heading");
    }

    [TestMethod]
    public void EditFirstBlock_SameLength_ListContentPreserved()
    {
        var vm       = Make(ThreeBlock);
        var origList = vm.Blocks[2].RawMarkdown;
        var block    = vm.Blocks[0];
        vm.BeginEdit(block);
        vm.OnBlockTextEdited(block, "# Changed");
        vm.OnBlockBlurred(block);

        Assert.AreEqual(origList, vm.Blocks.Last().RawMarkdown,
            "List block content changed after editing heading");
    }

    [TestMethod]
    public void EditFirstBlock_AddLines_SubsequentListNotCorrupted()
    {
        var vm       = Make(ThreeBlock);
        var origList = vm.Blocks[2].RawMarkdown;
        var block    = vm.Blocks[0];
        vm.BeginEdit(block);
        // Add an extra line inside the heading block's raw text
        vm.OnBlockTextEdited(block, "# Heading\n\nInserted paragraph.");
        vm.OnBlockBlurred(block);

        Assert.AreEqual(origList, vm.Blocks.Last().RawMarkdown,
            "List block content corrupted after inserting lines into heading block");
    }

    [TestMethod]
    public void EditMiddleBlock_HeadingContentPreserved()
    {
        var vm          = Make(ThreeBlock);
        var origHeading = vm.Blocks[0].RawMarkdown;
        var paraBlock   = vm.Blocks[1];
        vm.BeginEdit(paraBlock);
        vm.OnBlockTextEdited(paraBlock, "A different paragraph.");
        vm.OnBlockBlurred(paraBlock);

        Assert.AreEqual(origHeading, vm.Blocks[0].RawMarkdown,
            "Heading changed after editing middle block");
    }

    [TestMethod]
    public void EditMiddleBlock_ListContentPreserved()
    {
        var vm        = Make(ThreeBlock);
        var origList  = vm.Blocks[2].RawMarkdown;
        var paraBlock = vm.Blocks[1];
        vm.BeginEdit(paraBlock);
        vm.OnBlockTextEdited(paraBlock, "A different paragraph.");
        vm.OnBlockBlurred(paraBlock);

        Assert.AreEqual(origList, vm.Blocks.Last().RawMarkdown,
            "List changed after editing middle block");
    }

    [TestMethod]
    public void EditBlockAboveCodeFence_FenceContentPreserved()
    {
        const string src = "# Heading\n\n```python\nprint('hello')\n```\n";
        var vm    = Make(src);
        var origCode = vm.Blocks[1].RawMarkdown;
        var heading  = vm.Blocks[0];
        vm.BeginEdit(heading);
        vm.OnBlockTextEdited(heading, "# Changed Heading");
        vm.OnBlockBlurred(heading);

        Assert.AreEqual(origCode, vm.Blocks[1].RawMarkdown,
            "Code fence corrupted after editing heading");
    }

    [TestMethod]
    public void EditBlock_TotalBlockCount_PreservedWhenContentHasSameTopLevelBlocks()
    {
        var vm    = Make(ThreeBlock);
        var block = vm.Blocks[0];
        vm.BeginEdit(block);
        vm.OnBlockTextEdited(block, "# Same-count heading");
        vm.OnBlockBlurred(block);

        Assert.AreEqual(3, vm.Blocks.Count);
    }

    // ── Dirty flag ────────────────────────────────────────────────────────────

    [TestMethod]
    public void IsDirty_InitiallyFalse()
    {
        var vm = Make(ThreeBlock);

        Assert.IsFalse(vm.IsDirty);
    }

    [TestMethod]
    public void OnBlockTextEdited_SetsDirty()
    {
        var vm = Make(ThreeBlock);

        vm.OnBlockTextEdited(vm.Blocks[0], "# Modified");

        Assert.IsTrue(vm.IsDirty);
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Save_WritesEditedContent()
    {
        var path = Path.GetTempFileName();
        _tempFiles.Add(path);
        File.WriteAllText(path, ThreeBlock);
        var vm    = new MarkdownViewModel(path);
        var block = vm.Blocks[0];
        vm.BeginEdit(block);
        vm.OnBlockTextEdited(block, "# Saved Heading");
        vm.OnBlockBlurred(block);

        vm.SaveCommand.Execute(null);

        StringAssert.Contains(File.ReadAllText(path), "# Saved Heading");
    }

    [TestMethod]
    public void Save_ClearsDirtyFlag()
    {
        var vm    = Make(ThreeBlock);
        vm.OnBlockTextEdited(vm.Blocks[0], "# Modified");

        vm.SaveCommand.Execute(null);

        Assert.IsFalse(vm.IsDirty);
    }

    // ── Pin / editing-state ───────────────────────────────────────────────────

    [TestMethod]
    public void TogglePin_BlockStaysEditing_OnBlur()
    {
        var vm    = Make(ThreeBlock);
        var block = vm.Blocks[0];
        vm.BeginEdit(block);
        vm.TogglePin(block);  // pin it

        vm.OnBlockBlurred(block);  // blur should not exit edit mode when pinned

        Assert.IsTrue(block.IsEditing);
    }

    [TestMethod]
    public void TogglePinTwice_BlockExitsEditMode()
    {
        var vm    = Make(ThreeBlock);
        var block = vm.Blocks[0];
        vm.BeginEdit(block);
        vm.TogglePin(block);  // pin
        vm.TogglePin(block);  // unpin → exits editing

        Assert.IsFalse(block.IsEditing);
    }

    [TestMethod]
    public void BeginEdit_SetsIsEditing()
    {
        var vm    = Make(ThreeBlock);
        var block = vm.Blocks[0];

        vm.BeginEdit(block);

        Assert.IsTrue(block.IsEditing);
    }

    // ── Windows CRLF line endings ─────────────────────────────────────────────

    /// <summary>
    /// Real markdown files on Windows use \r\n line endings.
    /// File.ReadAllText preserves these; Split('\n') leaves trailing \r on each
    /// line.  After any edit, ReconstructRawText joins via "\n\n", so subsequent
    /// blocks' RawMarkdown must not carry embedded \r characters — otherwise
    /// Markdig embeds the \r in LiteralInlines and WPF renders artifacts.
    /// </summary>
    [TestMethod]
    public void CrLfFile_AfterEdit_SubsequentBlocksHaveNoCarriageReturns()
    {
        const string crlf = "# Heading\r\n\r\nA paragraph.\r\n\r\n- item one\r\n- item two\r\n";
        var vm = Make(crlf);

        var block = vm.Blocks[0];
        vm.BeginEdit(block);
        vm.OnBlockTextEdited(block, "# Changed");
        vm.OnBlockBlurred(block);

        foreach (var b in vm.Blocks)
            Assert.IsFalse(b.RawMarkdown.Contains('\r'),
                $"Block at line {b.StartLine} contains \\r: \"{b.RawMarkdown.Replace("\r", "\\r")}\"");
    }

    [TestMethod]
    public void CrLfFile_AfterEdit_ListContentCorrect()
    {
        const string crlf = "# Heading\r\n\r\nA paragraph.\r\n\r\n- item one\r\n- item two\r\n";
        var vm = Make(crlf);

        var block = vm.Blocks[0];
        vm.BeginEdit(block);
        vm.OnBlockTextEdited(block, "# Changed");
        vm.OnBlockBlurred(block);

        var listRaw = vm.Blocks.Last().RawMarkdown;
        Assert.AreEqual("- item one\n- item two", listRaw,
            $"List raw after CRLF edit: \"{listRaw.Replace("\r", "\\r")}\"");
    }
}

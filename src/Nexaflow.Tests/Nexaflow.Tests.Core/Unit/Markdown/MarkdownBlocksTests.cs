using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit.Markdown;

/// <summary>
/// Pure tests for the inline-editor block model (<see cref="MarkdownBlocks"/>):
/// blank-line content splitting, joining and compaction, plus the Enter/Ctrl+Enter
/// reducer shapes. WPF-free.
/// </summary>
[TestClass]
[CoversNode("markdown-block-model")]
public class MarkdownBlocksTests
{
    // ── Split ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void Split_Empty_ReturnsSingleEmptyBlock()
        => CollectionAssert.AreEqual(new[] { "" }, MarkdownBlocks.Split(""));

    [TestMethod]
    public void Split_Null_ReturnsSingleEmptyBlock()
        => CollectionAssert.AreEqual(new[] { "" }, MarkdownBlocks.Split(null));

    [TestMethod]
    public void Split_SingleParagraph_OneBlock()
        => CollectionAssert.AreEqual(new[] { "hello world" }, MarkdownBlocks.Split("hello world"));

    [TestMethod]
    public void Split_BlankLineSeparated_TwoContentBlocks()
        => CollectionAssert.AreEqual(new[] { "## x", "more" }, MarkdownBlocks.Split("## x\n\nmore"));

    [TestMethod]
    public void Split_SoftNewline_StaysOneBlock()
        => CollectionAssert.AreEqual(new[] { "a\nb" }, MarkdownBlocks.Split("a\nb"));

    [TestMethod]
    public void Split_HardBreak_StaysOneBlock_AndKeepsTrailingSpaces()
        => CollectionAssert.AreEqual(new[] { "line 2  \nl3" }, MarkdownBlocks.Split("line 2  \nl3"));

    [TestMethod]
    public void Split_MultiLineList_IsOneBlock()
        => CollectionAssert.AreEqual(new[] { "- a\n- b\n- c" }, MarkdownBlocks.Split("- a\n- b\n- c"));

    [TestMethod]
    public void Split_DropsLeadingTrailingAndRepeatedBlankLines()
        => CollectionAssert.AreEqual(new[] { "a", "b" }, MarkdownBlocks.Split("\n\na\n\n\n\nb\n\n"));

    [TestMethod]
    public void Split_NormalisesCrLf()
        => CollectionAssert.AreEqual(new[] { "a", "b" }, MarkdownBlocks.Split("a\r\n\r\nb"));

    // ── Fenced code / diagrams (blank lines inside must NOT split) ─────────

    [TestMethod]
    public void Split_FenceWithInnerBlankLine_StaysOneBlock()
    {
        const string fence = "```mermaid\ngraph TD\nA --> B\n\nB --> C\n```";
        CollectionAssert.AreEqual(new[] { fence }, MarkdownBlocks.Split(fence));
    }

    [TestMethod]
    public void Split_TextThenFenceWithBlankLine_KeepsFenceWhole()
    {
        var blocks = MarkdownBlocks.Split("intro\n\n```mermaid\nA\n\nB\n```\n\nouttro");
        CollectionAssert.AreEqual(new[] { "intro", "```mermaid\nA\n\nB\n```", "outtro" }, blocks);
    }

    [TestMethod]
    public void Split_UnclosedFence_IsOneBlock()
        => CollectionAssert.AreEqual(new[] { "```mermaid\nA\n\nB" }, MarkdownBlocks.Split("```mermaid\nA\n\nB"));

    [TestMethod]
    public void IsFenced_DetectsFenceOpener()
    {
        Assert.IsTrue(MarkdownBlocks.IsFenced("```mermaid\ngraph TD"));
        Assert.IsTrue(MarkdownBlocks.IsFenced("~~~\ncode"));
        Assert.IsFalse(MarkdownBlocks.IsFenced("just a paragraph"));
        Assert.IsFalse(MarkdownBlocks.IsFenced("- a list\n- with ``` inside an item"));
    }

    // ── Join ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void Join_SeparatesWithBlankLine()
        => Assert.AreEqual("a\n\nb", MarkdownBlocks.Join(["a", "b"]));

    [TestMethod]
    public void Join_PreservesIntraBlockNewlinesAndHardBreaks()
        => Assert.AreEqual("line 2  \nl3\n\nnext", MarkdownBlocks.Join(["line 2  \nl3", "next"]));

    [TestMethod]
    public void SplitJoin_RoundTripsCanonicalForm()
    {
        var src = "# H\n\npara line\n\n- l1\n- l2";
        Assert.AreEqual(src, MarkdownBlocks.Join(MarkdownBlocks.Split(src)));
    }

    // ── Compact ───────────────────────────────────────────────────────────

    [TestMethod]
    public void Compact_DropsEmptyBlocks()
        => CollectionAssert.AreEqual(new[] { "a", "b" }, MarkdownBlocks.Compact(["a", "", "  ", "b"]));

    [TestMethod]
    public void Compact_AllEmpty_KeepsOneEmptyBlock()
        => CollectionAssert.AreEqual(new[] { "" }, MarkdownBlocks.Compact(["", "  ", "\t"]));

    // ── Editing shapes (mirror the editor's reducers) ─────────────────────

    [TestMethod]
    public void Enter_SplitsBlockAtCaret_NewEmptyBlockGetsFocus()
    {
        // "## heading|" + Enter → block "## heading" stays (renders), new empty block follows.
        var blocks = new List<string> { "## heading" };
        const int caret = 10;
        var before = blocks[0][..caret];
        var after  = blocks[0][caret..];
        blocks[0] = before;
        blocks.Insert(1, after);
        CollectionAssert.AreEqual(new[] { "## heading", "" }, blocks);
    }

    [TestMethod]
    public void Backspace_AtBlockStart_MergesIntoPrevious_AtJoinPoint()
    {
        // empty trailing block, Backspace → merges into previous; caret at end of its content.
        var blocks = new List<string> { "line 2  \nl3", "" };
        int active = 1, join = blocks[active - 1].Length;
        blocks[active - 1] += blocks[active];
        blocks.RemoveAt(active);
        CollectionAssert.AreEqual(new[] { "line 2  \nl3" }, blocks);
        Assert.AreEqual("line 2  \nl3".Length, join);   // caret lands right after l3
    }

    [TestMethod]
    public void Sequence_Line1Enter_Line2HardBreak_L3Enter_ProducesCleanBlocks()
    {
        // "line 1" + Enter + "line 2" + Ctrl+Enter + "l3" + Enter
        var blocks = new List<string> { "line 1" };
        blocks.Insert(1, "");                 // Enter → new block
        blocks[1] = "line 2";
        blocks[1] += "  \n";                  // Ctrl+Enter (hard break)
        blocks[1] += "l3";
        blocks.Insert(2, "");                 // Enter → new block

        CollectionAssert.AreEqual(new[] { "line 1", "line 2  \nl3", "" }, blocks);
        Assert.AreEqual("line 1\n\nline 2  \nl3", MarkdownBlocks.Join(MarkdownBlocks.Compact(blocks)));
    }
}

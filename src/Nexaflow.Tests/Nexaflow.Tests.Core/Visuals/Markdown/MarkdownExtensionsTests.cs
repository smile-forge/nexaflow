using Markdig;
using Markdig.Syntax;
using Nexaflow.Visuals.Text.Markdown;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MdMarkdown = Markdig.Markdown;
using MdTable    = Markdig.Extensions.Tables.Table;
using DefList    = Markdig.Extensions.DefinitionLists.DefinitionList;
using MdFigure   = Markdig.Extensions.Figures.Figure;
using MdFooter   = Markdig.Extensions.Footers.FooterBlock;

namespace Nexaflow.Tests.Core.Visuals.Markdown;

/// <summary>
/// Render tests for the enabled Markdig extensions (grid tables, task lists, auto links,
/// definition lists, list extras, figures, footers, citations, inline math) plus expanded
/// pipe-table coverage. Asserts the extension actually parsed (not a paragraph fallback)
/// and that its rendered output carries the expected content.
/// </summary>
[TestClass]
[TestCategory("UI")]
public class MarkdownExtensionsTests
{
    private static MarkdownDocument Parse(string src) => MdMarkdown.Parse(src, MarkdownPipelineFactory.Default);

    // ── Pipe tables (re-verification) ──────────────────────────────────────

    [TestMethod]
    public void PipeTable_Minimal_RendersAllCellText() => UiThread.Run(() =>
    {
        var grid = (Grid)BlockRenderer.Render(Parse("| a | b |\n|---|---|\n| 1 | 2 |\n")[0]);
        var text = AllText(grid);
        foreach (var s in new[] { "a", "b", "1", "2" }) StringAssert.Contains(text, s);
    });

    [TestMethod]
    public void PipeTable_NoOuterPipes_Renders() => UiThread.Run(() =>
    {
        var block = Parse("a | b\n--- | ---\n1 | 2\n")[0];
        Assert.IsInstanceOfType(block, typeof(MdTable), "table without outer pipes should still parse");
        StringAssert.Contains(AllText(BlockRenderer.Render(block)), "1");
    });

    [TestMethod]
    public void PipeTable_Alignment_AppliesPerColumn() => UiThread.Run(() =>
    {
        var grid = (Grid)BlockRenderer.Render(Parse("| L | C | R |\n|:--|:-:|--:|\n| a | b | c |\n")[0]);
        Assert.AreEqual(TextAlignment.Left,   Cell(grid, 1, 0).TextAlignment);
        Assert.AreEqual(TextAlignment.Center, Cell(grid, 1, 1).TextAlignment);
        Assert.AreEqual(TextAlignment.Right,  Cell(grid, 1, 2).TextAlignment);
    });

    [TestMethod]
    public void PipeTable_InlineFormattingInCells_Renders() => UiThread.Run(() =>
    {
        var grid = (Grid)BlockRenderer.Render(Parse("| h |\n|---|\n| **b** `c` [l](http://x) |\n")[0]);
        var cell = Cell(grid, 1, 0);
        Assert.IsTrue(cell.Inlines.OfType<Span>().Any(), "bold span expected");
        Assert.IsTrue(cell.Inlines.OfType<Hyperlink>().Any(), "link expected");
    });

    [TestMethod]
    public void PipeTable_AfterParagraphWithoutBlankLine_Behaviour() => UiThread.Run(() =>
    {
        // GFM allows a pipe table to interrupt a paragraph. Verify a table forms and renders.
        var doc = Parse("Some intro text\n| a | b |\n|---|---|\n| 1 | 2 |\n");
        var table = doc.Descendants().OfType<MdTable>().FirstOrDefault();
        Assert.IsNotNull(table, "a pipe table directly under a paragraph line should still form");
        StringAssert.Contains(AllText(BlockRenderer.Render(table)), "1");
    });

    [TestMethod]
    public void PipeTable_InsideBlockquote_Renders() => UiThread.Run(() =>
    {
        var block = Parse("> | a | b |\n> |---|---|\n> | 1 | 2 |\n")[0];
        Assert.IsInstanceOfType(block, typeof(QuoteBlock));
        StringAssert.Contains(AllText(BlockRenderer.Render(block)), "1");
    });

    [TestMethod]
    public void PipeTable_RaggedRow_RendersWithoutThrowing() => UiThread.Run(() =>
    {
        // Body row has more cells than the header declares.
        var block = Parse("| a | b |\n|---|---|\n| 1 | 2 | 3 |\n")[0];
        Assert.IsInstanceOfType(block, typeof(MdTable));
        StringAssert.Contains(AllText(BlockRenderer.Render(block)), "1");
    });

    [TestMethod]
    public void PipeTable_CrlfLineEndings_Renders() => UiThread.Run(() =>
    {
        var block = Parse("| a | b |\r\n|---|---|\r\n| 1 | 2 |\r\n")[0];
        Assert.IsInstanceOfType(block, typeof(MdTable));
        StringAssert.Contains(AllText(BlockRenderer.Render(block)), "1");
    });

    [TestMethod]
    public void PipeTable_EmptyCells_Render() => UiThread.Run(() =>
    {
        var block = Parse("| a | b |\n|---|---|\n|  | 2 |\n")[0];
        Assert.IsInstanceOfType(block, typeof(MdTable));
        StringAssert.Contains(AllText(BlockRenderer.Render(block)), "2");
    });

    [TestMethod]
    public void PipeTable_EscapedPipeInCell_Renders() => UiThread.Run(() =>
    {
        var grid = (Grid)BlockRenderer.Render(Parse("| a | b |\n|---|---|\n| x \\| y | z |\n")[0]);
        Assert.AreEqual(2, grid.ColumnDefinitions.Count, "escaped pipe must not split the cell");
        StringAssert.Contains(AllText(grid), "z");
    });

    [TestMethod]
    public void FlowDocument_PipeTable_RendersSelectableTable() => UiThread.Run(() =>
    {
        // The selectable path (SelectableMarkdownView → AI overlay / AIChat) uses MarkdownFlowDocument.
        var doc   = MarkdownFlowDocument.Build("| a | b |\n|---|---|\n| 1 | 2 |\n");
        var table = doc.Blocks.OfType<Table>().FirstOrDefault();
        Assert.IsNotNull(table, "selectable path should produce a FlowDocument Table");
        Assert.AreEqual(2, table.Columns.Count);
        var text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
        foreach (var s in new[] { "a", "b", "1", "2" }) StringAssert.Contains(text, s);
    });

    // ── Grid tables ────────────────────────────────────────────────────────

    [TestMethod]
    public void GridTable_Renders_ColumnsAndCellText() => UiThread.Run(() =>
    {
        const string src =
            "+---+---+\n" +
            "| a | b |\n" +
            "+===+===+\n" +
            "| 1 | 2 |\n" +
            "+---+---+\n";
        var block = Parse(src)[0];
        Assert.IsInstanceOfType(block, typeof(MdTable));
        var grid = (Grid)BlockRenderer.Render(block);
        Assert.AreEqual(2, grid.ColumnDefinitions.Count);
        foreach (var s in new[] { "a", "b", "1", "2" }) StringAssert.Contains(AllText(grid), s);
    });

    [TestMethod]
    public void GridTable_ColumnSpan_IsHonoured() => UiThread.Run(() =>
    {
        const string src =
            "+-------+------+\n" +
            "| Big          |\n" +
            "+=======+======+\n" +
            "| a     | b    |\n" +
            "+-------+------+\n";
        var block = Parse(src)[0];
        Assert.IsInstanceOfType(block, typeof(MdTable));
        var grid = (Grid)BlockRenderer.Render(block);
        Assert.IsTrue(grid.Children.OfType<Border>().Any(b => Grid.GetColumnSpan(b) >= 2),
            "the merged top cell should span two columns");
    });

    [TestMethod]
    public void GridTable_ListCell_RendersListContent() => UiThread.Run(() =>
    {
        const string src =
            "+---------+-----+\n" +
            "| a       | b   |\n" +
            "+=========+=====+\n" +
            "| - one   | y   |\n" +
            "| - two   |     |\n" +
            "+---------+-----+\n";
        var block = Parse(src)[0];
        Assert.IsInstanceOfType(block, typeof(MdTable));
        var text = AllText(BlockRenderer.Render(block));
        StringAssert.Contains(text, "one");   // block-level (list) cell content must render
        StringAssert.Contains(text, "two");
    });

    [TestMethod]
    public void FlowDocument_GridTable_ListCell_RendersListContent() => UiThread.Run(() =>
    {
        const string src =
            "+---------+-----+\n" +
            "| a       | b   |\n" +
            "+=========+=====+\n" +
            "| - one   | y   |\n" +
            "| - two   |     |\n" +
            "+---------+-----+\n";
        var doc  = MarkdownFlowDocument.Build(src);
        var text = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
        StringAssert.Contains(text, "one");   // selectable path must also render block cell content
        StringAssert.Contains(text, "two");
    });

    // ── Task lists ─────────────────────────────────────────────────────────

    [TestMethod]
    public void TaskList_RendersCheckedAndUncheckedGlyphs() => UiThread.Run(() =>
    {
        var block = Parse("- [x] done\n- [ ] todo\n")[0];
        Assert.IsInstanceOfType(block, typeof(ListBlock));
        var text = AllText(BlockRenderer.Render(block));
        StringAssert.Contains(text, "☑");
        StringAssert.Contains(text, "☐");
        StringAssert.Contains(text, "done");
    });

    // ── Auto links (bare URL — extension, vs <…> base CommonMark) ──────────

    [TestMethod]
    public void AutoLink_BareUrl_RendersHyperlink() => UiThread.Run(() =>
    {
        var tb   = (TextBlock)BlockRenderer.Render(Parse("see https://example.com now\n")[0]);
        var link = tb.Inlines.OfType<Hyperlink>().Single();
        Assert.AreEqual("example.com", link.NavigateUri!.Host);
    });

    // ── Definition lists ───────────────────────────────────────────────────

    [TestMethod]
    public void DefinitionList_RendersTermAndDefinition() => UiThread.Run(() =>
    {
        const string src = "Apple\n:   A round fruit\n";
        var block = Parse(src)[0];
        Assert.IsInstanceOfType(block, typeof(DefList));
        var text = AllText(BlockRenderer.Render(block));
        StringAssert.Contains(text, "Apple");
        StringAssert.Contains(text, "A round fruit");
    });

    // ── List extras (alpha / roman ordered markers) ───────────────────────

    [TestMethod]
    public void ListExtras_AlphabeticMarkers_Render() => UiThread.Run(() =>
    {
        var block = (ListBlock)Parse("a. first\nb. second\n")[0];
        Assert.IsTrue(block.IsOrdered);
        Assert.AreEqual('a', block.BulletType);
        var text = AllText(BlockRenderer.Render(block));
        StringAssert.Contains(text, "a.");
        StringAssert.Contains(text, "b.");
    });

    [TestMethod]
    public void ListExtras_RomanMarkers_Render() => UiThread.Run(() =>
    {
        var block = (ListBlock)Parse("i. one\nii. two\n")[0];
        Assert.IsTrue(block.IsOrdered);
        Assert.AreEqual('i', block.BulletType);
        var text = AllText(BlockRenderer.Render(block));
        StringAssert.Contains(text, "i.");
        StringAssert.Contains(text, "ii.");
    });

    // ── Figures ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Figure_RendersBorderWithContent() => UiThread.Run(() =>
    {
        const string src = "^^^\nThis is a figure\n^^^\n";
        var block = Parse(src)[0];
        Assert.IsInstanceOfType(block, typeof(MdFigure));
        var fe = BlockRenderer.Render(block);
        Assert.IsInstanceOfType(fe, typeof(Border));
        StringAssert.Contains(AllText(fe), "This is a figure");
    });

    // ── Footers ────────────────────────────────────────────────────────────

    [TestMethod]
    public void Footer_RendersWithContent() => UiThread.Run(() =>
    {
        const string src = "^^ This is a footer\n";
        var block = Parse(src)[0];
        Assert.IsInstanceOfType(block, typeof(MdFooter));
        StringAssert.Contains(AllText(BlockRenderer.Render(block)), "This is a footer");
    });

    // ── Citations (""text"") ───────────────────────────────────────────────

    [TestMethod]
    public void Citation_RendersSuperscriptSpan() => UiThread.Run(() =>
    {
        // Markdig's UseCitations delimiter is a doubled double-quote, not ^^.
        var tb   = (TextBlock)BlockRenderer.Render(Parse("text \"\"cited\"\" end\n")[0]);
        var span = tb.Inlines.OfType<Span>().Single(s => s.BaselineAlignment == BaselineAlignment.Superscript);
        StringAssert.Contains(InlineText(span.Inlines), "cited");
    });

    // ── Inline math ($…$) ──────────────────────────────────────────────────

    [TestMethod]
    public void InlineMath_RendersFormulaOrFallback() => UiThread.Run(() =>
    {
        var tb         = (TextBlock)BlockRenderer.Render(Parse("value $x^2$ end\n")[0]);
        bool hasFormula = tb.Inlines.OfType<InlineUIContainer>().Any(c => c.Child is WpfMath.Controls.FormulaControl);
        bool hasFallback = InlineText(tb.Inlines).Contains("x^2");
        Assert.IsTrue(hasFormula || hasFallback, "inline math should render a formula or a LaTeX fallback");
    });

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Body/header cell TextBlock at a grid position (via the wrapping Border).</summary>
    private static TextBlock Cell(Grid g, int row, int col) =>
        (TextBlock)g.Children.OfType<Border>()
                    .Single(b => Grid.GetRow(b) == row && Grid.GetColumn(b) == col).Child;

    /// <summary>Recursively collects visible text from a rendered element tree.</summary>
    private static string AllText(object? element) => element switch
    {
        TextBlock tb      => InlineText(tb.Inlines),
        Panel panel       => string.Concat(panel.Children.Cast<object>().Select(AllText)),
        Border b          => AllText(b.Child),
        ContentControl cc => AllText(cc.Content),
        Decorator d       => AllText(d.Child),
        _                 => string.Empty,
    };

    private static string InlineText(InlineCollection inlines)
    {
        var sb = new StringBuilder();
        foreach (var inl in inlines) sb.Append(InlineText(inl));
        return sb.ToString();
    }

    private static string InlineText(Inline inline) => inline switch
    {
        Run r  => r.Text,
        Span s => InlineText(s.Inlines),   // covers Bold/Italic/Hyperlink (all Span subclasses)
        _      => string.Empty,
    };
}

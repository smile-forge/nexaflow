using Markdig.Syntax;
using Nexaflow.Visuals.Text.Markdown;
using System.Windows;
using System.Windows.Controls;
using MdMarkdown = Markdig.Markdown;
using MdTable    = Markdig.Extensions.Tables.Table;

namespace Nexaflow.Tests.Core.Visuals.Markdown;

[TestClass]
[TestCategory("UI")]
public class BlockRendererTests
{
    private static MarkdownDocument Parse(string src) =>
        MdMarkdown.Parse(src, MarkdownPipelineFactory.Default);

    [TestMethod]
    public void Heading_Level1_RendersStackWithUnderline() => UiThread.Run(() =>
    {
        var fe = BlockRenderer.Render(Parse("# Title\n")[0]);
        var sp = (StackPanel)fe;
        Assert.AreEqual(2, sp.Children.Count); // TextBlock + underline border
        Assert.IsInstanceOfType(sp.Children[0], typeof(TextBlock));
    });

    [TestMethod]
    public void Heading_Level3_RendersBareTextBlock() => UiThread.Run(() =>
    {
        Assert.IsInstanceOfType(BlockRenderer.Render(Parse("### Sub\n")[0]), typeof(TextBlock));
    });

    [TestMethod]
    public void Paragraph_RendersTextBlock() => UiThread.Run(() =>
    {
        Assert.IsInstanceOfType(BlockRenderer.Render(Parse("hello world\n")[0]), typeof(TextBlock));
    });

    [TestMethod]
    public void ThematicBreak_RendersBorder() => UiThread.Run(() =>
    {
        Assert.IsInstanceOfType(BlockRenderer.Render(Parse("---\n")[0]), typeof(Border));
    });

    [TestMethod]
    public void Blockquote_RendersBorderWrappingStack() => UiThread.Run(() =>
    {
        var border = (Border)BlockRenderer.Render(Parse("> quoted\n")[0]);
        Assert.IsInstanceOfType(border.Child, typeof(StackPanel));
    });

    [TestMethod]
    public void UnorderedList_RendersStackOfRows() => UiThread.Run(() =>
    {
        var sp = (StackPanel)BlockRenderer.Render(Parse("- a\n- b\n")[0]);
        Assert.AreEqual(2, sp.Children.Count);
    });

    [TestMethod]
    public void OrderedList_RendersStackOfRows() => UiThread.Run(() =>
    {
        var sp = (StackPanel)BlockRenderer.Render(Parse("1. first\n2. second\n3. third\n")[0]);
        Assert.AreEqual(3, sp.Children.Count);
    });

    [TestMethod]
    public void CodeFence_RendersBorder() => UiThread.Run(() =>
    {
        Assert.IsInstanceOfType(BlockRenderer.Render(Parse("```\ncode\n```\n")[0]), typeof(Border));
    });

    [TestMethod]
    public void Table_RendersGrid() => UiThread.Run(() =>
    {
        var table = Parse("| a | b |\n|---|---|\n| 1 | 2 |\n").OfType<MdTable>().Single();
        var grid  = (Grid)BlockRenderer.Render(table);
        Assert.AreEqual(2, grid.ColumnDefinitions.Count);
    });

    [TestMethod]
    public void DiagramFence_DispatchesToDiagramRenderer() => UiThread.Run(() =>
    {
        const string src = "```mermaid\npie\n```\n";
        var fc = Parse(src).OfType<FencedCodeBlock>().Single();
        Assert.IsNotNull(BlockRenderer.Render(fc, src));
    });

    [TestMethod]
    public void MathBlock_RendersFormulaControlOrFallbackBorder() => UiThread.Run(() =>
    {
        const string src = "$$\n\\not_a_command{\n$$\n";
        var mb = Parse(src).OfType<Markdig.Extensions.Mathematics.MathBlock>().Single();
        var fe = BlockRenderer.Render(mb, src);
        Assert.IsTrue(fe is Border || fe is WpfMath.Controls.FormulaControl);
    });
}

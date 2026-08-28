using Markdig.Syntax;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Latex;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using MdMarkdown = Markdig.Markdown;
using MdTable    = Markdig.Extensions.Tables.Table;

namespace Nexaflow.Tests.Visuals.Markdown;

[TestClass]
[TestCategory("UI")]
[CoversNode("latex")]
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
    public void MathBlock_TypesetsWhatItCanReadAndMarksWhatItCannot() => UiThread.Run(() =>
    {
        const string good = "$$\n\\frac{x^2}{2}\n$$\n";
        var typeset = BlockRenderer.Render(
            Parse(good).OfType<Markdig.Extensions.Mathematics.MathBlock>().Single(), good);
        Assert.IsInstanceOfType(typeset, typeof(FormulaElement));

        // Trouble in a formula does not cost it its typesetting. Maths under a caret is invalid most
        // of the time — every command is unreadable until its last letter is typed — so a formula that
        // turned into a box of source as you wrote it would spend most of its life as a box of source.
        // The parser recovers instead: what it understood is set, what it could not is shown as the
        // characters written, and a wave goes under those.
        const string bad = "$$\n\\not_a_command{\n$$\n";
        var recovered = BlockRenderer.Render(
            Parse(bad).OfType<Markdig.Extensions.Mathematics.MathBlock>().Single(), bad);

        var formula = recovered as FormulaElement;
        Assert.IsNotNull(formula, "an unreadable formula is still a formula, still editable in place");
        Assert.AreNotEqual(0, formula.Diagnostics.Count,
            "and it says which part of it could not be read, or nothing would mark the trouble");
    });

    // ── CommonMark base spec: block-level features ────────────────────────

    [TestMethod]
    public void SetextHeading_ParsesAsHeadingAndRendersLikeAtx() => UiThread.Run(() =>
    {
        var block = Parse("Title\n=====\n")[0];
        Assert.IsInstanceOfType(block, typeof(HeadingBlock));
        Assert.AreEqual(1, ((HeadingBlock)block).Level);

        var sp = (StackPanel)BlockRenderer.Render(block);   // H1 → text + underline border
        Assert.AreEqual(2, sp.Children.Count);
        Assert.IsInstanceOfType(sp.Children[0], typeof(TextBlock));
    });

    [TestMethod]
    public void NestedList_RendersSublistInsideItem() => UiThread.Run(() =>
    {
        var sp = (StackPanel)BlockRenderer.Render(Parse("- a\n  - b\n")[0]);
        Assert.AreEqual(1, sp.Children.Count);                        // one top-level item

        var row     = (Grid)sp.Children[0];
        var content = row.Children.OfType<StackPanel>().Single();     // column-1 content panel
        var nested  = content.Children.OfType<StackPanel>().Single(); // the sub-list
        Assert.AreEqual(1, nested.Children.Count);                    // sub-item "b"
    });

    [TestMethod]
    public void LooseList_RendersEveryItem() => UiThread.Run(() =>
    {
        var sp = (StackPanel)BlockRenderer.Render(Parse("- a\n\n- b\n")[0]);
        Assert.AreEqual(2, sp.Children.Count);
    });

    [TestMethod]
    public void IndentedCodeBlock_RendersBorderWithContent() => UiThread.Run(() =>
    {
        var block = Parse("    indented code\n")[0];
        Assert.AreEqual(typeof(CodeBlock), block.GetType());          // indented, not fenced
        var border = (Border)BlockRenderer.Render(block);
        StringAssert.Contains(((TextBlock)border.Child).Text, "indented code");
    });

    // ── CommonMark base spec: inline-level features ───────────────────────

    [TestMethod]
    public void InlineCode_RendersMonospaceRun() => UiThread.Run(() =>
    {
        var run = Para("`snippet`\n").Inlines.OfType<Run>().Single(r => r.FontFamily.Source.Contains("Consolas"));
        Assert.AreEqual("snippet", run.Text);
    });

    [TestMethod]
    public void Emphasis_RendersItalicSpan() => UiThread.Run(() =>
    {
        var span = Para("*word*\n").Inlines.OfType<Span>().Single();
        Assert.AreEqual(FontStyles.Italic, span.FontStyle);
        Assert.AreEqual("word", ((Run)span.Inlines.First()).Text);
    });

    [TestMethod]
    public void Strong_RendersBoldSpan() => UiThread.Run(() =>
    {
        var span = Para("**word**\n").Inlines.OfType<Span>().Single();
        Assert.AreEqual(FontWeights.Bold, span.FontWeight);
        Assert.AreEqual("word", ((Run)span.Inlines.First()).Text);
    });

    [TestMethod]
    public void InlineLink_RendersHyperlinkWithUriAndText() => UiThread.Run(() =>
    {
        var link = Para("[text](https://example.com)\n").Inlines.OfType<Hyperlink>().Single();
        Assert.AreEqual("example.com", link.NavigateUri!.Host);
        Assert.AreEqual("text", ((Run)link.Inlines.First()).Text);
    });

    [TestMethod]
    public void ReferenceLink_ResolvesToHyperlink() => UiThread.Run(() =>
    {
        const string src = "[text][id]\n\n[id]: https://example.com\n";
        var para = Parse(src).OfType<ParagraphBlock>().First();
        var link = ((TextBlock)BlockRenderer.Render(para)).Inlines.OfType<Hyperlink>().Single();
        Assert.AreEqual("example.com", link.NavigateUri!.Host);
    });

    [TestMethod]
    public void Autolink_AngleBracket_RendersHyperlink() => UiThread.Run(() =>
    {
        var link = Para("<https://example.com>\n").Inlines.OfType<Hyperlink>().Single();
        Assert.AreEqual("example.com", link.NavigateUri!.Host);
    });

    [TestMethod]
    public void HardLineBreak_RendersLineBreak() => UiThread.Run(() =>
    {
        // Two trailing spaces force a hard break.
        Assert.IsTrue(Para("a  \nb\n").Inlines.OfType<LineBreak>().Any());
    });

    [TestMethod]
    public void SoftLineBreak_RendersAsSpaceNotLineBreak() => UiThread.Run(() =>
    {
        var tb = Para("a\nb\n");
        Assert.IsFalse(tb.Inlines.OfType<LineBreak>().Any());
        StringAssert.Contains(RunText(tb), "a b");
    });

    [TestMethod]
    public void BackslashEscape_RendersLiteralAndSuppressesEmphasis() => UiThread.Run(() =>
    {
        var tb = Para("\\*not italic\\*\n");
        Assert.IsFalse(tb.Inlines.OfType<Span>().Any());             // no emphasis span
        StringAssert.Contains(RunText(tb), "*not italic*");
    });

    [TestMethod]
    public void EntityReferences_DecodeToCharacters() => UiThread.Run(() =>
    {
        var text = RunText(Para("&amp; &#9731;\n"));
        StringAssert.Contains(text, "&");
        StringAssert.Contains(text, "☃");                       // ☃ from &#9731;
    });

    [TestMethod]
    public void RawInlineHtml_IsDropped() => UiThread.Run(() =>
    {
        var text = RunText(Para("a <b>bold</b> c\n"));
        Assert.IsFalse(text.Contains('<'));                          // tags gone
        StringAssert.Contains(text, "bold");                         // inner text kept
    });

    [TestMethod]
    public void RemoteImage_ShowsAltTextInsteadOfFetching() => UiThread.Run(() =>
    {
        var tb = (TextBlock)BlockRenderer.Render(Parse("![alt text](https://example.com/x.png)\n")[0]);
        Assert.IsFalse(tb.Inlines.OfType<InlineUIContainer>().Any(), "remote image must not be fetched");
        StringAssert.Contains(RunText(tb), "alt text");
    });

    [TestMethod]
    public void LocalImage_EmbedsImageElement() => UiThread.Run(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexaflow-md-img-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            SaveOnePixelPng(Path.Combine(dir, "pixel.png"));
            var ctx = new MarkdownRenderContext { Palette = MarkdownPalette.Dark, BaseDirectory = dir };

            var tb = (TextBlock)BlockRenderer.Render(Parse("![alt](pixel.png)\n")[0], "", ctx);

            Assert.IsTrue(tb.Inlines.OfType<InlineUIContainer>().Any(c => c.Child is Image),
                "a resolvable local image should embed an Image element");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    });

    // ── Helpers ───────────────────────────────────────────────────────────

    private static TextBlock Para(string md) => (TextBlock)BlockRenderer.Render(Parse(md)[0]);

    /// <summary>Concatenates the text of the top-level runs in a rendered paragraph.</summary>
    private static string RunText(TextBlock tb) => string.Concat(tb.Inlines.OfType<Run>().Select(r => r.Text));

    private static void SaveOnePixelPng(string path)
    {
        var src     = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgr24, null, new byte[] { 0, 0, 0 }, 3);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(src));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}

using System.Windows.Media;

using Nexaflow.IO.Documents;
using Nexaflow.Markdown.Export;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Export;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.Export;

/// <summary>
/// A document written out as it is shown: what each piece is from the reading, and what it looks like from the layout — the
/// colour an alert is drawn in and the word over it, the number an item is drawn with, the picture another language drew where
/// it drew it — and a copy carrying the words and the marked-up text of what was copied.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("markdown-export")]
public class MarkdownExportTests
{
    [TestMethod]
    public void AnAlertIsWrittenInTheColourAndUnderTheWordItIsDrawnWith() => UiThread.Run(() =>
    {
        var quote = (ExportQuote)Exported("> [!WARNING]\n> Careful.\n").Blocks.Single();
        var warning = ((SolidColorBrush)WpfStyles.Light.Warning.Brush()).Color;

        Assert.AreEqual(new ExportCallout("WARNING", "Warning", Argb(warning)), quote.Callout);
        StringAssert.Contains(new HtmlDocumentWriter().Written(Exported("> [!WARNING]\n> Careful.\n")), $"#{warning.R:X2}{warning.G:X2}{warning.B:X2}");
    });

    [TestMethod]
    public void AnItemIsWrittenWithTheMarkerItIsDrawnWith() => UiThread.Run(() =>
    {
        var document = Exported("i. one\nii. two\n\n- [x] done\n");
        var (counted, ticked) = ((ExportList)document.Blocks[0], (ExportList)document.Blocks[1]);

        CollectionAssert.AreEqual(new[] { "i.", "ii." }, counted.Items.Select(item => item.Marker).ToArray());
        Assert.AreEqual('i', counted.Counting!.Bullet);
        Assert.AreEqual(true, ticked.Items.Single().Done);

        Assert.AreEqual("i. one\nii. two\n\n[x] done\n", new TextDocumentWriter().Written(document));
    });

    [TestMethod]
    public void WhatAnotherLanguageDrawsInACellIsThePictureThatCellHolds_WhereTheOccasionKeepsPictures() => UiThread.Run(() =>
    {
        var document = Exported("| f |\n|---|\n| $x^2$ |\n", new ExportOccasion { Pictures = true });
        var cell = ((ExportTable)document.Blocks.Single()).Rows[1].Cells.Single();
        var drawn = ((ExportWords)cell.Blocks.Single()).Words.OfType<ExportDrawnSpan>().Single().Drawing;

        Assert.AreEqual("latex", drawn.Language);
        Assert.AreEqual("x^2", drawn.Source);
        Assert.IsNotNull(drawn.Picture);
        CollectionAssert.AreEqual(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, drawn.Picture.Png.Take(4).ToArray(), "it is a PNG");
        Assert.IsTrue(drawn.Picture.Width > 0 && drawn.Picture.Height > 0);

        StringAssert.Matches(new HtmlDocumentWriter().Written(document), new System.Text.RegularExpressions.Regex("<td><img src=\"data:image/png;base64,"));
    });

    [TestMethod]
    public void WithoutPictures_ADiagramIsTheCodeItIsWrittenIn_OrTheSourceAScriptDrawsItFrom() => UiThread.Run(() =>
    {
        var document = Exported("```mermaid\npie\n  \"a\" : 1\n```\n");
        var drawn = ((ExportDrawn)document.Blocks.Single()).Drawing;

        Assert.IsNull(drawn.Picture);
        Assert.AreEqual("pie\n  \"a\" : 1", drawn.Source);

        StringAssert.Contains(new HtmlDocumentWriter().Written(document), "<pre><code class=\"language-mermaid\">pie\n  &quot;a&quot; : 1\n</code></pre>");
        StringAssert.Contains(new HtmlDocumentWriter(new HtmlWriting { Scripts = true }).Written(document), "<pre class=\"mermaid\">pie\n  &quot;a&quot; : 1</pre>");
    });

    [TestMethod]
    public void ADiagramWrittenInAQuoteIsItsOwnLines_WithoutTheQuotesMarks() => UiThread.Run(() =>
    {
        var quote = (ExportQuote)Exported("> ```mermaid\n> pie\n>   \"a\" : 1\n> ```\n").Blocks.Single();

        Assert.AreEqual("pie\n  \"a\" : 1", ((ExportDrawn)quote.Blocks.Single()).Drawing.Source);
    });

    [TestMethod]
    public void ACopyCarriesTheWordsAndTheMarkedUpTextOfWhatWasCopied() => UiThread.Run(() =>
    {
        var copy = MarkdownClipboard.Copied("# Title\n\nSome **bold** and `code`.\n", null);

        Assert.AreEqual("Title\n\nSome bold and code.\n", copy.Text);
        StringAssert.Contains(copy.Html, "<!--StartFragment--><h1 id=\"title\">Title</h1>");
        StringAssert.Contains(copy.Html, "<p>Some <strong>bold</strong> and <code>code</code>.</p>");
    });

    private static ExportDocument Exported(string markdown, ExportOccasion? occasion = null) =>
        LayoutExport.Exported(markdown, occasion ?? new ExportOccasion());

    private static uint Argb(Color colour) => ((uint)colour.A << 24) | ((uint)colour.R << 16) | ((uint)colour.G << 8) | colour.B;
}

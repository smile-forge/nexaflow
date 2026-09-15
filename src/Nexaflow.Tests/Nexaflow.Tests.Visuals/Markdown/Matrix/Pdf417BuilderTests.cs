using System.Linq;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.Pdf417;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// The <c>pdf417</c> block and its layout: the block's fields, dispatch through the one fenced-block router, rows
/// drawn taller than they are wide, the columns every row is made of, and the picture read back.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("pdf417-render")]
[CoversNode("pdf417-block-syntax")]
public class Pdf417BuilderTests
{
    [TestMethod]
    public void Pdf417IsADiagramLanguage()
    {
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("pdf417"));
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("PDF417"));
        Assert.IsFalse(DiagramRenderer.IsDiagramLanguage("pdf"));
    }

    [TestMethod]
    public void DispatchesThroughDiagramRenderer() => UiThread.Run(() =>
    {
        var element = DiagramRenderer.Render("pdf417", "type: text\ntext: hello", MarkdownPalette.Dark);

        Assert.IsTrue(((ContentElement)element).IsReadOnly);
    });

    [TestMethod]
    public void TakesTheQrTypeVocabulary()
    {
        var block = Read("type: wifi\nssid: Net\npassword: pw\nsecurity: WPA");
        Assert.AreEqual("WIFI:T:WPA;S:Net;P:pw;;", block.Payload);
    }

    [TestMethod]
    public void ReadsItsOwnShapeSettings()
    {
        var block = Read("type: text\ntext: x\ncolumns: 4\nec: 5\nrowHeight: 4\ntruncated: true\ncellSize: 3");

        Assert.AreEqual(4, block.Options.Columns);
        Assert.AreEqual(5, block.Options.ErrorCorrectionLevel);
        Assert.AreEqual(4, block.RowHeight);
        Assert.IsTrue(block.Options.Truncated);
        Assert.AreEqual(3, block.Settings.CellSize);
    }

    [TestMethod]
    public void RefusesSettingsOutsideTheStandard()
    {
        Assert.IsFalse(Pdf417BlockReader.TryRead(MatrixParser.Parse("type: text\ntext: x\ncolumns: 40"), out _, out var error));
        StringAssert.Contains(error, "30");

        Assert.IsFalse(Pdf417BlockReader.TryRead(MatrixParser.Parse("type: text\ntext: x\nrowHeight: 99"), out _, out error));
        StringAssert.Contains(error, "rowHeight");

        Assert.IsFalse(Pdf417BlockReader.TryRead(MatrixParser.Parse("type: text\ntext: x\ncolumnz: 4"), out _, out error));
        StringAssert.Contains(error, "columnz");
    }

    [TestMethod]
    public void RowsAreDrawnTallerThanTheyAreWide() => UiThread.Run(() =>
    {
        // The one code whose modules are not square. A PDF417 row carries nothing in its height, so it is drawn tall
        // enough for a scanner sweeping across the symbol to stay inside one row.
        const string source = "type: text\ntext: stacked\ncolumns: 2\ncellSize: 2\nmargin: 0\nrowHeight: 3";
        var symbol = Encoded(source);

        var laid = Build(source);

        Assert.AreEqual(symbol.Width * 2.0, laid.Size.Width);
        Assert.AreEqual(symbol.Height * 2.0 * 3, laid.Size.Height, "each row is three module widths tall");
    });

    [TestMethod]
    public void EveryRowIsStartIndicatorsCodewordsAndStop() => UiThread.Run(() =>
    {
        // Every one of them opens with a bar, so each starts where its column does: seventeen modules a column, the
        // start pattern first and three columns of data before the right indicator.
        var full = Build("type: text\ntext: columns\ncolumns: 3\ncellSize: 1\nmargin: 0\nrowHeight: 2");

        Assert.AreEqual(0, Left(full, Pdf417Builder.Start));
        Assert.AreEqual(17, Left(full, Pdf417Builder.LeftRowIndicator));
        Assert.AreEqual(34, Left(full, Pdf417Builder.Codewords));
        Assert.AreEqual(34 + 17 * 3, Left(full, Pdf417Builder.RightRowIndicator));
        Assert.AreEqual(34 + 17 * 4, Left(full, Pdf417Builder.Stop));
        Assert.AreEqual(full.Size.Width, MatrixLayouts.Of(full, Pdf417Builder.Stop).Single().Bounds.Right, 0.001,
                        "and the stop pattern closes on a bar");

        // A truncated symbol gives up its right indicator, and its stop is a single bar.
        var truncated = Build("type: text\ntext: columns\ncolumns: 3\ntruncated: true\ncellSize: 1\nmargin: 0");

        Assert.AreEqual(0, MatrixLayouts.Of(truncated, Pdf417Builder.RightRowIndicator).Length);
        Assert.AreEqual(34 + 17 * 3, Left(truncated, Pdf417Builder.Stop));
        Assert.AreEqual(1, MatrixLayouts.Of(truncated, Pdf417Builder.Stop).Single().Bounds.Width, 0.001);

        static double Left(Laid laid, string kind) => MatrixLayouts.Of(laid, kind).Single().Bounds.X;
    });

    [TestMethod]
    public void ThePictureReadsBack() => UiThread.Run(() =>
    {
        const string source = "type: url\nurl: https://markdown.org\ncolumns: 3\ncellSize: 2\nmargin: 2\nrowHeight: 3";
        var block = Read(source);
        var symbol = Encoded(source);

        var drawn = MatrixLayouts.ReadBack(Pdf417Builder.Element(source, DiagramRenderOptions.For(MarkdownPalette.Dark)),
                                           symbol.Width, symbol.Height, block.Settings, block.RowHeight);
        var decoded = Pdf417TestDecoder.Decode(drawn);

        Assert.AreEqual("https://markdown.org", decoded.Text);
        Assert.AreEqual(3, decoded.Columns);
    });

    [TestMethod]
    public void ABadBlock_StillDrawsACode_WithTheReason() => UiThread.Run(() =>
    {
        var laid = Build("type: text\ntext: x\ncolumns: 99");

        StringAssert.Contains(laid.Trouble.Single().Message, "30");
        Assert.AreEqual(1, MatrixLayouts.Of(laid, Pdf417Builder.Start).Length, "a code-shaped absence, not a gap");
        Assert.AreEqual(1, MatrixLayouts.Of(laid, MatrixPiece.Strike).Length);
    });

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Laid Build(string source) => Pdf417Builder.Build(source, MarkdownPalette.Dark, 1.0);

    private static Pdf417Block Read(string source)
    {
        Assert.IsTrue(Pdf417BlockReader.TryRead(MatrixParser.Parse(source), out var block, out var error), error);
        return block!;
    }

    private static Pdf417Symbol Encoded(string source)
    {
        var block = Read(source);
        Assert.IsTrue(Pdf417Encoder.TryEncode(block.Payload, block.Options, out var symbol, out var error), error);
        return symbol!;
    }
}

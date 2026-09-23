using System.Linq;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.DataMatrix;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;
using System.Windows;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// The layout end for Data Matrix: dispatch through the one fenced-block router, a rectangular symbol measuring to
/// its own width and height, a border on every data region, and the picture reading back.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("datamatrix-render")]
[CoversNode("matrix-renderer")]
public class DataMatrixBuilderTests
{
    [TestMethod]
    public void DataMatrixIsADiagramLanguage()
    {
        Assert.IsTrue(ContentLanguages.Reads("datamatrix"));
        Assert.IsTrue(ContentLanguages.Reads("DataMatrix"));
        Assert.IsTrue(ContentLanguages.Reads("data-matrix"));
        Assert.IsFalse(ContentLanguages.Reads("dm"));
    }

    [TestMethod]
    public void ADataMatrixFenceIsDrawnByItsLanguageWithNowhereInItToWrite() => UiThread.Run(() =>
    {
        var element = Alone.Drawn("datamatrix", "type: text\ntext: hello", StyleFormat.Dark);

        element.Measure(new Size(600, double.PositiveInfinity));
        Assert.IsFalse(element.AcceptsCaret, "a symbol has nowhere in it to write");
    });

    [TestMethod]
    public void ARectangle_MeasuresToItsOwnWidthAndHeight() => UiThread.Run(() =>
    {
        // A symbol whose two sides differ.
        const string source = "type: text\ntext: ABCDEFGH\nshape: rectangle\ncellSize: 3\nmargin: 1";
        var symbol = Encoded(source);

        var laid = Build(source);

        Assert.IsTrue(symbol.Width > symbol.Height);
        Assert.AreEqual((symbol.Width + 2) * 3.0, laid.Size.Width);
        Assert.AreEqual((symbol.Height + 2) * 3.0, laid.Size.Height);
    });

    [TestMethod]
    public void EveryRegionWearsAFinderAndAClock() => UiThread.Run(() =>
    {
        // The L runs down the left and along the bottom, and the clock across the top and down the right — so the
        // finder's bounds are the whole symbol, as the clock's are.
        var laid = Build("type: text\ntext: hello\ncellSize: 1\nmargin: 0");

        var finder = MatrixLayouts.Of(laid, DataMatrixBuilder.Finder).Single();
        var clock = MatrixLayouts.Of(laid, DataMatrixBuilder.Clock).Single();

        Assert.AreEqual(laid.Size.Width, finder.Bounds.Width, 0.001);
        Assert.AreEqual(laid.Size.Height, finder.Bounds.Height, 0.001);
        Assert.AreEqual(laid.Size.Width, clock.Bounds.Right, 0.001);
    });

    [TestMethod]
    public void TheInkCoversExactlyTheDarkModules_EachOnce_AcrossSeveralRegions() => UiThread.Run(() =>
    {
        // Long enough to need more than one data region, so the borders between regions are shared out too.
        var source = $"type: text\ntext: {new string('x', 120)}\ncellSize: 1\nmargin: 0";
        var symbol = Encoded(source);
        Assert.IsTrue(symbol.Size.RegionsAcross > 1, "the payload no longer needs several regions");

        int dark = 0;
        for (int y = 0; y < symbol.Height; y++)
            for (int x = 0; x < symbol.Width; x++)
                if (symbol[x, y]) dark++;

        Assert.AreEqual(dark, MatrixLayouts.InkedArea(Build(source)), dark * 0.01);
    });

    [TestMethod]
    public void ThePictureReadsBack() => UiThread.Run(() =>
    {
        // Rendered, rasterised, sampled at each module's centre and decoded.
        const string source = "type: ppn\npzn: 01234562\nlot: L1\ncellSize: 3\nmargin: 2";
        var block = Read(source);
        var symbol = Encoded(source);

        var drawn = MatrixLayouts.ReadBack(Alone.Drawn("datamatrix", source, StyleFormat.Dark),
                                           symbol.Width, symbol.Height, block.Settings);
        var decoded = DataMatrixTestDecoder.Decode(drawn);

        Assert.AreEqual(block.Payload, decoded.Text);
        Assert.AreEqual(DataMatrixMacro.Macro06, decoded.Macro);
    });

    [TestMethod]
    public void ABadBlock_StillDrawsACode_WithTheReason() => UiThread.Run(() =>
    {
        var laid = Build("type: ppn\npzn: 1");

        StringAssert.Contains(laid.Trouble.Single().Message, "PZN");
        Assert.AreEqual(1, MatrixLayouts.Of(laid, DataMatrixBuilder.Finder).Length, "a code-shaped absence, not a gap");
        Assert.AreEqual(1, MatrixLayouts.Of(laid, MatrixPiece.Strike).Length);
    });

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Laid Build(string source) => DataMatrixBuilder.Lay(source, StyleFormat.Dark);

    private static DataMatrixBlock Read(string source)
    {
        Assert.IsTrue(DataMatrixBlockReader.TryRead(MatrixParser.Parse(source), out var block, out var error), error);
        return block!;
    }

    private static DataMatrixSymbol Encoded(string source)
    {
        var block = Read(source);
        Assert.IsTrue(DataMatrixEncoder.TryEncode(block.Payload, block.Options, out var symbol, out var error), error);
        return symbol!;
    }
}

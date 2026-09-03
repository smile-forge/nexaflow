using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.Pdf417;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// The <c>pdf417</c> block and its drawing: the block body, dispatch through the one fenced-block
/// router, rows drawn taller than they are wide, and the picture read back.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("pdf417-render")]
[CoversNode("pdf417-block-syntax")]
public class Pdf417RendererTests
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

        Assert.IsInstanceOfType(element, typeof(Border));
        Assert.IsInstanceOfType(((Border)element).Child, typeof(System.Windows.Shapes.Path));
    });

    [TestMethod]
    public void TakesTheQrTypeVocabulary()
    {
        var block = Parse("type: wifi\nssid: Net\npassword: pw\nsecurity: WPA");
        Assert.AreEqual("WIFI:T:WPA;S:Net;P:pw;;", block.Payload);
    }

    [TestMethod]
    public void ReadsItsOwnShapeSettings()
    {
        var block = Parse("type: text\ntext: x\ncolumns: 4\nec: 5\nrowHeight: 4\ntruncated: true\ncellSize: 3");

        Assert.AreEqual(4, block.Options.Columns);
        Assert.AreEqual(5, block.Options.ErrorCorrectionLevel);
        Assert.AreEqual(4, block.RowHeight);
        Assert.IsTrue(block.Options.Truncated);
        Assert.AreEqual(3, block.Settings.CellSize);
    }

    [TestMethod]
    public void RefusesSettingsOutsideTheStandard()
    {
        Assert.IsFalse(Pdf417BlockParser.TryParse("type: text\ntext: x\ncolumns: 40", out _, out var error));
        StringAssert.Contains(error, "30");

        Assert.IsFalse(Pdf417BlockParser.TryParse("type: text\ntext: x\nrowHeight: 99", out _, out error));
        StringAssert.Contains(error, "rowHeight");

        Assert.IsFalse(Pdf417BlockParser.TryParse("type: text\ntext: x\ncolumnz: 4", out _, out error));
        StringAssert.Contains(error, "columnz");
    }

    [TestMethod]
    public void RowsAreDrawnTallerThanTheyAreWide() => UiThread.Run(() =>
    {
        // The one thing neither QR nor Data Matrix exercises in the shared renderer: a module that is
        // not square. A PDF417 row carries nothing in its height, so it is drawn tall enough for a
        // scanner sweeping across the symbol to stay inside one row.
        var block = Parse("type: text\ntext: stacked\ncolumns: 2\ncellSize: 2\nmargin: 0\nrowHeight: 3");
        Assert.IsTrue(Pdf417Encoder.TryEncode(block.Payload, block.Options, out var symbol, out var error), error);

        var border = (Border)WpfPdf417Renderer.Render(symbol!, block, MarkdownPalette.Dark);
        var path   = (System.Windows.Shapes.Path)border.Child;

        Assert.AreEqual(symbol!.Width * 2.0, path.Width);
        Assert.AreEqual(symbol.Height * 2.0 * 3, path.Height, "each row is three module widths tall");
    });

    [TestMethod]
    public void ThePictureReadsBack() => UiThread.Run(() =>
    {
        var block = Parse("type: url\nurl: https://markdown.org\ncolumns: 3\ncellSize: 2\nmargin: 2\nrowHeight: 3");
        Assert.IsTrue(Pdf417Encoder.TryEncode(block.Payload, block.Options, out var symbol, out var error), error);

        var decoded = Pdf417TestDecoder.Decode(ReadBackFromPixels(block, symbol!));

        Assert.AreEqual("https://markdown.org", decoded.Text);
        Assert.AreEqual(3, decoded.Columns);
    });

    [TestMethod]
    public void ABadBlock_ShowsTheReason_InsteadOfThrowing() => UiThread.Run(() =>
    {
        var element = DiagramRenderer.Render("pdf417", "type: text\ntext: x\ncolumns: 99", MarkdownPalette.Dark);
        StringAssert.Contains(AllText(element), "30");
    });

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Pdf417Block Parse(string source)
    {
        Assert.IsTrue(Pdf417BlockParser.TryParse(source, out var block, out var error), error);
        return block!;
    }

    /// <summary>Renders, rasterises, and samples the centre of each module back into a matrix.</summary>
    private static IModuleMatrix ReadBackFromPixels(Pdf417Block block, Pdf417Symbol symbol)
    {
        var element = WpfPdf417Renderer.Render(symbol, block, MarkdownPalette.Dark);
        element.Margin = default;

        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        element.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(element.DesiredSize.Width), (int)Math.Ceiling(element.DesiredSize.Height),
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        int stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        double cell = block.Settings.CellSize;
        double rowPitch = cell * block.RowHeight;
        var modules = new bool[symbol.Width, symbol.Height];

        for (int y = 0; y < symbol.Height; y++)
        for (int x = 0; x < symbol.Width; x++)
        {
            int px = (int)((block.Settings.Margin + x + 0.5) * cell);
            int py = (int)(block.Settings.Margin * cell + (y + 0.5) * rowPitch);
            int i  = py * stride + px * 4;
            modules[x, y] = (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3 < 128;
        }

        return new Sampled(modules);
    }

    private sealed class Sampled(bool[,] modules) : IModuleMatrix
    {
        public int Width  => modules.GetLength(0);
        public int Height => modules.GetLength(1);
        public bool this[int x, int y] => modules[x, y];
    }

    private static string AllText(DependencyObject root)
    {
        var text = new System.Text.StringBuilder();
        void Walk(DependencyObject node)
        {
            if (node is TextBlock tb) text.Append(tb.Text).Append('\n');
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Walk(VisualTreeHelper.GetChild(node, i));
            if (node is Border { Child: { } child }) Walk(child);
            if (node is Panel panel) foreach (UIElement e in panel.Children) Walk(e);
        }
        Walk(root);
        return text.ToString();
    }
}

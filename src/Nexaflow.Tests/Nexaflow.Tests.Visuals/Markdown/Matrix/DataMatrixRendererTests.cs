using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.DataMatrix;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// The drawing end for Data Matrix: dispatch through the one fenced-block router, a rectangular symbol
/// measuring to its own width and height, and the picture reading back — through the shared renderer,
/// which is the point of having one.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("datamatrix-render")]
[CoversNode("matrix-renderer")]
public class DataMatrixRendererTests
{
    [TestMethod]
    public void DataMatrixIsADiagramLanguage()
    {
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("datamatrix"));
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("DataMatrix"));
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("data-matrix"));
        Assert.IsFalse(DiagramRenderer.IsDiagramLanguage("dm"));
    }

    [TestMethod]
    public void DispatchesThroughDiagramRenderer() => UiThread.Run(() =>
    {
        var element = DiagramRenderer.Render("datamatrix", "type: text\ntext: hello", MarkdownPalette.Dark);

        Assert.IsInstanceOfType(element, typeof(Border));
        Assert.IsInstanceOfType(((Border)element).Child, typeof(System.Windows.Shapes.Path));
    });

    [TestMethod]
    public void ARectangle_MeasuresToItsOwnWidthAndHeight() => UiThread.Run(() =>
    {
        // The one thing QR could never exercise in the renderer: a symbol whose two sides differ.
        var block  = Parse("type: text\ntext: ABCDEFGH\nshape: rectangle\ncellSize: 3\nmargin: 1");
        Assert.IsTrue(DataMatrixEncoder.TryEncode(block.Payload, block.Options, out var symbol, out var error), error);

        var border = (Border)WpfDataMatrixRenderer.Render(symbol!, block, MarkdownPalette.Dark);
        var path   = (System.Windows.Shapes.Path)border.Child;

        Assert.IsTrue(symbol!.Width > symbol.Height);
        Assert.AreEqual(symbol.Width  * 3.0, path.Width);
        Assert.AreEqual(symbol.Height * 3.0, path.Height);
    });

    [TestMethod]
    public void ThePictureReadsBack() => UiThread.Run(() =>
    {
        // Rendered, rasterised, sampled at each module's centre and decoded — what a scanner does,
        // minus finding the symbol in a photograph.
        var block = Parse("type: ppn\npzn: 01234562\nlot: L1\ncellSize: 3\nmargin: 2");
        Assert.IsTrue(DataMatrixEncoder.TryEncode(block.Payload, block.Options, out var symbol, out var error), error);

        var read    = ReadBackFromPixels(block, symbol!);
        var decoded = DataMatrixTestDecoder.Decode(read);

        Assert.AreEqual(block.Payload, decoded.Text);
        Assert.AreEqual(DataMatrixMacro.Macro06, decoded.Macro);
    });

    [TestMethod]
    public void ABadBlock_ShowsTheReason_InsteadOfThrowing() => UiThread.Run(() =>
    {
        var element = DiagramRenderer.Render("datamatrix", "type: ppn\npzn: 1", MarkdownPalette.Dark);
        StringAssert.Contains(AllText(element), "PZN");
    });

    // ── Helpers ────────────────────────────────────────────────────────────

    private static DataMatrixBlock Parse(string source)
    {
        Assert.IsTrue(DataMatrixBlockParser.TryParse(source, out var block, out var error), error);
        return block!;
    }

    private static IModuleMatrix ReadBackFromPixels(DataMatrixBlock block, DataMatrixSymbol symbol)
    {
        var element = WpfDataMatrixRenderer.Render(symbol, block, MarkdownPalette.Dark);
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
        var modules = new bool[symbol.Width, symbol.Height];

        for (int y = 0; y < symbol.Height; y++)
        for (int x = 0; x < symbol.Width; x++)
        {
            int px = (int)((block.Settings.Margin + x + 0.5) * cell);
            int py = (int)((block.Settings.Margin + y + 0.5) * cell);
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

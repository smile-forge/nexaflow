using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Qr;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Tests.Visuals.Markdown.Qr;

/// <summary>
/// The drawing end: that a block reaches the renderer through the same dispatch every other fenced
/// diagram uses, that the geometry is the size the settings ask for, and that a bad block shows the
/// reason instead of throwing.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("qr-render")]
public class QrRendererTests
{
    private const string Source =
        """
        type: url
        url: https://markdown.org
        """;

    [TestMethod]
    public void QrIsADiagramLanguage()
    {
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("qr"));
        Assert.IsTrue(DiagramRenderer.IsDiagramLanguage("QR"));
        Assert.IsFalse(DiagramRenderer.IsDiagramLanguage("qrcode"));
    }

    [TestMethod]
    public void DispatchesThroughDiagramRenderer() => UiThread.Run(() =>
    {
        var element = DiagramRenderer.Render("qr", Source, MarkdownPalette.Dark);

        Assert.IsInstanceOfType(element, typeof(Border));
        Assert.IsInstanceOfType(((Border)element).Child, typeof(System.Windows.Shapes.Path));
    });

    [TestMethod]
    public void MeasuresToTheModuleCountTimesTheCellSize() => UiThread.Run(() =>
    {
        var block  = ParseBlock($"{Source}\ncellSize: 6\nmargin: 2");
        var matrix = QrEncoder.Encode(block.Payload, block.ErrorCorrection);
        var border = (Border)WpfQrRenderer.Render(matrix, block, MarkdownPalette.Dark);
        var path   = (System.Windows.Shapes.Path)border.Child;

        // The symbol itself, then the quiet zone the border holds around it.
        Assert.AreEqual(matrix.Size * 6.0, path.Width);
        Assert.AreEqual(matrix.Size * 6.0, path.Height);
        Assert.AreEqual(new Thickness(2 * 6), border.Padding);

        // Width is free of the block spacing the border carries vertically, so it measures to exactly the
        // two together.
        border.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.AreEqual((matrix.Size + 2 * 2) * 6.0, border.DesiredSize.Width, 0.5);
    });

    [TestMethod]
    public void ColoursComeFromThePalette_UnlessTheBlockOverridesThem() => UiThread.Run(() =>
    {
        // The palette's QR tokens rather than its text and surface brushes: a code that follows the
        // theme onto a dark background stops being scannable.
        var themed = (Border)WpfQrRenderer.Render(ParseBlock(Source), MarkdownPalette.Dark);
        Assert.AreEqual(((SolidColorBrush)MarkdownPalette.Dark.QrLight).Color,
                        ((SolidColorBrush)themed.Background).Color);

        var overridden = (Border)WpfQrRenderer.Render(
            ParseBlock($"{Source}\ndark: #123456\nlight: #fedcba"), MarkdownPalette.Dark);

        Assert.AreEqual(Color.FromRgb(0xFE, 0xDC, 0xBA), ((SolidColorBrush)overridden.Background).Color);
        Assert.AreEqual(Color.FromRgb(0x12, 0x34, 0x56),
                        ((SolidColorBrush)((System.Windows.Shapes.Path)overridden.Child).Fill).Color);
    });

    [TestMethod]
    public void GeometryCoversExactlyTheDarkModules() => UiThread.Run(() =>
    {
        // Runs are merged as they are emitted, so the figure count is well below the module count while
        // the area they cover still has to be every dark module.
        var block  = ParseBlock(Source);
        var matrix = QrEncoder.Encode(block.Payload, block.ErrorCorrection);
        var path   = (System.Windows.Shapes.Path)((Border)WpfQrRenderer.Render(matrix, block, MarkdownPalette.Dark)).Child;

        int dark = 0;
        for (int y = 0; y < matrix.Size; y++)
            for (int x = 0; x < matrix.Size; x++)
                if (matrix[x, y]) dark++;

        double cell = block.CellSize;
        Assert.AreEqual(dark * cell * cell, path.Data.GetArea(0.01, ToleranceType.Absolute), dark * 0.01);
    });

    [TestMethod]
    public void ABadBlock_ShowsTheReason_InsteadOfThrowing() => UiThread.Run(() =>
    {
        var element = DiagramRenderer.Render("qr", "type: barcode\ntext: x", MarkdownPalette.Dark);

        Assert.IsNotNull(element);
        StringAssert.Contains(AllText(element), "barcode");
    });

    [TestMethod]
    public void AnOversizedPayload_ShowsTheReason_InsteadOfThrowing() => UiThread.Run(() =>
    {
        var element = DiagramRenderer.Render("qr", $"type: text\ntext: {new string('a', 3000)}\nec: H",
                                             MarkdownPalette.Dark);

        StringAssert.Contains(AllText(element), "Too much data");
    });

    // ΓöÇΓöÇ Helpers ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    private static QrBlock ParseBlock(string source)
    {
        Assert.IsTrue(QrBlockParser.TryParse(source, out var block, out string? error), error);
        return block!;
    }

    /// <summary>Every TextBlock's text in the tree, joined ΓÇö enough to assert what an error element says.</summary>
    private static string AllText(DependencyObject root)
    {
        var text = new System.Text.StringBuilder();

        void Walk(DependencyObject node)
        {
            if (node is TextBlock tb) text.Append(tb.Text).Append('\n');
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                Walk(VisualTreeHelper.GetChild(node, i));

            if (node is Border { Child: { } child }) Walk(child);
            if (node is Panel panel) foreach (UIElement e in panel.Children) Walk(e);
        }

        Walk(root);
        return text.ToString();
    }
}

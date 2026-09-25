using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Matrix;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Markdown.Matrix;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Qr;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Qr;

/// <summary>
/// The drawing end: that a block reaches the builder through the same dispatch every other fenced block uses, that
/// the layout is the size the settings ask for and made of the parts a QR code is, and that a block which cannot be
/// drawn still draws a code — struck through, with the reason — rather than nothing.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("qr-render")]
public class QrBuilderTests
{
    private const string Source =
        """
        type: url
        url: https://markdown.org
        """;

    [TestMethod]
    public void QrIsADiagramLanguage()
    {
        Assert.IsTrue(ContentLanguages.Reads("qr"));
        Assert.IsTrue(ContentLanguages.Reads("QR"));
        Assert.IsFalse(ContentLanguages.Reads("qrcode"));
    }

    [TestMethod]
    public void AQrFenceIsDrawnByItsLanguageWithNowhereInItToWrite() => UiThread.Run(() =>
    {
        var element = Alone.Drawn("qr", Source, StyleFormat.Dark);

        var content = (ContentElement)element;
        content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Assert.IsFalse(content.AcceptsCaret, "nothing drawn is anything typed, so the caret arrows over it");
        Assert.AreEqual(0, content.Diagnostics.Count);
    });

    [TestMethod]
    public void MeasuresToTheModuleCountTimesTheCellSize() => UiThread.Run(() =>
    {
        var laid = Build($"{Source}\ncellSize: 6\nmargin: 2");
        int size = Encoded(Source).Size;

        // The symbol, and the quiet zone round it.
        Assert.AreEqual((size + 2 * 2) * 6.0, laid.Size.Width);
        Assert.AreEqual((size + 2 * 2) * 6.0, laid.Size.Height);
    });

    [TestMethod]
    public void ColoursComeFromThePalette_UnlessTheBlockOverridesThem() => UiThread.Run(() =>
    {
        // The palette's QR tokens rather than its text and surface brushes: a code that follows the theme onto a
        // dark background stops being scannable.
        var themed = Build(Source);
        Assert.AreEqual(((SolidColorBrush)StyleFormat.Dark.QrLight).Color,
                        ((SolidColorBrush)MatrixLayouts.Ground(themed).Foreground!).Color);

        var overridden = Build($"{Source}\ndark: #123456\nlight: #fedcba");
        Assert.AreEqual(Color.FromRgb(0xFE, 0xDC, 0xBA), ((SolidColorBrush)MatrixLayouts.Ground(overridden).Foreground!).Color);
        Assert.IsTrue(MatrixLayouts.Ink(overridden).All(mark => ((SolidColorBrush)mark.Fill!).Color == Color.FromRgb(0x12, 0x34, 0x56)));
    });

    [TestMethod]
    public void TheInkCoversExactlyTheDarkModules_EachOnce() => UiThread.Run(() =>
    {
        // Runs are merged and shared out among the parts, so the figure count is well below the module count —
        // while the area they cover still has to be every dark module, none of them twice.
        var matrix = Encoded(Source);

        int dark = 0;
        for (int y = 0; y < matrix.Size; y++)
            for (int x = 0; x < matrix.Size; x++)
                if (matrix[x, y]) dark++;

        double cell = MatrixSettings.DefaultCellSize;
        Assert.AreEqual(dark * cell * cell, MatrixLayouts.InkedArea(Build(Source)), dark * 0.01);
    });

    [TestMethod]
    public void AFinderStandsInThreeCorners_AndTimingRunsBetweenThem() => UiThread.Run(() =>
    {
        var laid = Build($"{Source}\ncellSize: 1\nmargin: 0");
        int size = Encoded(Source).Size;

        var corners = MatrixLayouts.Of(laid, QrBuilder.Finder).Select(finder => finder.Bounds.TopLeft).ToHashSet();
        CollectionAssert.AreEquivalent(new[] { new Point(0, 0), new Point(size - 7, 0), new Point(0, size - 7) },
                                       corners.ToArray());

        Assert.AreEqual(2, MatrixLayouts.Of(laid, QrBuilder.Timing).Length);
        Assert.AreEqual(1, MatrixLayouts.Of(laid, MatrixPiece.Modules).Length, "and everything else is its modules");
    });

    [TestMethod]
    public void ABadBlock_IsShownAsWritten_WithTheReason() => UiThread.Run(() =>
    {
        var laid = Build("type: barcode\ntext: x");

        StringAssert.Contains(laid.Trouble.Single().Message, "barcode");
        Assert.IsTrue(laid.ShowsSource, "a code is only read, so what will not read is put right in its source");

        var reason = (TextMark)MatrixLayouts.Of(laid, SourceShown.Reason).Single().Marks[0];
        StringAssert.Contains(reason.Glyphs.Text, "barcode", "with why, under it");
    });

    [TestMethod]
    public void AnOversizedPayload_SaysSo() => UiThread.Run(() =>
    {
        var laid = Build($"type: text\ntext: {new string('a', 3000)}\nec: H");

        StringAssert.Contains(laid.Trouble.Single().Message, "Too much data");
    });

    [TestMethod]
    public void NonsenseStillDrawsACode() => UiThread.Run(() =>
    {
        foreach (var nonsense in new[] { "", "just some prose", ": :", "}{ ::", "#", "type:\ncellSize: 999" })
        {
            var laid = Build(nonsense);

            Assert.IsTrue(laid.Size.Width > 0 && laid.Size.Height > 0, $"'{nonsense}' drew nothing");
            Assert.AreEqual(1, laid.Trouble.Count, $"'{nonsense}' drew without saying what was wrong");
        }
    });

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Laid Build(string source) => QrBuilder.Lay(source, StyleFormat.Dark);

    private static QrMatrix Encoded(string source)
    {
        Assert.IsTrue(QrBlockReader.TryRead(MatrixParser.Parse(source), out var block, out string? error), error);
        return QrEncoder.Encode(block!.Payload, block.ErrorCorrection);
    }
}

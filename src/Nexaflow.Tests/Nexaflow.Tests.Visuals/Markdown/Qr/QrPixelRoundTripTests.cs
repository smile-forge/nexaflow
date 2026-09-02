using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Qr;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Tests.Visuals.Markdown.Qr;

/// <summary>
/// The last gap between "the matrix is right" and "a phone can read it": every other test here asserts
/// on modules held in memory, but a camera only ever sees pixels. This one renders a block the way the
/// document does, reads the modules back out of the rasterised image, and decodes that.
///
/// <para>
/// It is the test that would catch a renderer drawing the grid a half-pixel out, transposed, inverted,
/// or with the quiet zone eating the first row ΓÇö none of which the matrix-level tests can see, and all
/// of which look like a perfectly plausible picture.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("qr-render")]
public class QrPixelRoundTripTests
{
    [TestMethod]
    public void EveryPayloadTypeSurvivesBeingDrawnAndReadBack() => UiThread.Run(() =>
    {
        (string Name, string Source)[] blocks =
        [
            ("url", """
                    type: url
                    url: https://markdown.org/tools/diagrams/qr/
                    cellSize: 5
                    """),
            ("wifi", """
                     type: wifi
                     ssid: Nexaflow Guest
                     password: s3cr3t-pass
                     security: WPA
                     cellSize: 5
                     """),
            ("vcard", """
                      type: vcard
                      name: Ada Lovelace
                      org: Analytical Engines
                      title: Engineer
                      email: ada@example.com
                      cellSize: 5
                      """),
        ];

        foreach (var (name, source) in blocks)
        {
            Assert.IsTrue(QrBlockParser.TryParse(source, out var block, out string? error), error);

            var matrix = QrEncoder.Encode(block!.Payload, block.ErrorCorrection);
            var read   = ReadBackFromPixels(block, matrix);

            Assert.AreEqual(block.Payload, QrTestDecoder.Decode(read),
                $"the drawn {name} code does not read back as its payload");
        }
    });

    [TestMethod]
    public void ASmallCellSizeStillRasterisesEveryModule() => UiThread.Run(() =>
    {
        // One device-independent pixel per module is the floor the parser allows, and the place a
        // rounding error would first swallow a row.
        Assert.IsTrue(QrBlockParser.TryParse("type: text\ntext: TIGHT PACKED 123\ncellSize: 1\nmargin: 0",
                                             out var block, out string? error), error);

        var matrix = QrEncoder.Encode(block!.Payload, block.ErrorCorrection);
        Assert.AreEqual(block.Payload, QrTestDecoder.Decode(ReadBackFromPixels(block, matrix)));
    });

    // ΓöÇΓöÇ Rasterise, then sample ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    /// <summary>
    /// Renders the block to a bitmap and samples the centre of each module back into a matrix ΓÇö what a
    /// scanner does, minus finding the code in a photograph.
    /// </summary>
    private static QrMatrix ReadBackFromPixels(QrBlock block, QrMatrix matrix)
    {
        var element = WpfQrRenderer.Render(matrix, block, MarkdownPalette.Dark);
        element.Margin = default;   // the block spacing is layout, not part of the picture

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

        double cell = block.CellSize;
        var modules = new bool[matrix.Size * matrix.Size];

        for (int y = 0; y < matrix.Size; y++)
        {
            for (int x = 0; x < matrix.Size; x++)
            {
                int px = (int)((block.Margin + x + 0.5) * cell);
                int py = (int)((block.Margin + y + 0.5) * cell);
                int i  = py * stride + px * 4;

                // Pbgra32: B, G, R, A. A dark module is far from the near-white background, so the
                // midpoint separates them without needing to know either colour.
                int luminance = (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3;
                modules[y * matrix.Size + x] = luminance < 128;
            }
        }

        return new QrMatrix(matrix.Version, matrix.ErrorCorrection, matrix.Mask, modules);
    }
}

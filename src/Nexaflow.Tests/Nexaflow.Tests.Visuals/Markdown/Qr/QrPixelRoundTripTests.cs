using Nexaflow.Markdown.Matrix;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Visuals.Markdown.Matrix;
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
            Assert.IsTrue(QrBlockReader.TryRead(MatrixParser.Parse(source), out var block, out string? error), error);

            var matrix = QrEncoder.Encode(block!.Payload, block.ErrorCorrection);
            var read   = ReadBackFromPixels(source, block, matrix);

            Assert.AreEqual(block.Payload, QrTestDecoder.Decode(read),
                $"the drawn {name} code does not read back as its payload");
        }
    });

    [TestMethod]
    public void ASmallCellSizeStillRasterisesEveryModule() => UiThread.Run(() =>
    {
        // One device-independent pixel per module is the floor the parser allows, and the place a
        // rounding error would first swallow a row.
        const string source = "type: text\ntext: TIGHT PACKED 123\ncellSize: 1\nmargin: 0";
        Assert.IsTrue(QrBlockReader.TryRead(MatrixParser.Parse(source), out var block, out string? error), error);

        var matrix = QrEncoder.Encode(block!.Payload, block.ErrorCorrection);
        Assert.AreEqual(block.Payload, QrTestDecoder.Decode(ReadBackFromPixels(source, block, matrix)));
    });

    // ΓöÇΓöÇ Rasterise, then sample ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    /// <summary>
    /// Renders the block through the same dispatch the document uses, rasterises it, and samples the centre
    /// of each module back into a matrix — what a scanner does, minus finding the code in a photograph.
    /// </summary>
    private static QrMatrix ReadBackFromPixels(string source, QrBlock block, QrMatrix matrix)
    {
        var drawn = MatrixLayouts.ReadBack(DiagramRenderer.Render("qr", source, StyleFormat.Dark),
                                           matrix.Size, matrix.Size, block.Settings);

        var modules = new bool[matrix.Size * matrix.Size];
        for (int y = 0; y < matrix.Size; y++)
            for (int x = 0; x < matrix.Size; x++)
                modules[y * matrix.Size + x] = drawn[x, y];

        return new QrMatrix(matrix.Version, matrix.ErrorCorrection, matrix.Mask, modules);
    }
}

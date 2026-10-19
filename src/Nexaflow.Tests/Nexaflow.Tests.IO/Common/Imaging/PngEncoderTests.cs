using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Imaging;

/// <summary>
/// The PNG writer, read back by a reader written from the specification (<see cref="PngReader"/>): the chunks are in
/// order with right checksums, the header says what was written, and undoing each row's filter gives the pixels back.
/// </summary>
[TestClass]
[CoversNode("owned-images")]
public class PngEncoderTests
{
    [TestMethod]
    public void APicture_ReadsBackPixelForPixel()
    {
        const int width = 37, height = 23;
        var rgb = new byte[width * height * 3];
        var random = new Random(7);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 3;
                rgb[i] = (byte)(x * 7);                  // a gradient, which Sub predicts
                rgb[i + 1] = (byte)(y * 11);             // one Up predicts
                rgb[i + 2] = (byte)random.Next(256);     // noise, which no filter helps
            }

        var picture = PngReader.Read(PngEncoder.EncodeRgb(rgb, width, height));

        Assert.AreEqual((width, height), (picture.Width, picture.Height));
        CollectionAssert.AreEqual(new[] { "IHDR", "IDAT", "IEND" }, picture.Chunks.ToArray());
        CollectionAssert.AreEqual(rgb, picture.Rgb);
    }

    [TestMethod]
    public void AFlatPicture_ShrinksToAFractionOfItsPixels()
    {
        var rgb = new byte[256 * 256 * 3];
        Array.Fill(rgb, (byte)200);

        var png = PngEncoder.EncodeRgb(rgb, 256, 256);

        Assert.IsTrue(png.Length < rgb.Length / 50, $"{png.Length} bytes for {rgb.Length} of pixels");
        CollectionAssert.AreEqual(rgb, PngReader.Read(png).Rgb);
    }

    [TestMethod]
    public void PixelsThatAreNotWidthTimesHeightTimesThree_AreRefused()
        => Assert.ThrowsExactly<ArgumentException>(() => PngEncoder.EncodeRgb(new byte[10], 2, 2));

    [TestMethod]
    [DataRow(10, 20, 15, 15)]
    [DataRow(10, 20, 30, 10)]
    [DataRow(50, 40, 45, 45)]
    public void Paeth_PicksTheNeighbourNearestItsEstimate(int left, int up, int upLeft, int expected)
        => Assert.AreEqual(expected, PngEncoder.Paeth(left, up, upLeft));
}

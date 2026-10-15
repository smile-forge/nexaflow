using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Imaging;

/// <summary>Pictures read through Windows' own decoders as 8-bit RGB: a PNG pixel for pixel, and bytes that are no picture refused.</summary>
[TestClass]
[CoversNode("owned-video")]
public class PictureDecoderTests
{
    [TestMethod]
    public void APng_ReadsBackPixelForPixel()
    {
        const int width = 37, height = 23;
        var rgb = new byte[width * height * 3];
        new Random(11).NextBytes(rgb);

        var picture = PictureDecoder.DecodeRgb(PngEncoder.EncodeRgb(rgb, width, height));

        Assert.AreEqual((width, height), (picture.Width, picture.Height));
        CollectionAssert.AreEqual(rgb, picture.Rgb);
    }

    [TestMethod]
    public void BytesThatAreNoPicture_AreRefused() =>
        Assert.ThrowsExactly<InvalidDataException>(() => PictureDecoder.DecodeRgb("not a picture"u8.ToArray()));
}

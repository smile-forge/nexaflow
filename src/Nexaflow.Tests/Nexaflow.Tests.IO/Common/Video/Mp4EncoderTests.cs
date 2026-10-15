
using Nexaflow.IO.Common.Video;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Video;

/// <summary>
/// The MP4 writer, read back box by box (<see cref="Mp4Reader"/>): one H.264 track of the frames written, at
/// their size and rate; and the colours it turns frames into, against BT.709's own figures.
/// </summary>
[TestClass]
[CoversNode("owned-video")]
public class Mp4EncoderTests
{
    [TestMethod]
    public void AClip_IsOneH264TrackOfItsFrames_AtTheirSizeAndRate()
    {
        const int width = 320, height = 192, rate = 24, frames = 30;
        using var file = new MemoryStream();
        using (var encoder = new Mp4Encoder(file, width, height, rate))
        {
            var rgb = new byte[width * height * 3];
            for (int f = 0; f < frames; f++)
            {
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int i = (y * width + x) * 3;
                        rgb[i] = (byte)(x + f * 4);   // a gradient that moves, so frames differ
                        rgb[i + 1] = (byte)y;
                        rgb[i + 2] = 128;
                    }
                encoder.AddFrame(rgb);
            }
            encoder.Finish();
            Assert.AreEqual(frames, encoder.Frames);
        }

        var clip = Mp4Reader.Read(file.ToArray());

        Assert.AreEqual(1, clip.Tracks, "one video track, no sound");
        Assert.AreEqual("avc1", clip.Codec, "H.264");
        Assert.AreEqual((width, height), (clip.Width, clip.Height));
        Assert.AreEqual(frames, clip.Frames, "a sample for every frame");
        Assert.AreEqual((double)frames / rate, clip.Seconds, 1.0 / rate, "it runs as long as its frames at their rate");
    }

    [TestMethod]
    public void AFrameThatIsNotWidthTimesHeightTimesThree_IsRefused()
    {
        using var file = new MemoryStream();
        using var encoder = new Mp4Encoder(file, 64, 64, 24);
        Assert.ThrowsExactly<ArgumentException>(() => encoder.AddFrame(new byte[64 * 64 * 3 - 1]));
    }

    [TestMethod]
    public void OddSides_AreRefused() =>
        Assert.ThrowsExactly<ArgumentException>(() => new Mp4Encoder(new MemoryStream(), 63, 64, 24));

    [TestMethod]
    [DataRow(255, 255, 255, 235, 128, 128, DisplayName = "white")]
    [DataRow(0, 0, 0, 16, 128, 128, DisplayName = "black")]
    [DataRow(255, 0, 0, 63, 102, 240, DisplayName = "red")]
    [DataRow(0, 255, 0, 173, 42, 26, DisplayName = "green")]
    [DataRow(0, 0, 255, 32, 240, 118, DisplayName = "blue")]
    public void AColour_IsItsBt709LimitedRangeYuv(int r, int g, int b, int y, int u, int v)
    {
        var rgb = new byte[2 * 2 * 3];
        for (int i = 0; i < 4; i++) (rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2]) = ((byte)r, (byte)g, (byte)b);
        var nv12 = new byte[2 * 2 * 3 / 2];

        Yuv.FromRgb(rgb, 2, 2, nv12);

        CollectionAssert.AreEqual(new byte[] { (byte)y, (byte)y, (byte)y, (byte)y, (byte)u, (byte)v }, nv12);
    }
}

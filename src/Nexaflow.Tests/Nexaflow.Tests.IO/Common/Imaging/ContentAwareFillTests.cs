using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Imaging;

/// <summary>Content-aware fill: a hole filled from the picture's own patches — exactly, where the picture repeats — and
/// nothing else touched.</summary>
[TestClass]
[CoversNode("images-edit-erase")]
public class ContentAwareFillTests
{
    /// <summary>Diagonal stripes in red and blue, a checker in green: every patch of it is found again elsewhere.</summary>
    private static byte[] Pattern(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                byte stripe = ((x + y) / 6) % 2 == 0 ? (byte)200 : (byte)60;
                rgba[i] = stripe;
                rgba[i + 1] = (x / 10 + y / 10) % 2 == 0 ? (byte)180 : (byte)90;
                rgba[i + 2] = (byte)(255 - stripe);
                rgba[i + 3] = (byte)(100 + x % 50);
            }
        return rgba;
    }

    private static byte[] Hole(int width, int height, int left, int top, int w, int h)
    {
        var mask = new byte[width * height];
        for (int y = top; y < top + h; y++)
            for (int x = left; x < left + w; x++) mask[y * width + x] = 255;
        return mask;
    }

    private static byte[] Damaged(byte[] rgba, byte[] mask)
    {
        var damaged = rgba.ToArray();
        for (int p = 0; p < mask.Length; p++)
            if (mask[p] != 0) (damaged[p * 4], damaged[p * 4 + 1], damaged[p * 4 + 2]) = (255, 0, 255);
        return damaged;
    }

    [TestMethod]
    public void AHoleInARepeatingPicture_IsFilledWithWhatWasThere()
    {
        var picture = Pattern(240, 200);
        var mask = Hole(240, 200, 90, 80, 60, 50);

        var filled = ContentAwareFill.Fill(Damaged(picture, mask), 240, 200, mask);

        double error = 0;
        int values = 0;
        for (int p = 0; p < mask.Length; p++)
            if (mask[p] != 0)
                for (int c = 0; c < 3; c++, values++) error += Math.Abs(filled[p * 4 + c] - picture[p * 4 + c]);
        Assert.IsTrue(error / values < 1, $"the fill is {error / values:F2} levels from what was there on average");
    }

    [TestMethod]
    public void OnlyTheMasksColour_Changes_EveryAlphaAndEveryPixelOutsideKept()
    {
        var picture = Damaged(Pattern(160, 120), Hole(160, 120, 40, 40, 30, 20));
        var mask = Hole(160, 120, 40, 40, 30, 20);

        var filled = ContentAwareFill.Fill(picture, 160, 120, mask);

        for (int p = 0; p < mask.Length; p++)
        {
            Assert.AreEqual(picture[p * 4 + 3], filled[p * 4 + 3], $"alpha at {p}");
            if (mask[p] == 0) CollectionAssert.AreEqual(picture[(p * 4)..(p * 4 + 3)], filled[(p * 4)..(p * 4 + 3)], $"pixel {p} is outside the mask");
        }
    }

    [TestMethod]
    public void TheSamePictureMaskAndSeed_FillTheSameWay()
    {
        var mask = Hole(200, 160, 70, 60, 40, 30);
        var picture = Damaged(Pattern(200, 160), mask);
        for (int p = 0; p < picture.Length; p += 7) picture[p] = (byte)(picture[p] ^ (p % 13));   // not quite repeating

        CollectionAssert.AreEqual(ContentAwareFill.Fill(picture, 200, 160, mask, seed: 3), ContentAwareFill.Fill(picture, 200, 160, mask, seed: 3));
    }

    [TestMethod]
    public void NothingMasked_ChangesNothing()
    {
        var picture = Pattern(64, 48);

        CollectionAssert.AreEqual(picture, ContentAwareFill.Fill(picture, 64, 48, new byte[64 * 48]));
    }

    [TestMethod]
    public void AMaskNotThePicturesSize_IsRefused()
        => Assert.ThrowsExactly<ArgumentException>(() => ContentAwareFill.Fill(new byte[16 * 4], 4, 4, new byte[10]));
}

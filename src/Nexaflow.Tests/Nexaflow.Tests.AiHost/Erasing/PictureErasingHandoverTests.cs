using Nexaflow.AiHost.Erasing;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost.Erasing;

/// <summary>A region to and from the inpainter as LaMa's predict hands its pictures over — padding, the mask, the 8-bit
/// result — and only the mask's pixels written back.</summary>
[TestClass]
[CoversNode("picture-erasing")]
public class PictureErasingHandoverTests
{
    private static byte[] Picture(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (int p = 0; p < width * height; p++) (rgba[p * 4], rgba[p * 4 + 1], rgba[p * 4 + 2], rgba[p * 4 + 3]) = ((byte)p, (byte)(p * 3), (byte)(255 - p), 200);
        return rgba;
    }

    [TestMethod]
    public void ARegion_IsPaddedToAMultipleOf8_MirroredAsNumPysSymmetricPadding()
    {
        // 5 x 3: padded to 8 x 8; column 5 repeats column 4, 6 is 3, 7 is 2; row 3 repeats row 2, then 1, 0, 0, 1.
        var rgba = Picture(5, 3);
        var prepared = ErasingHandover.Prepare(rgba, 5, new byte[15], new InpaintRegion(0, 0, 5, 3, 5, 3));

        Assert.AreEqual((8, 8), (prepared.Width, prepared.Height));
        int[] columns = [0, 1, 2, 3, 4, 4, 3, 2], rows = [0, 1, 2, 2, 1, 0, 0, 1];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                Assert.AreEqual(rgba[(rows[y] * 5 + columns[x]) * 4] / 255f, prepared.Rgb[y * 8 + x], $"red at {x},{y}");
    }

    [TestMethod]
    public void TheMask_IsOneWhereverItIsNotZero()
    {
        var mask = new byte[64];
        (mask[3], mask[10]) = (1, 255);
        var prepared = ErasingHandover.Prepare(Picture(8, 8), 8, mask, new InpaintRegion(0, 0, 8, 8, 8, 8));

        Assert.AreEqual(2, prepared.Mask.Count(v => v == 1f));
        Assert.AreEqual((1f, 1f), (prepared.Mask[3], prepared.Mask[10]));
    }

    [TestMethod]
    public void TheResult_IsWrittenOnlyWhereTheMaskFills_ClippedAndTruncatedTo8Bits_AlphaKept()
    {
        var rgba = Picture(8, 8);
        var before = rgba.ToArray();
        var mask = new byte[64];
        mask[9] = 255;
        var region = new InpaintRegion(0, 0, 8, 8, 8, 8);
        var prepared = ErasingHandover.Prepare(rgba, 8, mask, region);
        var inpainted = new float[3 * 64];
        Array.Fill(inpainted, 0.5f);            // 127.5 truncates to 127
        inpainted[9] = 1.2f;                    // red over 1 clips to 255

        ErasingHandover.LayBack(rgba, 8, mask, region, inpainted, prepared);

        Assert.AreEqual((255, 127, 127, before[9 * 4 + 3]), ((int)rgba[36], (int)rgba[37], (int)rgba[38], (int)rgba[39]));
        for (int p = 0; p < 64; p++)
            if (p != 9) CollectionAssert.AreEqual(before[(p * 4)..(p * 4 + 4)], rgba[(p * 4)..(p * 4 + 4)], $"pixel {p} is outside the mask");
    }

    [TestMethod]
    public void AShrunkRegion_IsWorkedAtItsWorkingSize_AndItsFillBroughtBackToTheMasksPixels()
    {
        var rgba = Picture(32, 16);
        var before = rgba.ToArray();
        var mask = new byte[32 * 16];
        for (int y = 4; y < 8; y++) for (int x = 8; x < 16; x++) mask[y * 32 + x] = 255;
        var region = new InpaintRegion(0, 0, 32, 16, 16, 8);

        var prepared = ErasingHandover.Prepare(rgba, 32, mask, region);
        Assert.AreEqual((16, 8), (prepared.Width, prepared.Height));
        Assert.IsTrue(prepared.Mask.Count(v => v == 1f) >= 4 * 2, "every working pixel the hole reaches is filled");

        var inpainted = new float[3 * 16 * 8];
        Array.Fill(inpainted, 0.25f);
        ErasingHandover.LayBack(rgba, 32, mask, region, inpainted, prepared);
        for (int p = 0; p < 32 * 16; p++)
        {
            var pixel = rgba[(p * 4)..(p * 4 + 4)];
            if (mask[p] != 0) CollectionAssert.AreEqual(new byte[] { 63, 63, 63, before[p * 4 + 3] }, pixel, $"pixel {p} is filled");
            else CollectionAssert.AreEqual(before[(p * 4)..(p * 4 + 4)], pixel, $"pixel {p} is outside the mask");
        }
    }
}

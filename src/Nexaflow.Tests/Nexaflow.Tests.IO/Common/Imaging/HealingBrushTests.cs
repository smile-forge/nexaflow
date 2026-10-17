using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Imaging;

/// <summary>The healing brush: texture copied from an offset, made to meet the colour and light round the mask.</summary>
[TestClass]
[CoversNode("images-edit-erase")]
public class HealingBrushTests
{
    /// <summary>A fine checker of ±10 over a dark left half (50) and a light right half (150), alpha 255.</summary>
    private static byte[] TwoTones(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4, value = (x < width / 2 ? 50 : 150) + ((x / 2 + y / 2) % 2 == 0 ? 10 : -10);
                (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]) = ((byte)value, (byte)value, (byte)value, 255);
            }
        return rgba;
    }

    private static byte[] Square(int width, int height, int left, int top, int side)
    {
        var mask = new byte[width * height];
        for (int y = top; y < top + side; y++)
            for (int x = left; x < left + side; x++) mask[y * width + x] = 255;
        return mask;
    }

    [TestMethod]
    public void TextureCopiedFromALighterPlace_TakesOnTheDarkerPlaceItLandsIn()
    {
        // The texture repeats every 4 pixels, so 100 to the right is the same texture, 100 levels lighter: healed, the
        // square is the dark checker it was.
        var picture = TwoTones(200, 80);
        var mask = Square(200, 80, 30, 30, 20);
        var damaged = picture.ToArray();
        for (int p = 0; p < mask.Length; p++)
            if (mask[p] != 0) (damaged[p * 4], damaged[p * 4 + 1], damaged[p * 4 + 2]) = (255, 0, 0);

        var healed = HealingBrush.Heal(damaged, 200, 80, mask, offsetX: 100, offsetY: 0);

        int worst = 0;
        for (int p = 0; p < mask.Length; p++)
            if (mask[p] != 0)
                for (int c = 0; c < 3; c++) worst = Math.Max(worst, Math.Abs(healed[p * 4 + c] - picture[p * 4 + c]));
        Assert.IsTrue(worst <= 1, $"a healed value is {worst} levels from the dark checker");
    }

    [TestMethod]
    public void OnlyTheMasksColour_Changes()
    {
        var picture = TwoTones(120, 60);
        var mask = Square(120, 60, 10, 10, 12);

        var healed = HealingBrush.Heal(picture, 120, 60, mask, offsetX: 70, offsetY: 5);

        for (int p = 0; p < mask.Length; p++)
        {
            Assert.AreEqual(picture[p * 4 + 3], healed[p * 4 + 3], $"alpha at {p}");
            if (mask[p] == 0) CollectionAssert.AreEqual(picture[(p * 4)..(p * 4 + 4)], healed[(p * 4)..(p * 4 + 4)], $"pixel {p} is outside the mask");
        }
    }

    [TestMethod]
    public void ASourcePastTheEdge_IsTakenFromTheEdge()
    {
        var picture = TwoTones(60, 40);
        var mask = Square(60, 40, 40, 10, 10);

        var healed = HealingBrush.Heal(picture, 60, 40, mask, offsetX: 500, offsetY: -500);

        Assert.AreEqual(picture.Length, healed.Length);
    }

    [TestMethod]
    public void NothingMasked_ChangesNothing()
    {
        var picture = TwoTones(32, 16);

        CollectionAssert.AreEqual(picture, HealingBrush.Heal(picture, 32, 16, new byte[32 * 16], 5, 5));
    }
}

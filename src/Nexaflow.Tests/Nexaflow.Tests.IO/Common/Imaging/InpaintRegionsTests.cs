using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Imaging;

/// <summary>Where a mask's fill is worked: round each part, with its surroundings, parts whose surroundings meet worked
/// together, a large region worked smaller.</summary>
[TestClass]
[CoversNode("picture-erasing")]
public class InpaintRegionsTests
{
    private static byte[] Mask(int width, int height, params (int Left, int Top, int Width, int Height)[] holes)
    {
        var mask = new byte[width * height];
        foreach (var (left, top, w, h) in holes)
            for (int y = top; y < top + h; y++)
                for (int x = left; x < left + w; x++) mask[y * width + x] = 255;
        return mask;
    }

    private static bool Holds(InpaintRegion region, (int Left, int Top, int Width, int Height) hole)
        => region.Left <= hole.Left && region.Top <= hole.Top
           && region.Left + region.Width >= hole.Left + hole.Width && region.Top + region.Height >= hole.Top + hole.Height;

    [TestMethod]
    public void NothingFilled_HasNoRegions()
        => Assert.AreEqual(0, InpaintRegions.Of(new byte[64 * 48], 64, 48).Count);

    [TestMethod]
    public void ASmallHole_IsWorkedInASquareOfItsSurroundings_AtLeastTheLeastSide()
    {
        var region = InpaintRegions.Of(Mask(2000, 1500, (1000, 700, 20, 10)), 2000, 1500).Single();

        Assert.AreEqual((InpaintRegions.LeastSide, InpaintRegions.LeastSide), (region.Width, region.Height));
        Assert.IsTrue(Holds(region, (1000, 700, 20, 10)), "the hole is inside its region");
        Assert.IsTrue(Math.Abs(region.Left + region.Width / 2 - 1010) <= 1 && Math.Abs(region.Top + region.Height / 2 - 705) <= 1,
                      "the region is centred on the hole");
        Assert.IsFalse(region.Shrunk);
    }

    [TestMethod]
    public void ALargeHole_IsWorkedWithAsMuchPictureAgainOnEverySide()
    {
        var region = InpaintRegions.Of(Mask(4000, 3000, (1500, 1200, 600, 400)), 4000, 3000).Single();

        Assert.AreEqual((900, 800, 1800, 1200), (region.Left, region.Top, region.Width, region.Height));
    }

    [TestMethod]
    public void AHoleAtTheEdge_HasItsRegionKeptInsideThePicture()
    {
        var region = InpaintRegions.Of(Mask(2000, 1500, (0, 1490, 30, 10)), 2000, 1500).Single();

        Assert.AreEqual((0, 1500 - InpaintRegions.LeastSide), (region.Left, region.Top));
        Assert.IsTrue(Holds(region, (0, 1490, 30, 10)));
    }

    [TestMethod]
    public void HolesFarApart_AreWorkedApart_AndHolesWhoseSurroundingsMeet_Together()
    {
        var apart = InpaintRegions.Of(Mask(3000, 1000, (100, 400, 20, 20), (2800, 400, 20, 20)), 3000, 1000);
        Assert.AreEqual(2, apart.Count);

        var near = InpaintRegions.Of(Mask(3000, 1000, (1000, 400, 20, 20), (1300, 400, 20, 20)), 3000, 1000);
        var joined = near.Single();
        Assert.IsTrue(Holds(joined, (1000, 400, 20, 20)) && Holds(joined, (1300, 400, 20, 20)));
    }

    [TestMethod]
    public void APictureSmallerThanTheLeastSide_IsWorkedWhole()
    {
        var region = InpaintRegions.Of(Mask(300, 200, (140, 90, 5, 5)), 300, 200).Single();

        Assert.AreEqual((0, 0, 300, 200, 300, 200), (region.Left, region.Top, region.Width, region.Height, region.WorkWidth, region.WorkHeight));
    }

    [TestMethod]
    public void ARegionLargerThanTheInpainterWorks_IsWorkedSmaller_ItsShapeKept()
    {
        var region = InpaintRegions.Of(Mask(6000, 4000, (1000, 1000, 4000, 2000)), 6000, 4000).Single();

        Assert.IsTrue(region.Shrunk);
        Assert.IsTrue((long)region.WorkWidth * region.WorkHeight <= InpaintRegions.MostWorkPixels);
        Assert.AreEqual((double)region.Width / region.Height, (double)region.WorkWidth / region.WorkHeight, 0.01);
    }

    [TestMethod]
    public void AMaskNotThePicturesSize_IsRefused()
        => Assert.ThrowsExactly<ArgumentException>(() => InpaintRegions.Of(new byte[10], 4, 4));
}

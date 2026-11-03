using Nexaflow.AiHost.SceneDepth;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost.SceneDepth;

/// <summary>A picture handed to Depth Anything V2 as its makers' infer_image prepares one: the working size its Resize gives
/// — the shorter side 518, both sides multiples of 14 — and the picture over 255, normalised by ImageNet's mean and deviation,
/// planar.</summary>
[TestClass]
[CoversNode("scene-depth")]
public class SceneDepthHandoverTests
{
    [TestMethod]
    [DataRow(2048, 1362, 784, 518, DisplayName = "a 3:2 photo: the shorter side 518, the longer the nearest multiple of 14")]
    [DataRow(640, 425, 784, 518, DisplayName = "a small one is brought up the same way")]
    [DataRow(1000, 1000, 518, 518, DisplayName = "a square one is the makers' own square")]
    [DataRow(425, 640, 518, 784, DisplayName = "upright: the width is the shorter side")]
    [DataRow(500, 2600, 392, 2072, DisplayName = "past four to one: brought to the longest side along its length")]
    public void TheWorkingSize_IsTheMakersLowerBound_AtMultiplesOf14(int width, int height, int workingWidth, int workingHeight)
        => Assert.AreEqual((workingWidth, workingHeight), SceneDepthHandover.WorkingSize(width, height));

    [TestMethod]
    public void APicture_IsNormalisedPlanarRgb_ItsAlphaUnread()
    {
        // A flat 518-pixel square: resized to itself, so each plane holds the colour's normalised value everywhere.
        int side = SceneDepthHandover.Side;
        var rgba = new byte[side * side * 4];
        for (int p = 0; p < side * side; p++) (rgba[p * 4], rgba[p * 4 + 1], rgba[p * 4 + 2], rgba[p * 4 + 3]) = (255, 0, 128, 7);

        var input = SceneDepthHandover.Prepare(rgba, side, side);

        Assert.AreEqual((side, side), (input.Width, input.Height));
        int plane = side * side;
        Assert.AreEqual((float)((1 - 0.485) / 0.229), input.Planes[0], 1e-6f);
        Assert.AreEqual((float)((0 - 0.456) / 0.224), input.Planes[plane + 100], 1e-6f);
        Assert.AreEqual((float)((128 / 255.0 - 0.406) / 0.225), input.Planes[2 * plane + plane - 1], 1e-6f);
    }
}

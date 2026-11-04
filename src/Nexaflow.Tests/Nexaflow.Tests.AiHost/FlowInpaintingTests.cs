using Nexaflow.AiHost.Sampling;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// Drawing again under a mask, as FLUX.2 [klein] and HiDream-O1 both do it: the picture noised as the scheduler scales
/// noise, and blended back where the mask leaves it — a mask value over a latent token's channels, or over a pixel's value.
/// </summary>
[TestClass]
[CoversNode("owned-images")]
public class FlowInpaintingTests
{
    [TestMethod]
    public void UnderAMask_ThePictureIsKeptWhereItIsZero_AndTheDrawingWhereItIsOne_EachValueOverItsWidth()
    {
        const int width = 4;
        var sample = Enumerable.Repeat(2f, 3 * width).ToArray();
        var kept   = Enumerable.Repeat(-1f, 3 * width).ToArray();

        FlowInpainting.Keep(sample, kept, [0f, 1f, 0.25f], width);

        Assert.AreEqual(-1f, sample[0]);
        Assert.AreEqual(-1f, sample[width - 1], "a mask value covers its whole width");
        Assert.AreEqual(2f, sample[width]);
        Assert.AreEqual(0.75f * -1f + 0.25f * 2f, sample[2 * width + 3], 1e-6f);
    }

    [TestMethod]
    public void AMaskWiderThanTheSample_IsRefused()
    {
        Assert.ThrowsExactly<ArgumentException>(() => FlowInpainting.Keep(new float[4], new float[4], [1f, 1f, 1f], 2));
    }

    [TestMethod]
    public void APicture_IsNoisedAsTheSchedulerScalesNoise()
    {
        var noised = FlowInpainting.Noised([1f, -2f], [0.5f, 4f], 0.25f);

        Assert.AreEqual(0.25f * 0.5f + 0.75f * 1f, noised[0], 1e-6f);
        Assert.AreEqual(0.25f * 4f + 0.75f * -2f, noised[1], 1e-6f);
    }
}

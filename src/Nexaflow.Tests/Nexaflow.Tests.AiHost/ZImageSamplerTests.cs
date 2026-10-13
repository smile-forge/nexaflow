using Nexaflow.AiHost.Drawing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The arithmetic around Z-Image's graphs, against what its PyTorch pipeline does: the noise levels, the patch layout
/// and its inverse, the prompt's padding and positions, and the sizes it draws at.
/// </summary>
[TestClass]
[CoversNode("owned-images")]
public class ZImageSamplerTests
{
    [TestMethod]
    public void TheNoiseLevels_AreThePipelinesForNineStepsAtShiftThree()
    {
        // FlowMatchEulerDiscreteScheduler with get_default_z_image_sigmas(9) and shift 3, printed from diffusers.
        float[] expected = [1.0f, 0.96f, 0.913044f, 0.857143f, 0.789474f, 0.705882f, 0.6f, 0.461538f, 0.272727f, 0f];

        var sigmas = ZImageSampler.Sigmas(9, 3f);

        Assert.AreEqual(expected.Length, sigmas.Length);
        for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], sigmas[i], 2e-6f, $"level {i}");
    }

    [TestMethod]
    public void APatchToken_HoldsItsTwoByTwoCells_RowColumnThenChannel()
    {
        const int h = 4, w = 6;
        var latents = new float[ZImageSampler.Channels * h * w];
        for (int i = 0; i < latents.Length; i++) latents[i] = i;   // channel-major, as [16, h, w]

        var tokens = ZImageSampler.Patchify(latents, h, w);

        // Token 1 is the patch at rows 0-1, columns 2-3; its 17th value is (row 0, column 3, channel 1).
        int within = (0 * 2 + 1) * ZImageSampler.Channels + 1;
        Assert.AreEqual(latents[(1 * h + 0) * w + 3], tokens[1 * ZImageSampler.TokenWidth + within]);
        Assert.AreEqual(h / 2 * (w / 2) * ZImageSampler.TokenWidth, tokens.Length);
    }

    [TestMethod]
    public void AStep_PutsTheTokensBackWhereTheyCameFrom()
    {
        const int h = 8, w = 10;
        var random = new Random(3);
        var latents = new float[ZImageSampler.Channels * h * w];
        for (int i = 0; i < latents.Length; i++) latents[i] = (float)random.NextDouble();
        var start = (float[])latents.Clone();

        ZImageSampler.Step(latents, ZImageSampler.Patchify(start, h, w), h, w, -1f);

        Assert.IsTrue(latents.All(v => Math.Abs(v) < 1e-6f), "stepping back by the latents' own tokens leaves nothing");
    }

    [TestMethod]
    public void ThePrompt_IsPaddedToAMultipleOf32_WithItsLastRow_AndEveryRowNumbered()
    {
        const int rows = 45, width = 3;
        var caption = new float[rows * width];
        for (int i = 0; i < caption.Length; i++) caption[i] = i;

        var (values, padding, positions) = ZImageSampler.PromptRows(caption, rows, width);

        Assert.AreEqual(64, padding.Length);
        Assert.AreEqual(45, padding.Count(p => !p));
        Assert.IsTrue(padding.Skip(45).All(p => p));
        Assert.AreEqual((Half)caption[^1], values[^1], "a padded row repeats the last real one");
        CollectionAssert.AreEqual(new long[] { 64, 0, 0 }, positions[^3..], "padded rows are numbered on, as Z-Image numbers them");
        CollectionAssert.AreEqual(new long[] { 1, 0, 0 }, positions[..3]);
    }

    [TestMethod]
    public void TheImage_IsPlacedAfterThePrompt_ByRowAndColumn()
    {
        var positions = ZImageSampler.ImagePositions(4, 6, promptRows: 64);

        CollectionAssert.AreEqual(new long[] { 65, 0, 0, 65, 0, 1, 65, 0, 2, 65, 1, 0, 65, 1, 1, 65, 1, 2 }, positions);
    }

    [TestMethod]
    [DataRow(1024, 1024, true)]
    [DataRow(1344, 768, true)]
    [DataRow(768, 1344, true)]
    [DataRow(1000, 1000, false)]
    [DataRow(1040, 1040, false)]
    public void ASize_IsDrawnInWholePatches_WhoseCountTheModelTakes(int width, int height, bool draws)
        => Assert.AreEqual(draws, ZImageSampler.Draws(width, height));

    [TestMethod]
    public void TheNoise_IsTheSameForTheSameSeed_AndStandardNormal()
    {
        var a = ZImageSampler.Noise(42, 64, 64);
        var b = ZImageSampler.Noise(42, 64, 64);
        var c = ZImageSampler.Noise(43, 64, 64);

        CollectionAssert.AreEqual(a, b);
        CollectionAssert.AreNotEqual(a, c);
        double mean = a.Average(), variance = a.Average(v => (v - mean) * (v - mean));
        Assert.AreEqual(0, mean, 0.02);
        Assert.AreEqual(1, variance, 0.02);
    }

    [TestMethod]
    public void Pixels_BecomeInterleavedRgb_RoundedAndClamped()
    {
        float[] planes = [0f, 1f, 2f, -1f, 0.5f, 0.25f];   // [3, 1, 2]: r = (0, 1), g = (2, -1), b = (0.5, 0.25)

        CollectionAssert.AreEqual(new byte[] { 0, 255, 128, 255, 0, 64 }, ZImageSampler.Rgb(planes, 1, 2));
    }
}

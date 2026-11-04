using Nexaflow.AiHost.Drawing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The arithmetic around FLUX.2 [klein]'s graphs, against what diffusers' Flux2KleinPipeline and
/// Flux2KleinInpaintPipeline do: the noise levels shifted by the picture's size, the positions of the latents and of each
/// picture worked from, the mask brought to the tokens' grid, the first picture kept under it, and the sizes it draws at.
/// </summary>
[TestClass]
[CoversNode("owned-images")]
public class KleinSamplerTests
{
    [TestMethod]
    [DataRow(256, new[] { 1.0f, 0.955391f, 0.877134f, 0.704112f, 0f })]
    [DataRow(4096, new[] { 1.0f, 0.967384f, 0.908144f, 0.7672f, 0f })]
    [DataRow(6400, new[] { 1.0f, 0.933305f, 0.823464f, 0.608588f, 0f })]
    public void TheNoiseLevels_AreThePipelinesForFourSteps_ShiftedByThePicturesSize(int tokens, float[] expected)
    {
        // FlowMatchEulerDiscreteScheduler from klein's scheduler_config.json, set_timesteps(linspace(1, 0.25, 4),
        // mu=compute_empirical_mu(tokens, 4)), printed from diffusers — 6400 past the 4300 tokens where mu stops following
        // the step count.
        var sigmas = KleinSampler.Sigmas(4, tokens);

        Assert.AreEqual(expected.Length, sigmas.Length);
        for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], sigmas[i], 2e-6f, $"level {i}");
    }

    [TestMethod]
    public void TheLatents_ComeFirst_ThenEachPicture_TenApartInTime()
    {
        var positions = KleinSampler.ImagePositions(2, 3, [(1, 2), (2, 1)]);

        long[][] expected =
        [
            [0, 0, 0, 0], [0, 0, 1, 0], [0, 0, 2, 0], [0, 1, 0, 0], [0, 1, 1, 0], [0, 1, 2, 0],
            [10, 0, 0, 0], [10, 0, 1, 0],
            [20, 0, 0, 0], [20, 1, 0, 0],
        ];
        Assert.AreEqual(expected.Length * 4, positions.Length);
        for (int t = 0; t < expected.Length; t++) CollectionAssert.AreEqual(expected[t], positions[(t * 4)..(t * 4 + 4)], $"token {t}");
    }

    [TestMethod]
    public void ThePromptsRows_AreNumberedOnTheLastAxis()
    {
        var positions = KleinSampler.PromptPositions();

        Assert.AreEqual(KleinSampler.PromptTokens * 4, positions.Length);
        CollectionAssert.AreEqual(new long[] { 0, 0, 0, 0 }, positions[..4]);
        CollectionAssert.AreEqual(new long[] { 0, 0, 0, 511 }, positions[^4..]);
    }

    [TestMethod]
    public void AMask_IsBroughtToTheTokensGrid_AsTorchInterpolatesIt()
    {
        // torch.nn.functional.interpolate(mode="bilinear") of a 48x80 mask, its right half and one stray pixel set, to
        // 3x5 tokens: [0, 0.5, 1] in every row — what the inpaint pipeline's prepare_mask_latents does.
        const int width = 48, height = 80;
        var mask = new byte[width * height];
        for (int y = 0; y < height; y++)
            for (int x = width / 2; x < width; x++)
                mask[y * width + x] = 255;
        mask[34 * width + 10] = 255;

        var tokens = KleinSampler.TokenMask(mask, width, height);

        float[] expected = [0, 0.5f, 1, 0, 0.5f, 1, 0, 0.5f, 1, 0, 0.5f, 1, 0, 0.5f, 1];
        Assert.AreEqual(expected.Length, tokens.Length);
        for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], tokens[i], 1e-6f, $"token {i}");
    }

    [TestMethod]
    public void TokensAndPlanes_AreEachOthersInverse()
    {
        const int rows = 3, cols = 5;
        var planes = new float[KleinSampler.Channels * rows * cols];
        for (int i = 0; i < planes.Length; i++) planes[i] = i;

        var tokens = KleinSampler.Tokens(planes, rows, cols);

        Assert.AreEqual(planes[1 * rows * cols + 7], tokens[7 * KleinSampler.Channels + 1], "token 7, channel 1");
        CollectionAssert.AreEqual(planes, KleinSampler.Planes(tokens, rows, cols));
    }

    [TestMethod]
    [DataRow(1024, 1024, true)]
    [DataRow(1360, 768, true)]
    [DataRow(16, 16, true)]
    [DataRow(1024, 1040, false)]
    [DataRow(1000, 1024, false)]
    public void ItDrawsWholeTokens_WithinAMegapixel(int width, int height, bool draws) =>
        Assert.AreEqual(draws, KleinSampler.Draws(width, height));
}

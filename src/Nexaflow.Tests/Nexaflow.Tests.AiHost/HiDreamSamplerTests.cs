using System.Text.Json.Nodes;
using Nexaflow.AiHost.Drawing;
using Nexaflow.AiHost.Sampling;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The arithmetic around HiDream-O1's denoiser, against what its own PyTorch code does: the UniPC sampler's timesteps,
/// noise levels and steps (recorded by tools/ai-models/hidream-o1/unipc_reference.py), the guidance, the patch layout,
/// the picture's positions and the sizes it draws at.
/// </summary>
[TestClass]
[CoversNode("owned-images")]
public class HiDreamSamplerTests
{
    private static JsonObject Reference()
    {
        using var stream = typeof(HiDreamSamplerTests).Assembly.GetManifestResourceStream("hidream-unipc.json")
                           ?? throw new InvalidOperationException("hidream-unipc.json is not embedded.");
        return JsonNode.Parse(stream)!.AsObject();
    }

    private static float[] Floats(JsonNode? node) => [.. node!.AsArray().Select(v => (float)v!.GetValue<double>())];

    [TestMethod]
    public void TheTimestepsAndNoiseLevels_AreHiDreamsForFiftyStepsAtShiftThree()
    {
        var reference = Reference();

        var sampler = FlowUniPc.ShiftedTwice(50, 3.0);

        CollectionAssert.AreEqual(reference["timesteps"]!.AsArray().Select(t => t!.GetValue<int>()).ToArray(), sampler.Timesteps,
                                  "the model is shown each level as a whole timestep, the level shifted twice");
        var sigmas = Floats(reference["sigmas"]);
        Assert.AreEqual(sigmas.Length, sampler.Sigmas.Count);
        for (int i = 0; i < sigmas.Length; i++) Assert.AreEqual(sigmas[i], sampler.Sigmas[i], 1e-6f, $"level {i}");
    }

    [TestMethod]
    public void EveryStep_IsHiDreamsUniPc_PredictedAndCorrected_WithGuidance()
    {
        var reference = Reference();
        float guidance = (float)reference["guidance"]!.GetValue<double>();
        var sample = Floats(reference["start"]);
        var cond = Floats(reference["cond_bias"]);
        var uncond = Floats(reference["uncond_bias"]);
        var sampler = FlowUniPc.ShiftedTwice(50, 3.0);
        var checkpoints = reference["trace"]!.AsArray().ToDictionary(t => t!["step"]!.GetValue<int>(), t => Floats(t!["sample"]));

        // The made-up model the reference was recorded with: predictions that move with the sample and the step.
        float[] Predict(float[] bias, int step) => [.. sample.Select((z, i) => MathF.Tanh(0.1f * z + bias[i] * (step + 1) / 50f))];

        for (int step = 0; step < 50; step++)
        {
            var output = HiDreamSampler.Guided(Predict(cond, step), Predict(uncond, step), sample, sampler.Timesteps[step], guidance);
            sample = sampler.Step(output, sample);
            if (!checkpoints.TryGetValue(step, out var expected)) continue;
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], sample[i], 1e-3f * Math.Max(1f, Math.Abs(expected[i])), $"step {step}, value {i}");
        }
    }

    [TestMethod]
    public void TheGuidedOutput_SteersTheVelocityFromThePromptAwayFromTheEmptyOnes_AndIsNegated()
    {
        // At t = 500 (σ = 0.5): v = (x − z) / σ; guided = v_without + 5 (v_with − v_without); the sampler steps along −guided.
        var output = HiDreamSampler.Guided([1f], [0.5f], [0f], 500, 5f);

        Assert.AreEqual(-(1f + 5f * (2f - 1f)), output[0], 1e-6f);
        Assert.AreEqual(0.4f, HiDreamSampler.TimestepInput(600), 1e-6f, "the timestep embedder reads 1 − t/1000");
    }

    [TestMethod]
    public void ThePicture_IsPlacedPastAnyPrompt_ByRowAndColumn()
    {
        var positions = HiDreamSampler.ImagePositions(2, 3);

        // [3, 6]: the time axis fixed at 4096, then 4096 + row, then 4096 + column, patch by patch, row by row.
        CollectionAssert.AreEqual(new long[] { 4096, 4096, 4096, 4096, 4096, 4096,
                                               4096, 4096, 4096, 4097, 4097, 4097,
                                               4096, 4097, 4098, 4096, 4097, 4098 }, positions);
    }

    [TestMethod]
    public void APatchToken_IsItsPixels_ChannelThenRowThenColumn_BackToRgbFromMinusOneToOne()
    {
        var tokens = new float[2 * HiDreamSampler.TokenWidth];
        int Within(int channel, int y, int x) => (channel * HiDreamSampler.Patch + y) * HiDreamSampler.Patch + x;
        tokens[Within(0, 0, 0)] = 1f;                                  // patch 0, red, top left: white
        tokens[HiDreamSampler.TokenWidth + Within(2, 1, 2)] = -1f;      // patch 1, blue, row 1 column 2: black
        tokens[HiDreamSampler.TokenWidth + Within(1, 1, 2)] = 3f;       // its green past the range: clamped

        var rgb = HiDreamSampler.Rgb(tokens, rows: 1, cols: 2);

        int width = 2 * HiDreamSampler.Patch;
        Assert.AreEqual(255, rgb[0], "red of the first pixel");
        Assert.AreEqual(128, rgb[1], "0 is the middle of the range");
        int pixel = (1 * width + HiDreamSampler.Patch + 2) * 3;
        Assert.AreEqual((byte)255, rgb[pixel + 1], "green, clamped");
        Assert.AreEqual((byte)0, rgb[pixel + 2], "blue");
    }

    [TestMethod]
    [DataRow(2048, 2048, true)]
    [DataRow(2560, 1440, true)]
    [DataRow(1440, 2560, true)]
    [DataRow(1024, 1024, false)]
    [DataRow(2048, 1024, false)]
    public void ASize_IsDrawnOnlyWhenTheModelWasTrainedOnIt(int width, int height, bool draws)
        => Assert.AreEqual(draws, HiDreamSampler.Draws(width, height));

    [TestMethod]
    public void TheNoise_IsTheSameForTheSameSeed_AndEightTimesStandardNormal()
    {
        var a = HiDreamSampler.Noise(42, 64);
        var b = HiDreamSampler.Noise(42, 64);
        var c = HiDreamSampler.Noise(43, 64);

        CollectionAssert.AreEqual(a, b);
        CollectionAssert.AreNotEqual(a, c);
        double mean = a.Average(), deviation = Math.Sqrt(a.Average(v => (v - mean) * (v - mean)));
        Assert.AreEqual(0, mean, 0.1);
        Assert.AreEqual(8, deviation, 0.1);
    }

    // ── editing, against hidream-edit.json (tools/ai-models/hidream-o1/edit_reference.py) ─────────────────────────────

    private static JsonObject Edit()
    {
        using var stream = typeof(HiDreamSamplerTests).Assembly.GetManifestResourceStream("hidream-edit.json")
                           ?? throw new InvalidOperationException("hidream-edit.json is not embedded.");
        return JsonNode.Parse(stream)!.AsObject();
    }

    private static long[] Longs(JsonNode? node) => [.. node!.AsArray().Select(v => v!.GetValue<long>())];

    [TestMethod]
    public void APictureToWorkFrom_IsReadByTheVisionTower_AtHiDreamsConditionSize()
    {
        foreach (var c in Edit()["conditions"]!.AsArray())
        {
            int width = c!["width"]!.GetValue<int>(), height = c["height"]!.GetValue<int>();
            var into = Longs(c["into"]);

            Assert.AreEqual(((int)into[0], (int)into[1]), HiDreamSampler.ConditionSize(width, height), $"{width}x{height}");
        }
    }

    [TestMethod]
    public void APicture_IsCutIntoTheVisionTowersPatches_AsQwen3VlsProcessorLaysThemOut()
    {
        var reader = Edit()["reader"]!;
        int width = reader["width"]!.GetValue<int>(), height = reader["height"]!.GetValue<int>();
        byte[] rgb = [.. reader["pixels"]!.AsArray().Select(v => (byte)v!.GetValue<int>())];
        float[] expected = [.. reader["patch_bytes"]!.AsArray().Select(v => (v!.GetValue<int>() - 127.5f) / 127.5f)];

        var patches = HiDreamSampler.ReaderPatches(rgb, width, height);

        Assert.AreEqual(reader["rows"]!.GetValue<int>() * reader["cols"]!.GetValue<int>() * HiDreamSampler.ReaderPatchWidth, patches.Length);
        CollectionAssert.AreEqual(expected, patches, "in 2x2 groups row by row; each patch its channel, two frames, its row, its column");
    }

    [TestMethod]
    public void EachPatch_IsSampledFromThePositionEmbedding_AndPlaced_AsQwen3VlsVisionTowerDoes()
    {
        var edit = Edit();
        foreach (var s in edit["samplings"]!.AsArray().Append(edit["reader"]))
        {
            int rows = s!["rows"]!.GetValue<int>(), cols = s["cols"]!.GetValue<int>();

            var (index, weights, positions) = HiDreamSampler.ReaderSampling(rows, cols);

            CollectionAssert.AreEqual(Longs(s["index"]), index, $"the cells, {rows}x{cols}");
            CollectionAssert.AreEqual(Longs(s["positions"]), positions, $"the rotary rows and columns, {rows}x{cols}");
            var expected = Floats(s["weights"]);
            Assert.AreEqual(expected.Length, weights.Length);
            for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], weights[i], $"weight {i}, {rows}x{cols}");
        }
    }

    [TestMethod]
    public void AnEditsTokens_ArePlacedAsHiDreamPlacesThem_ThePromptsPicturesAsGrids_ThePictureDrawnPastIt_ThePicturesWorkedFromAfter()
    {
        var prompt = Edit()["prompt"]!;
        var ids = Longs(prompt["ids"]);
        var grids = prompt["grids"]!.AsArray().Select(g => ((int)g![0]!.GetValue<long>(), (int)g[1]!.GetValue<long>())).ToList();
        var refs = prompt["refs"]!.AsArray().Select(g => ((int)g![0]!.GetValue<long>(), (int)g[1]!.GetValue<long>())).ToList();
        int rows = prompt["rows"]!.GetValue<int>(), cols = prompt["cols"]!.GetValue<int>();
        var expected = prompt["positions"]!.AsArray().Select(Longs).ToArray();   // [3, prompt + timestep + images]

        var inPrompt = HiDreamSampler.PromptPositions(ids, grids);
        var images = HiDreamSampler.ImagePositions(rows, cols, refs);

        int promptCount = ids.Length + 1, imageCount = images.Length / 3;
        Assert.AreEqual(expected[0].Length, promptCount + imageCount);
        for (int axis = 0; axis < 3; axis++)
        {
            CollectionAssert.AreEqual(expected[axis][..promptCount], inPrompt[(axis * promptCount)..((axis + 1) * promptCount)], $"the prompt's, axis {axis}");
            CollectionAssert.AreEqual(expected[axis][promptCount..], images[(axis * imageCount)..((axis + 1) * imageCount)], $"the images', axis {axis}");
        }
    }

    [TestMethod]
    public void APromptWithoutPictures_IsNumberedOnTheDiagonal_TheTimestepTokenLast()
    {
        CollectionAssert.AreEqual(new long[] { 0, 1, 2, 0, 1, 2, 0, 1, 2 }, HiDreamSampler.PromptPositions([5, 6], []));
        Assert.ThrowsExactly<ArgumentException>(() => HiDreamSampler.PromptPositions([HiDreamSampler.ImageToken], []), "a picture with no grid");
        Assert.ThrowsExactly<ArgumentException>(() => HiDreamSampler.PromptPositions([HiDreamSampler.ImageToken], [(1, 2)]), "a grid larger than its run");
    }

    [TestMethod]
    [DataRow(1, 2048, 1536, 2048)]
    [DataRow(2, 2048, 1536, 1536)]
    [DataRow(3, 1536, 2048, 1024)]
    [DataRow(4, 2048, 2048, 1024)]
    public void EachPictureWorkedFrom_IsReadWithinASquareThatShrinksWithHowManyThereAre(int sources, int width, int height, int side)
        => Assert.AreEqual(side, HiDreamSampler.ReferenceSide(sources, width, height));

    [TestMethod]
    [DataRow(2336, 1760, true)]
    [DataRow(64, 32, true)]
    [DataRow(2048, 2048, true)]
    [DataRow(2080, 2048, false)]
    [DataRow(100, 64, false)]
    public void AnEdit_IsDrawnAtAnySizeOfWholePatches_WithinItsSquare(int width, int height, bool reads)
        => Assert.AreEqual(reads, HiDreamSampler.Reads(width, height));

    [TestMethod]
    public void APicturesTokens_AreItsPixelsAsRgbReadsThemBack_AndAMaskLiesOverEachValue()
    {
        const int width = 64, height = 32;
        var rgb = new byte[width * height * 3];
        for (int i = 0; i < rgb.Length; i++) rgb[i] = (byte)(i * 7 % 256);
        var mask = new byte[width * height];
        mask[(5 * width) + 40] = 255;

        var tokens = HiDreamSampler.TokensOf(rgb, width, height);
        var values = HiDreamSampler.PixelMask(mask, width, height);

        CollectionAssert.AreEqual(rgb, HiDreamSampler.Rgb(tokens, rows: 1, cols: 2));
        int at = HiDreamSampler.TokenWidth + (5 * HiDreamSampler.Patch) + 8;   // the second patch, red, row 5, column 8
        Assert.AreEqual(1f, values[at]);
        Assert.AreEqual(1f, values[at + 2 * HiDreamSampler.Patch * HiDreamSampler.Patch], "and its blue");
        Assert.AreEqual(3f, values.Sum(), "the pixel's three values alone");
    }
}

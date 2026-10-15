using System.Text.Json.Nodes;
using Nexaflow.AiHost.Filming;
using Nexaflow.AiHost.Sampling;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost.Filming;

/// <summary>
/// The arithmetic around Wan2.2's graphs, against what its own Python does (recorded by
/// tools/ai-models/wan2.2-ti2v-5b/wan_reference.py): the UniPC scheduler's timesteps, noise levels and guided steps, the
/// noise levels FastWan's distilled timesteps stand for, prompts cleaned as Wan's pipeline cleans them — and the layouts
/// the graphs take: a picture held as the first latent frame, one latent frame cut out for the decoder.
/// </summary>
[TestClass]
[CoversNode("owned-video")]
public class WanSamplerTests
{
    private static JsonObject Reference()
    {
        using var stream = typeof(WanSamplerTests).Assembly.GetManifestResourceStream("wan-reference.json")
                           ?? throw new InvalidOperationException("wan-reference.json is not embedded.");
        return JsonNode.Parse(stream)!.AsObject();
    }

    private static float[] Floats(JsonNode? node) => [.. node!.AsArray().Select(v => (float)v!.GetValue<double>())];

    [TestMethod]
    public void TheTimestepsAndNoiseLevels_AreWansForFiftyStepsAtShiftFive()
    {
        var reference = Reference();

        var sampler = FlowUniPc.Shifted(50, 5.0);

        CollectionAssert.AreEqual(reference["timesteps"]!.AsArray().Select(t => t!.GetValue<int>()).ToArray(), sampler.Timesteps,
                                  "the model is shown each level as a whole timestep");
        var sigmas = Floats(reference["sigmas"]);
        Assert.AreEqual(sigmas.Length, sampler.Sigmas.Count);
        for (int i = 0; i < sigmas.Length; i++) Assert.AreEqual(sigmas[i], sampler.Sigmas[i], 1e-6f, $"level {i}");
    }

    [TestMethod]
    public void EveryStep_IsDiffusersUniPc_OnTheGuidedFlow()
    {
        var reference = Reference();
        float guidance = (float)reference["guidance"]!.GetValue<double>();
        var sample = Floats(reference["start"]);
        var cond = Floats(reference["cond_bias"]);
        var uncond = Floats(reference["uncond_bias"]);
        var sampler = FlowUniPc.Shifted(50, 5.0);
        var checkpoints = reference["trace"]!.AsArray().ToDictionary(t => t!["step"]!.GetValue<int>(), t => Floats(t!["sample"]));

        // The made-up model the reference was recorded with: a flow that moves with the sample and the step.
        float[] Flow(float[] bias, int step) => [.. sample.Select((z, i) => MathF.Tanh(0.1f * z + bias[i] * (step + 1) / 50f))];

        for (int step = 0; step < 50; step++)
        {
            sample = sampler.Step(WanSampler.Guided(Flow(cond, step), Flow(uncond, step), guidance), sample);
            if (!checkpoints.TryGetValue(step, out var expected)) continue;
            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], sample[i], 1e-4f * Math.Max(1f, Math.Abs(expected[i])), $"step {step}, value {i}");
        }
    }

    /// <summary>The shift FastWan2.2-TI2V-5B was distilled with, which its catalog entry gives.</summary>
    private const float FastWanShift = 8f;

    [TestMethod]
    public void ADistilledTimestep_StandsForTheNoiseLevelFastVideoReadsItAs()
    {
        foreach (var entry in Reference()["distilled"]!.AsArray())
            Assert.AreEqual(entry!["sigma"]!.GetValue<double>(), WanSampler.DistilledSigma(entry["timestep"]!.GetValue<int>(), FastWanShift), 1e-9,
                            $"timestep {entry["timestep"]}");
    }

    [TestMethod]
    public void ADistilledStep_PredictsTheCleanClip_ThenNoisesItAfreshToTheNextLevel()
    {
        // At σ = 0.75 the clean clip is x − σ·flow = 2 − 0.75·4 = −1; at the next level 0.5 it is (1 − 0.5)(−1) + 0.5·noise.
        var next = WanSampler.DistilledStep([2f], [4f], 0.75, 0.5, [3f]);
        var last = WanSampler.DistilledStep([2f], [4f], 0.75, null, []);

        Assert.AreEqual(0.5f * -1f + 0.5f * 3f, next[0], 1e-6f);
        Assert.AreEqual(-1f, last[0], 1e-6f, "the last step's prediction is the clip");
    }

    [TestMethod]
    public void GuidanceSteers_FromTheNegativePromptsFlow_TowardsAndPastThePromptsOwn() =>
        Assert.AreEqual(0.5f + 5f * (2f - 0.5f), WanSampler.Guided([2f], [0.5f], 5f)[0], 1e-6f);

    [TestMethod]
    public void APictureToStartFrom_IsTheFirstLatentFrame_AtTimestepZero()
    {
        var shape = new LatentShape(3, 2, 2);
        var latents = Enumerable.Range(0, shape.Values).Select(i => (float)i).ToArray();
        var picture = Enumerable.Repeat(-1f, shape.FrameValues).ToArray();

        WanSampler.KeepFirstFrame(latents, picture, shape);

        // [48, 3, 2, 2]: each channel's first frame is the picture's, its later two frames untouched.
        for (int c = 0; c < WanSampler.Channels; c++)
        {
            int plane = c * 3 * 4;
            CollectionAssert.AreEqual(new[] { -1f, -1f, -1f, -1f }, latents[plane..(plane + 4)], $"channel {c}'s first frame");
            Assert.AreEqual(plane + 4f, latents[plane + 4], $"channel {c}'s second frame");
        }
        CollectionAssert.AreEqual(new[] { 0f, 640f, 640f }, WanSampler.Timesteps(3, 640f, startsFromPicture: true));
        CollectionAssert.AreEqual(new[] { 640f, 640f, 640f }, WanSampler.Timesteps(3, 640f, startsFromPicture: false));
    }

    [TestMethod]
    public void ALatentFrame_IsCutOutChannelByChannel_ForTheDecoder()
    {
        var shape = new LatentShape(3, 2, 2);
        var latents = Enumerable.Range(0, shape.Values).Select(i => (float)i).ToArray();

        var second = WanSampler.LatentFrame(latents, shape, 1);

        Assert.AreEqual(shape.FrameValues, second.Length);
        CollectionAssert.AreEqual(new[] { 4f, 5f, 6f, 7f, 16f }, second[..5], "channel 0's second frame, then channel 1's");
    }

    [TestMethod]
    [DataRow(1280, 704, 121, true)]
    [DataRow(704, 1280, 121, true)]
    [DataRow(1280, 704, 1, true, DisplayName = "a single frame")]
    [DataRow(1280, 704, 120, false, DisplayName = "not one frame then whole latent frames")]
    [DataRow(1280, 700, 121, false, DisplayName = "not whole 2x2 patches of latent cells")]
    public void AClip_IsMadeOnlyOfWholePatchesAndLatentFrames(int width, int height, int frames, bool films)
    {
        Assert.AreEqual(films, WanSampler.Films(width, height, frames));
        if (films) Assert.AreEqual(new LatentShape((frames - 1) / 4 + 1, height / 16, width / 16), LatentShape.Of(frames, width, height));
    }

    [TestMethod]
    public void TheNoise_IsTheSameForTheSameSeed_DrawnInTurn()
    {
        var a = new ClipNoise(42);
        var b = new ClipNoise(42);

        var first = a.Draw(64);
        CollectionAssert.AreEqual(first, b.Draw(64));
        CollectionAssert.AreNotEqual(first, a.Draw(64), "each draw goes on from the last");
        double mean = first.Average(), deviation = Math.Sqrt(first.Average(v => (v - mean) * (v - mean)));
        Assert.AreEqual(0, mean, 0.35);
        Assert.AreEqual(1, deviation, 0.35);
    }

    [TestMethod]
    public void APrompt_IsCleanedAsWansPipelineCleansIt()
    {
        foreach (var entry in Reference()["cleaning"]!.AsArray())
            Assert.AreEqual(entry!["clean"]!.GetValue<string>(), WanPromptEncoder.Clean(entry["text"]!.GetValue<string>()), entry["text"]!.GetValue<string>());
    }
}

using Nexaflow.AiHost.Drawing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The arithmetic around MOSS-Image's graphs, against what the build recipe's check_moss_image.py computes — the loop the
/// graphs were checked with against MOSS-Image's PyTorch pipeline: the noise levels, the rotary rows, the timestep's
/// sinusoid, the guided step, the sizes it draws at, and a distillation's data-space sampler.
/// </summary>
[TestClass]
[CoversNode("owned-images-moss")]
public class MossSamplerTests
{
    [TestMethod]
    public void TheNoiseLevels_AreTheSchedulersFiftySnappedToTheTrainedLevels()
    {
        // check_moss_image.sigmas(): MOSSImageScheduler's fifty levels at shift 5, each the nearest k/1000.
        float[] expected = [1.0f, 0.996f, 0.992f, 0.987f, 0.983f, 0.978f, 0.973f, 0.968f, 0.963f, 0.957f, 0.952f, 0.946f, 0.939f,
                            0.933f, 0.926f, 0.919f, 0.912f, 0.905f, 0.897f, 0.888f, 0.88f, 0.871f, 0.861f, 0.851f, 0.84f, 0.829f, 0.817f,
                            0.805f, 0.791f, 0.777f, 0.762f, 0.746f, 0.729f, 0.711f, 0.692f, 0.671f, 0.648f, 0.623f, 0.597f, 0.568f,
                            0.536f, 0.501f, 0.463f, 0.421f, 0.373f, 0.321f, 0.261f, 0.193f, 0.115f, 0.024f, 0f];

        var sigmas = MossSampler.Sigmas(50, 5f);

        CollectionAssert.AreEqual(expected, sigmas, "every level is a trained one, k/1000 to float32, exactly");
    }

    [TestMethod]
    public void FewerSteps_DropALevelTheOneBeforeAlreadyTook()
    {
        var sigmas = MossSampler.Sigmas(2000, 5f);

        Assert.AreEqual(0f, sigmas[^1]);
        for (int i = 1; i < sigmas.Length; i++) Assert.IsTrue(sigmas[i] < sigmas[i - 1], $"level {i} repeats or rises");
        Assert.IsTrue(sigmas.Length <= MossSampler.TrainingSteps + 1, "never more levels than were trained");
    }

    [TestMethod]
    public void TheRotaryRows_AreTheReferencesForAPictureAndItsPrompt()
    {
        // check_moss_image.rope(4, 6, 3): 24 cells then 3 prompt rows; values at columns 0, 1, 17, 33, 49, 64, 81, 127.
        int[] columns = [0, 1, 17, 33, 49, 64, 81, 127];
        var expected = new Dictionary<int, float[]>
        {
            [0]  = [0f, 0f, 0.9932531714439392f, -0.7469826936721802f, -0.9864277243614197f, 1f, -0.11596616357564926f, 0.9999998807907104f],
            [5]  = [0f, 0f, 0.9932531714439392f, -0.7469826936721802f, 0.9864277243614197f, 1f, -0.11596616357564926f, 0.9999998807907104f],
            [23] = [0f, 0f, 0.9932531714439392f, 0.7469826936721802f, 0.9864277243614197f, 1f, -0.11596616357564926f, 0.9999998807907104f],
            [24] = [0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f],
            [26] = [0f, 0f, 0.9021307229995728f, 0f, 0f, 1f, 0.431462824344635f, 1f],
        };

        var rope = MossSampler.Rope(4, 6, 3);

        Assert.AreEqual(27 * MossSampler.RopeWidth, rope.Length);
        foreach (var (row, values) in expected)
            for (int i = 0; i < columns.Length; i++)
                Assert.AreEqual(values[i], rope[row * MossSampler.RopeWidth + columns[i]], 2e-6f, $"row {row}, column {columns[i]}");
    }

    [TestMethod]
    public void TheTimestepsSinusoid_IsTheReferencesCosinesThenSines()
    {
        // check_moss_image.time_rows(987.0), at columns 0, 1, 64, 127, 128, 200, 255.
        int[] columns = [0, 1, 64, 127, 128, 200, 255];
        float[] expected = [0.8577566146850586f, 0.4270671606063843f, -0.9025150537490845f, 0.994380533695221f, 0.5140560269355774f,
                            -0.66901034116745f, 0.10586504638195038f];

        var time = MossSampler.TimeRows(MossSampler.Timestep(0.987f));

        Assert.AreEqual(MossSampler.TimeWidth, time.Length);
        for (int i = 0; i < columns.Length; i++) Assert.AreEqual(expected[i], time[columns[i]], 1e-6f, $"column {columns[i]}");
    }

    [TestMethod]
    public void AGuidedStep_MovesAlongTheNegativeVelocitySteeredTowardThePrompts()
    {
        float[] latents = [1f, 2f];
        float[] withPrompt = [3f, -1f], withNegative = [1f, 1f];

        MossSampler.Step(latents, withPrompt, withNegative, guidance: 3f, step: -0.5f);

        // v = negative + 3 (prompt − negative) = (7, −5); x += −0.5 v.
        CollectionAssert.AreEqual(new[] { -2.5f, 4.5f }, latents);
    }

    [TestMethod]
    public void TheSizesItDraws_AreWholeCellsTheDenoiserTakes()
    {
        Assert.IsTrue(MossSampler.Draws(1024, 1024));
        Assert.IsTrue(MossSampler.Draws(1344, 768));
        Assert.IsTrue(MossSampler.Draws(256, 2048), "16 to 128 cells each way");
        Assert.IsFalse(MossSampler.Draws(1000, 1000), "not whole cells");
        Assert.IsFalse(MossSampler.Draws(240, 1024), "fewer than 16 cells");
        Assert.IsFalse(MossSampler.Draws(2064, 1024), "more than 128 cells");
    }

    [TestMethod]
    public void TheSameSeed_StartsFromTheSameNoise()
    {
        CollectionAssert.AreEqual(MossSampler.Noise(42, 16, 16), MossSampler.Noise(42, 16, 16));
        CollectionAssert.AreNotEqual(MossSampler.Noise(42, 16, 16), MossSampler.Noise(43, 16, 16));
        Assert.AreEqual(MossSampler.Channels * 16 * 16, MossSampler.Noise(1, 16, 16).Length);
    }

    [TestMethod]
    public void ADistillationsSampler_AsksAtTheNoisePlusItsMixOfTheEstimate_AndEstimatesZMinusV()
    {
        string file = Path.Combine(TestTemp.CreateSubdirectory("moss-sampler-").FullName, "fast.sampler.json");
        int channels = MossSampler.Channels;
        // Q swaps channels 0 and 1, doubles channel 2, and leaves the rest out.
        var mix = new double[channels][];
        for (int o = 0; o < channels; o++) mix[o] = new double[channels];
        mix[0][1] = 1; mix[1][0] = 1; mix[2][2] = 2;
        File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(new { kind = "dataspace", step_codes = new[] { 999.0, 500.5 }, latent_mix = mix }));

        var sampler = DataSpaceSampler.Read(file);
        int cells = 3;
        var noise = new float[channels * cells];
        var clean = new float[channels * cells];
        for (int i = 0; i < clean.Length; i++) { noise[i] = 1f; clean[i] = i / cells + 1; }   // channel c's estimate is c + 1

        var input = sampler.Input(noise, clean);

        CollectionAssert.AreEqual(new[] { 999f, 500.5f }, sampler.StepCodes.ToArray());
        Assert.AreEqual(1f + 2f, input[0 * cells], "channel 0 takes channel 1's estimate");
        Assert.AreEqual(1f + 1f, input[1 * cells + 2], "channel 1 takes channel 0's");
        Assert.AreEqual(1f + 2 * 3f, input[2 * cells + 1], "channel 2 doubles its own");
        Assert.AreEqual(1f, input[5 * cells], "a channel the mix leaves out is the noise alone");

        var velocity = new float[noise.Length];
        Array.Fill(velocity, 0.25f);
        DataSpaceSampler.Estimate(clean, noise, velocity);
        Assert.IsTrue(clean.All(v => v == 0.75f), "z − v");
    }

    [TestMethod]
    public void ASamplerFileThatIsNotADataSpaceOneForItsChannels_IsRefused()
    {
        string folder = TestTemp.CreateSubdirectory("moss-sampler-").FullName;
        string wrongKind = Path.Combine(folder, "a.json"), wrongMix = Path.Combine(folder, "b.json");
        File.WriteAllText(wrongKind, """{"kind":"euler","step_codes":[1],"latent_mix":[]}""");
        File.WriteAllText(wrongMix, """{"kind":"dataspace","step_codes":[1],"latent_mix":[[1,0],[0,1]]}""");

        StringAssert.Contains(Assert.ThrowsExactly<Nexaflow.AiHost.HostRequestException>(() => DataSpaceSampler.Read(wrongKind)).Message, "not a dataspace sampler");
        StringAssert.Contains(Assert.ThrowsExactly<Nexaflow.AiHost.HostRequestException>(() => DataSpaceSampler.Read(wrongMix)).Message, "128 × 128");
    }
}

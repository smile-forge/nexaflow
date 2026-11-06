using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Filming;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// Denoisers streamed against the same denoisers whole, in this process on the CUDA pack, where a machine has the model and its
/// streamed build (<c>denoiser.stream.json</c> beside the graph): the same latents and prompt give the same prediction, to the
/// rounding of fp16 arithmetic done in another order, and the GPU holds less giving it. Those drawn in a host of their own are
/// checked with their model's other tests (<c>AStreamedDenoiser_…</c>).
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("owned-images-streamed")]
public class StreamedDenoiserTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [Timeout(900_000, CooperativeCancellation = true)]
    public void WansDenoiser_Streamed_PredictsAsTheWholeOneDoes_HoldingLess()
    {
        string root = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_VIDEO") ?? "";
        string folder = Path.Combine(root, "denoiser");
        if (root.Length == 0 || PackRuntime.Bind() is null || !File.Exists(Path.Combine(folder, "denoiser.stream.json")))
            Assert.Inconclusive("NEXAFLOW_TEST_VIDEO names no denoiser with a streamed build, or NEXAFLOW_TEST_RUNTIME_PACK is not set.");
        var shape = new LatentShape(31, 44, 80);   // 1280 × 704, 121 frames
        var rng = new Random(9);
        var latents = new float[shape.Values];
        for (int i = 0; i < latents.Length; i++) latents[i] = (float)(rng.NextDouble() * 2 - 1);
        var timesteps = Enumerable.Repeat(757f, shape.Frames).ToArray();
        var text = new float[WanPromptEncoder.Rows * WanPromptEncoder.Width];
        for (int i = 0; i < 64 * WanPromptEncoder.Width; i++) text[i] = (float)(rng.NextDouble() * 0.2 - 0.1);
        var spec = new ModelSpec("wan2.2-ti2v-5b/denoiser", "wan-denoiser", folder, "denoiser.onnx", null, 16L << 30, 0, Device: 0);

        float[] Flow(ModelSpec at, out long peak)
        {
            using var watch = GpuMemoryWatch.Start();
            using var denoiser = (WanDenoiser)new WanDenoiserRunner().Load(at);
            var flow = denoiser.Flow(latents, shape, timesteps, text, lastCall: false);
            peak = watch.PeakBytes;
            return flow;
        }

        var streamed = Flow(spec with { File = "denoiser.stream.json" }, out long streamedPeak);
        var whole = Flow(spec, out long wholePeak);
        TestContext.WriteLine($"the GPU at its peak: streamed {streamedPeak / 1e9:0.00} GB, whole {wholePeak / 1e9:0.00} GB");
        double num = 0, den = 0;
        for (int i = 0; i < whole.Length; i++) { double d = streamed[i] - whole[i]; num += d * d; den += (double)whole[i] * whole[i]; }
        double relative = Math.Sqrt(num / den);
        Assert.IsTrue(relative < 1e-3, $"the streamed prediction is {relative:E2} from the whole one's");
        Assert.IsTrue(streamedPeak < wholePeak, $"streamed, the GPU held {streamedPeak / 1e9:0.00} GB at its peak, whole {wholePeak / 1e9:0.00} GB");
    }
}

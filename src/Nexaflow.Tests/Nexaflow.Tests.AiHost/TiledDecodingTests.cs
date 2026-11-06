using Nexaflow.AiHost;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Drawing;
using Nexaflow.AiHost.Filming;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// Decoders run in tiles against the same decoders whole, in this process on the CUDA pack — where a machine has the models:
/// <c>NEXAFLOW_TEST_IMAGE</c> and <c>NEXAFLOW_TEST_VIDEO</c> name their folders, <c>NEXAFLOW_TEST_RUNTIME_PACK</c> the pack. The
/// latents are smooth fields standing for a picture's; what is checked is that the tiles give the whole decode's picture, to within
/// what each tile's own normalisation moves it, and hold the GPU to less doing it.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("owned-images-tiled")]
public class TiledDecodingTests
{
    public TestContext TestContext { get; set; } = null!;

    private static string? Pack => PackRuntime.Bind();

    /// <summary>A smooth latent of <paramref name="channels"/> planes, the same each run: low waves, as a picture's varies across it.</summary>
    private static float[] Latent(int channels, int rows, int cols, int frame = 0)
    {
        var rng = new Random(5);
        var waves = Enumerable.Range(0, 32).Select(_ => (fx: rng.NextDouble() * 0.2, fy: rng.NextDouble() * 0.2, ft: rng.NextDouble() * 0.5,
                                                         p: rng.NextDouble() * 6.3, c: rng.Next(channels), a: rng.NextDouble())).ToArray();
        var latent = new float[channels * rows * cols];
        foreach (var w in waves)
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                    latent[w.c * rows * cols + y * cols + x] += (float)(w.a * Math.Sin(w.fx * x + w.fy * y + w.ft * frame + w.p));
        return latent;
    }

    private static double Psnr(ReadOnlySpan<float> a, ReadOnlySpan<float> b, double range)
    {
        double mse = 0;
        for (int i = 0; i < a.Length; i++) { double d = Math.Clamp(a[i], -range, range) - Math.Clamp(b[i], -range, range); mse += d * d; }
        mse /= a.Length;
        return mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(range * range * 4 / mse);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public void ZImagesDecoder_InTiles_GivesTheWholeDecodesPicture_HoldingLess()
    {
        string root = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_IMAGE") ?? "";
        if (root.Length == 0 || Pack is null) Assert.Inconclusive("NEXAFLOW_TEST_IMAGE and NEXAFLOW_TEST_RUNTIME_PACK are not both set.");
        const int rows = 192, cols = 192;   // 1536 × 1536
        var latent = Latent(ZImageSampler.Channels, rows, cols);
        var spec = new ModelSpec("z-image-turbo/decoder", "z-image-decoder", Path.Combine(root, "decoder"), "decoder.onnx", null, 6L << 30, 0, Device: 0);

        float[] Decode(ModelSpec at, out long peak)
        {
            using var watch = GpuMemoryWatch.Start();
            using var decoder = (ZImageDecoder)new ZImageDecoderRunner().Load(at);
            var picture = decoder.Decode(latent, rows, cols);
            peak = watch.PeakBytes;
            return picture;
        }

        var tiled = Decode(spec with { TileCells = 64 }, out long tiledPeak);
        var whole = Decode(spec, out long wholePeak);
        TestContext.WriteLine($"the GPU at its peak: tiles of 64 {tiledPeak / 1e9:0.00} GB, whole {wholePeak / 1e9:0.00} GB");
        double psnr = Psnr(tiled, whole, 1);
        Assert.IsTrue(psnr >= 35, $"tiles of 64 decode {psnr:0.0} dB from the whole picture");
        Assert.IsTrue(tiledPeak < wholePeak, $"in tiles the GPU held {tiledPeak / 1e9:0.00} GB at its peak, whole {wholePeak / 1e9:0.00} GB");
    }

    [TestMethod]
    [Timeout(900_000, CooperativeCancellation = true)]
    public void WansDecoder_InTiles_GivesTheWholeDecodesFrames_HoldingLess()
    {
        string root = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_VIDEO") ?? "";
        if (root.Length == 0 || Pack is null) Assert.Inconclusive("NEXAFLOW_TEST_VIDEO and NEXAFLOW_TEST_RUNTIME_PACK are not both set.");
        const int rows = 44, cols = 80, frames = 6;   // 1280 × 704, 21 frames
        var spec = new ModelSpec("wan2.2-ti2v-5b/decoder", "wan-decoder", Path.Combine(root, "decoder"), "decoder.onnx", null, 22L << 30, 0, Device: 0);

        byte[] Decode(ModelSpec at, out long peak)
        {
            using var watch = GpuMemoryWatch.Start();
            using var decoder = (WanDecoder)new WanDecoderRunner().Load(at);
            using var decoding = decoder.Start(rows, cols);
            var clip = new List<byte>();
            for (int f = 0; f < frames; f++) clip.AddRange(decoding.Next(Latent(WanSampler.Channels, rows, cols, f), f == frames - 1));
            peak = watch.PeakBytes;
            return [.. clip];
        }

        var tiled = Decode(spec with { TileCells = 24 }, out long tiledPeak);
        var whole = Decode(spec, out long wholePeak);
        TestContext.WriteLine($"the GPU at its peak: tiles of 24 {tiledPeak / 1e9:0.00} GB, whole {wholePeak / 1e9:0.00} GB");
        Assert.AreEqual(whole.Length, tiled.Length, "as many frames, of the same size");
        double mse = whole.Zip(tiled).Average(p => (p.First - p.Second) * (double)(p.First - p.Second));
        double psnr = 10 * Math.Log10(255.0 * 255 / mse);
        Assert.IsTrue(psnr >= 38, $"tiles of 24 decode {psnr:0.0} dB from the whole clip");
        Assert.IsTrue(tiledPeak < wholePeak, $"in tiles the GPU held {tiledPeak / 1e9:0.00} GB at its peak, whole {wholePeak / 1e9:0.00} GB");
    }
}

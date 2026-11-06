using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Drawing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The host drawing and editing with FLUX.2 [klein]'s real graphs. <c>NEXAFLOW_TEST_KLEIN</c> names a folder holding the
/// built model as its catalog lays it out (<c>encoder/</c>, <c>denoiser/</c>, <c>picture-encoder/</c>, <c>decoder/</c>);
/// <c>NEXAFLOW_TEST_RUNTIME_PACK</c> the CUDA pack, without which nothing is drawn — the model is built for the GPU. What is
/// asserted is the shape of a drawing — its steps, its size, that it is a picture — and, under a mask, that what the mask
/// leaves is kept; not what it shows.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("owned-images")]
public class KleinModelTests
{
    private const int Steps = 4;

    private static AiHostClient? _client;
    private static DrawingModel? _model;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_KLEIN") is not { Length: > 0 } root) return;
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_RUNTIME_PACK") is not { Length: > 0 } pack) return;

        var aiDir = TestTemp.CreateSubdirectory("nexaflow-aihost-").FullName;
        _client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"), aiDir))
        {
            Environment = new AiHostEnvironment(pack),
        };
        ModelSpec Part(string folder, string runner, long bytes) =>
            new($"flux2-klein-4b/{folder}", runner, Path.Combine(root, folder), $"{folder}.onnx", null, bytes, 0, Device: 0);
        _model = new DrawingModel(DrawingMethod.Flux2Klein, new Dictionary<string, ModelSpec>
        {
            [DrawingParts.PromptEncoder]  = Part("encoder", "klein-prompt-encoder", 5L << 30),
            [DrawingParts.Denoiser]       = Part("denoiser", "klein-denoiser", 11L << 30),
            [DrawingParts.PictureEncoder] = Part("picture-encoder", "klein-picture-encoder", 2L << 30),
            [DrawingParts.Decoder]        = Part("decoder", "klein-decoder", 5L << 30),
        }, Steps, Shift: 0f, Guidance: 0f);
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private static (AiHostClient Client, DrawingModel Model) Host()
    {
        if (_client is null || _model is null) Assert.Inconclusive("NEXAFLOW_TEST_KLEIN and NEXAFLOW_TEST_RUNTIME_PACK are not both set.");
        return (_client, _model);
    }

    private async Task<(List<HostReply> Replies, PngReader.Picture Picture)> DrawAsync(Func<long, Draw> draw, string name)
    {
        var (client, _) = Host();
        var replies = new List<HostReply>();
        await foreach (var reply in client.SendAsync(draw, TimeSpan.FromMinutes(5))) replies.Add(reply);
        var png = replies.OfType<Drawn>().Single().Png;
        var saved = Path.Combine(TestContext.TestRunResultsDirectory!, name);
        File.WriteAllBytes(saved, png);
        TestContext.AddResultFile(saved);   // to look at: the test checks its shape, a person checks what it shows
        return (replies, PngReader.Read(png));
    }

    private static void AssertAPicture(PngReader.Picture picture)
    {
        double mean = picture.Rgb.Average(b => (double)b);
        double spread = Math.Sqrt(picture.Rgb.Average(b => (b - mean) * (b - mean)));
        Assert.IsTrue(spread > 20, $"a picture, not a flat field (spread {spread:F1})");
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task APrompt_IsDrawnStepByStep_AsAPictureOfTheSizeAskedFor()
    {
        var (_, model) = Host();

        var (replies, picture) = await DrawAsync(id => new Draw(id, model, "A red fox asleep in the snow, watercolour.", 1360, 768, 7, RequestPriority.Job),
                                                 "drawn-fox-klein.png");

        var steps = replies.OfType<Progress>().Where(p => p.Stage == DrawHandler.Drawing && p.Done > 0).Select(p => p.Done).ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(1, Steps).Select(i => (long)i).ToArray(), steps, "each denoising step is reported as it finishes");
        Assert.AreEqual((1360, 768), (picture.Width, picture.Height));
        AssertAPicture(picture);
    }

    /// <summary>The same prompt and seed drawn with the denoiser whole and streamed, where its folder holds the streamed build: one
    /// picture, to the rounding of fp16 arithmetic done in another order.</summary>
    [TestMethod]
    [Timeout(900_000, CooperativeCancellation = true)]
    [CoversNode("owned-images-streamed")]
    public async Task AStreamedDenoiser_DrawsThePictureTheWholeOneDraws()
    {
        var (_, model) = Host();
        var denoiser = model.Parts[DrawingParts.Denoiser];
        if (!File.Exists(Path.Combine(denoiser.Folder, "denoiser.stream.json"))) Assert.Inconclusive("The denoiser's folder holds no streamed build.");
        var streamed = model with
        {
            Parts = new Dictionary<string, ModelSpec>(model.Parts) { [DrawingParts.Denoiser] = denoiser with { File = "denoiser.stream.json", MemoryBytes = 7L << 30 } },
        };

        PngReader.Picture whole, stream;
        
        long streamedPeak, wholePeak;
        using (var watch = GpuMemoryWatch.Start()) { stream = (await DrawAsync(id => new Draw(id, streamed, "A red fox asleep in the snow, watercolour.", 1360, 768, 7, RequestPriority.Job), "fox-klein-streamed.png")).Picture; streamedPeak = watch.PeakBytes; }
        using (var watch = GpuMemoryWatch.Start()) { whole = (await DrawAsync(id => new Draw(id, model, "A red fox asleep in the snow, watercolour.", 1360, 768, 7, RequestPriority.Job), "fox-klein-whole.png")).Picture; wholePeak = watch.PeakBytes; }
        TestContext.WriteLine($"the GPU at its peak: streamed {streamedPeak / 1e9:0.00} GB, whole {wholePeak / 1e9:0.00} GB");
        Assert.IsTrue(streamedPeak < wholePeak, $"streamed, the GPU held {streamedPeak / 1e9:0.00} GB at its peak, whole {wholePeak / 1e9:0.00} GB");
        double mse = whole.Rgb.Zip(stream.Rgb).Average(p => (p.First - p.Second) * (double)(p.First - p.Second));
        double psnr = mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255 / mse);
        Assert.IsTrue(psnr >= 35, $"the streamed denoiser's picture is {psnr:0.0} dB from the whole one's");
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task APicture_IsChangedByAnInstruction_AtItsOwnSize()
    {
        var (_, model) = Host();
        var (_, source) = await DrawAsync(id => new Draw(id, model, "A red-brick café on a street corner, morning light.", 1024, 768, 3, RequestPriority.Job),
                                          "edit-source-klein.png");

        var (_, edited) = await DrawAsync(id => new Draw(id, model, "Make it night time, the street lamps lit.", 1024, 768, 4, RequestPriority.Job)
                                                { Sources = [new DrawingSource(1024, 768, source.Rgb)] },
                                          "edited-night-klein.png");

        Assert.AreEqual((1024, 768), (edited.Width, edited.Height));
        AssertAPicture(edited);
        Assert.IsTrue(Mean(edited.Rgb) < Mean(source.Rgb) - 10, "night is darker than morning");
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task UnderAMask_OnlyWhatItCovers_IsDrawnAgain()
    {
        var (_, model) = Host();
        const int width = 768, height = 768;
        var (_, source) = await DrawAsync(id => new Draw(id, model, "A wooden table in a sunlit kitchen, an empty white plate on it.", width, height, 5, RequestPriority.Job),
                                          "fill-source-klein.png");
        var mask = new byte[width * height];
        for (int y = 256; y < 512; y++)
            for (int x = 256; x < 512; x++)
                mask[y * width + x] = 255;

        var (_, filled) = await DrawAsync(id => new Draw(id, model, "a slice of chocolate cake", width, height, 6, RequestPriority.Job)
                                                { Sources = [new DrawingSource(width, height, source.Rgb)], Mask = mask },
                                          "filled-cake-klein.png");

        // What the mask leaves comes back through the VAE: the same picture to within its rounding. What it covers is drawn again.
        double kept = Psnr(source.Rgb, filled.Rgb, width, (x, y) => (x < 224 || x >= 544) && (y < 224 || y >= 544));
        double covered = Psnr(source.Rgb, filled.Rgb, width, (x, y) => x is >= 288 and < 480 && y is >= 288 and < 480);
        Assert.IsTrue(kept > 28, $"what the mask leaves is kept ({kept:F1} dB)");
        Assert.IsTrue(covered < kept - 6, $"what the mask covers is drawn again ({covered:F1} dB against {kept:F1} dB)");
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task AMask_WithNoPictureToLieOver_IsRefused()
    {
        var (client, model) = Host();

        var ex = await Assert.ThrowsAsync<AiHostException>(async () =>
        {
            await foreach (var _ in client.SendAsync(id => new Draw(id, model, "A cat.", 512, 512, 1, RequestPriority.Job) { Mask = new byte[512 * 512] },
                                                     TimeSpan.FromMinutes(1))) { }
        });
        StringAssert.Contains(ex.Message, "mask");
    }

    [TestMethod]
    public void APrompt_IsReadAsThePipelineWritesIt_ThinkingOff_PaddedTo512()
    {
        string root = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_KLEIN") ?? "";
        if (root.Length == 0) Assert.Inconclusive("NEXAFLOW_TEST_KLEIN is not set.");
        var tokenizer = QwenTokenizer.FromJson(File.ReadAllText(Path.Combine(root, "encoder", "tokenizer.json")));
        var encoder = new KleinPromptEncoder(null!, null!, tokenizer);

        // Token ids from the pipeline's tokenizer, its chat template applied with enable_thinking=False, padded to 512.
        long[] english = [151644, 872, 198, 8078, 432, 3729, 882, 11, 279, 8592, 49452, 13020, 151645, 198, 151644, 77091, 198, 151667, 271, 151668, 271];
        long[] french  = [151644, 872, 198, 81581, 1959, 6185, 66961, 18857, 12486, 65671, 939, 17520, 8182, 151645, 198, 151644, 77091, 198, 151667, 271, 151668, 271];

        foreach (var (prompt, expected) in new[] { ("Make it night time, the street lamps lit", english), ("Bonjour — une enseigne « Café des Arts »", french) })
        {
            var (ids, mask) = encoder.Tokenize(prompt);
            Assert.AreEqual(KleinSampler.PromptTokens, ids.Length);
            CollectionAssert.AreEqual(expected, ids[..expected.Length], prompt);
            Assert.IsTrue(ids[expected.Length..].All(id => id == 151643), "padded with <|endoftext|>");
            Assert.AreEqual(expected.Length, (int)mask.Sum(), "the mask marks the prompt's tokens");
        }
    }

    private static double Mean(byte[] rgb) => rgb.Average(b => (double)b);

    private static double Psnr(byte[] a, byte[] b, int width, Func<int, int, bool> where)
    {
        double sum = 0;
        long count = 0;
        for (int i = 0; i < a.Length / 3; i++)
        {
            if (!where(i % width, i / width)) continue;
            for (int c = 0; c < 3; c++)
            {
                double d = a[i * 3 + c] - b[i * 3 + c];
                sum += d * d;
                count++;
            }
        }
        double mse = sum / count;
        return mse == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 / mse);
    }
}

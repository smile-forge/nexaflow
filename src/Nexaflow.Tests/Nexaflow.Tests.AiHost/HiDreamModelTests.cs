using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Drawing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The host drawing and editing with HiDream-O1's real graphs. <c>NEXAFLOW_TEST_HIDREAM</c> names a folder holding the
/// built model as its catalog lays it out (<c>denoiser/</c>, <c>picture-reader/</c>); <c>NEXAFLOW_TEST_RUNTIME_PACK</c>
/// the CUDA pack, without which nothing is drawn — the model is built for the GPU. What is asserted is the shape of a
/// drawing — its steps, its size, that it is a picture, what a mask keeps — not what it shows.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("owned-images")]
public class HiDreamModelTests
{
    private const int Steps = 50;

    private static AiHostClient? _client;
    private static DrawingModel? _model;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_HIDREAM") is not { Length: > 0 } root) return;
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_RUNTIME_PACK") is not { Length: > 0 } pack) return;

        var aiDir = TestTemp.CreateSubdirectory("nexaflow-aihost-").FullName;
        _client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"), aiDir))
        {
            Environment = new AiHostEnvironment(pack),
        };
        _model = new DrawingModel(DrawingMethod.HiDream, new Dictionary<string, ModelSpec>
        {
            [DrawingParts.Denoiser] = new("hidream-o1-image/denoiser", "hidream-denoiser", Path.Combine(root, "denoiser"), "denoiser.onnx", null, 12L << 30, 0, Device: 0),
            [DrawingParts.PictureReader] = new("hidream-o1-image/picture-reader", "hidream-picture-reader", Path.Combine(root, "picture-reader"),
                                                "picture-reader.onnx", null, 2L << 30, 0, Device: 0),
        }, Steps, Shift: 3f, Guidance: 5f);
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private static (AiHostClient Client, DrawingModel Model) Host()
    {
        if (_client is null || _model is null) Assert.Inconclusive("NEXAFLOW_TEST_HIDREAM and NEXAFLOW_TEST_RUNTIME_PACK are not both set.");
        return (_client, _model);
    }

    [TestMethod]
    [Timeout(1_200_000, CooperativeCancellation = true)]
    public async Task APrompt_IsDrawnStepByStep_AsAPictureOfTheSizeAskedFor()
    {
        var (client, model) = Host();

        var replies = new List<HostReply>();
        await foreach (var reply in client.SendAsync(id => new Draw(id, model, "A red fox asleep in the snow, watercolour.", 2560, 1440, 7, RequestPriority.Job),
                                                     TimeSpan.FromMinutes(5)))
            replies.Add(reply);

        var steps = replies.OfType<Progress>().Where(p => p.Stage == DrawHandler.Drawing && p.Done > 0).Select(p => p.Done).ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(1, Steps).Select(i => (long)i).ToArray(), steps, "each denoising step is reported as it finishes");

        var png = replies.OfType<Drawn>().Single().Png;
        var saved = Path.Combine(TestContext.TestRunResultsDirectory!, "drawn-fox-hidream.png");
        File.WriteAllBytes(saved, png);
        TestContext.AddResultFile(saved);   // to look at: the test checks its shape, a person checks what it shows
        var picture = PngReader.Read(png);
        Assert.AreEqual((2560, 1440), (picture.Width, picture.Height));
        double mean = picture.Rgb.Average(b => (double)b);
        double spread = Math.Sqrt(picture.Rgb.Average(b => (b - mean) * (b - mean)));
        Assert.IsTrue(spread > 20, $"a picture, not a flat field (spread {spread:F1})");
    }

    /// <summary>The same prompt and seed drawn with the denoiser whole and streamed, where its folder holds the streamed build: one
    /// picture, to the rounding of fp16 arithmetic done in another order.</summary>
    [TestMethod]
    [Timeout(2_400_000, CooperativeCancellation = true)]
    [CoversNode("owned-images-streamed")]
    public async Task AStreamedDenoiser_DrawsThePictureTheWholeOneDraws()
    {
        var (client, model) = Host();
        var denoiser = model.Parts[DrawingParts.Denoiser];
        if (!File.Exists(Path.Combine(denoiser.Folder, "denoiser.stream.json"))) Assert.Inconclusive("The denoiser's folder holds no streamed build.");
        var streamed = model with
        {
            Parts = new Dictionary<string, ModelSpec>(model.Parts) { [DrawingParts.Denoiser] = denoiser with { File = "denoiser.stream.json", MemoryBytes = 8L << 30 } },
        };

        async Task<PngReader.Picture> Draw(DrawingModel drawing)
        {
            await foreach (var reply in client.SendAsync(id => new Draw(id, drawing, "A red fox asleep in the snow, watercolour.", 2560, 1440, 7, RequestPriority.Job),
                                                         TimeSpan.FromMinutes(5)))
                if (reply is Drawn drawn) return PngReader.Read(drawn.Png);
            throw new AssertFailedException("nothing was drawn");
        }

        PngReader.Picture whole, stream;
        
        long streamedPeak, wholePeak;
        using (var watch = GpuMemoryWatch.Start()) { stream = await Draw(streamed); streamedPeak = watch.PeakBytes; }
        using (var watch = GpuMemoryWatch.Start()) { whole = await Draw(model); wholePeak = watch.PeakBytes; }
        TestContext.WriteLine($"the GPU at its peak: streamed {streamedPeak / 1e9:0.00} GB, whole {wholePeak / 1e9:0.00} GB");
        Assert.IsTrue(streamedPeak < wholePeak, $"streamed, the GPU held {streamedPeak / 1e9:0.00} GB at its peak, whole {wholePeak / 1e9:0.00} GB");
        double mse = whole.Rgb.Zip(stream.Rgb).Average(p => (p.First - p.Second) * (double)(p.First - p.Second));
        double psnr = mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255 / mse);
        Assert.IsTrue(psnr >= 35, $"the streamed denoiser's picture is {psnr:0.0} dB from the whole one's");
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task ADrawing_StopsBetweenSteps_WhenCancelled()
    {
        var (client, model) = Host();
        using var cancel = new CancellationTokenSource();

        var ex = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var reply in client.SendAsync(id => new Draw(id, model, "A city at night.", 2048, 2048, 5, RequestPriority.Job), TimeSpan.FromMinutes(5), cancel.Token))
                if (reply is Progress { Stage: DrawHandler.Drawing, Done: 2 }) cancel.Cancel();
        });
        Assert.IsNotNull(ex);
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ASizeTheModelWasNotTrainedOn_IsRefused()
    {
        var (client, model) = Host();

        var ex = await Assert.ThrowsAsync<AiHostException>(async () =>
        {
            await foreach (var _ in client.SendAsync(id => new Draw(id, model, "A cat.", 1024, 1024, 1, RequestPriority.Job), TimeSpan.FromMinutes(1))) { }
        });
        StringAssert.Contains(ex.Message, "1024x1024");
    }

    [TestMethod]
    public void APrompt_IsReadAsHiDreamWritesIt_UpToTheBeginImageToken()
    {
        string root = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_HIDREAM") ?? "";
        if (root.Length == 0) Assert.Inconclusive("NEXAFLOW_TEST_HIDREAM is not set.");
        var tokenizer = QwenTokenizer.FromJson(File.ReadAllText(Path.Combine(root, "denoiser", "tokenizer.json")));

        // Token ids from HiDream's own processor, chat template applied and <|boi_token|> after it.
        long[] french = [151644, 872, 198, 32, 51950, 42359, 2482, 5022, 429, 15804, 364, 2120, 266, 3845, 9682, 549, 40577, 266, 283, 4517,
                         220, 16, 17, 12984, 6, 304, 28485, 98389, 42359, 6524, 287, 13, 151645, 198, 151644, 77091, 198, 151669];
        long[] empty  = [151644, 872, 198, 220, 151645, 198, 151644, 77091, 198, 151669];

        CollectionAssert.AreEqual(french, HiDreamDenoiser.PromptIds(tokenizer,
            "A café chalkboard menu that reads 'Plat du jour : Ratatouille 12 €' in neat handwritten chalk lettering."));
        CollectionAssert.AreEqual(empty, HiDreamDenoiser.PromptIds(tokenizer, " "), "the empty prompt guidance steers from");
    }

    [TestMethod]
    [Timeout(1_800_000, CooperativeCancellation = true)]
    public async Task AFill_DrawsAgainOnlyWhatItsMaskCovers_KeepingThePictureElsewhereToThePixel()
    {
        var (client, model) = Host();
        const int width = 1536, height = 1024;
        var source = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int at = (y * width + x) * 3;
                (source[at], source[at + 1], source[at + 2]) = ((byte)(x * 255 / width), (byte)(y * 255 / height), (byte)((x / 64 + y / 64) % 2 * 200));
            }
        var mask = new byte[width * height];
        for (int y = 384; y < 640; y++)
            for (int x = 640; x < 896; x++) mask[y * width + x] = 255;

        var replies = new List<HostReply>();
        await foreach (var reply in client.SendAsync(id => new Draw(id, model, "A red apple on the board.", width, height, 3, RequestPriority.Job)
                                                     {
                                                         Sources = [new DrawingSource(width, height, source)],
                                                         Mask = mask,
                                                     }, TimeSpan.FromMinutes(10)))
            replies.Add(reply);

        var png = replies.OfType<Drawn>().Single().Png;
        var saved = Path.Combine(TestContext.TestRunResultsDirectory!, "filled-apple-hidream.png");
        File.WriteAllBytes(saved, png);
        TestContext.AddResultFile(saved);
        var picture = PngReader.Read(png);
        Assert.AreEqual((width, height), (picture.Width, picture.Height), "an edit is its picture's size");
        int kept = 0, changed = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            bool same = picture.Rgb[i * 3] == source[i * 3] && picture.Rgb[i * 3 + 1] == source[i * 3 + 1] && picture.Rgb[i * 3 + 2] == source[i * 3 + 2];
            if (mask[i] == 0) { if (same) kept++; }
            else if (!same) changed++;
        }
        Assert.AreEqual(mask.Count(m => m == 0), kept, "where the mask leaves it, the picture is as it was");
        Assert.IsTrue(changed > mask.Count(m => m != 0) / 2, $"what the mask covers is drawn again ({changed} pixels changed)");
    }

    [TestMethod]
    public void AnEditsPrompt_IsReadAsQwen3VlWritesIt_ThePicturesPlaceholdersBeforeTheText()
    {
        string root = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_HIDREAM") ?? "";
        if (root.Length == 0) Assert.Inconclusive("NEXAFLOW_TEST_HIDREAM is not set.");
        var tokenizer = QwenTokenizer.FromJson(File.ReadAllText(Path.Combine(root, "denoiser", "tokenizer.json")));
        using var stream = typeof(HiDreamModelTests).Assembly.GetManifestResourceStream("hidream-edit.json")!;
        var prompt = System.Text.Json.Nodes.JsonNode.Parse(stream)!["prompt"]!;
        int[] counts = [.. prompt["grids"]!.AsArray().Select(g => g![0]!.GetValue<int>() * g[1]!.GetValue<int>())];
        long[] Ids(string name) => [.. prompt[name]!.AsArray().Select(v => v!.GetValue<long>())];

        // HiDream's processor's tokens for two pictures and the text, with <|boi_token|> after them.
        CollectionAssert.AreEqual(Ids("ids"), HiDreamDenoiser.PromptIds(tokenizer, prompt["text"]!.GetValue<string>(), counts));
        CollectionAssert.AreEqual(Ids("empty_ids"), HiDreamDenoiser.PromptIds(tokenizer, " ", counts), "the empty prompt guidance steers from, with the pictures");
    }
}

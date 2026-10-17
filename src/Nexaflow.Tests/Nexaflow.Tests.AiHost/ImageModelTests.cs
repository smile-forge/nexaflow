using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Drawing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The host drawing with Z-Image Turbo's real graphs. <c>NEXAFLOW_TEST_IMAGE</c> names a folder holding the built model
/// as its catalog lays it out (<c>encoder/</c>, <c>denoiser/</c>, <c>decoder/</c>); <c>NEXAFLOW_TEST_RUNTIME_PACK</c>
/// the CUDA pack, without which nothing is drawn — the model is built for the GPU. What is asserted is the shape of a
/// drawing — its steps, its size, that it is a picture — not what it shows.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("owned-images")]
public class ImageModelTests
{
    private static AiHostClient? _client;
    private static DrawingModel? _model;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_IMAGE") is not { Length: > 0 } root) return;
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_RUNTIME_PACK") is not { Length: > 0 } pack) return;

        var aiDir = TestTemp.CreateSubdirectory("nexaflow-aihost-").FullName;
        _client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"), aiDir))
        {
            Environment = new AiHostEnvironment(pack),
        };
        _model = new DrawingModel(DrawingMethod.ZImage, new Dictionary<string, ModelSpec>
        {
            [DrawingParts.PromptEncoder] = new("z-image-turbo/promptEncoder", "z-image-prompt-encoder", Path.Combine(root, "encoder"), "encoder.onnx", null, 5L << 30, 0, Device: 0),
            [DrawingParts.Denoiser]      = new("z-image-turbo/denoiser", "z-image-denoiser", Path.Combine(root, "denoiser"), "denoiser.onnx", null, 10L << 30, 0, Device: 0),
            [DrawingParts.Decoder]       = new("z-image-turbo/decoder", "z-image-decoder", Path.Combine(root, "decoder"), "decoder.onnx", null, 6L << 30, 0, Device: 0),
        }, Steps: 9, Shift: 3f, Guidance: 0f);
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private static (AiHostClient Client, DrawingModel Model) Host()
    {
        if (_client is null || _model is null) Assert.Inconclusive("NEXAFLOW_TEST_IMAGE and NEXAFLOW_TEST_RUNTIME_PACK are not both set.");
        return (_client, _model);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task APrompt_IsDrawnStepByStep_AsAPictureOfTheSizeAskedFor()
    {
        var (client, model) = Host();

        var replies = new List<HostReply>();
        await foreach (var reply in client.SendAsync(id => new Draw(id, model, "A red fox asleep in the snow, watercolour.", 1344, 768, 7, RequestPriority.Job),
                                                     TimeSpan.FromMinutes(5)))
            replies.Add(reply);

        var steps = replies.OfType<Progress>().Where(p => p.Stage == DrawHandler.Drawing && p.Done > 0).Select(p => p.Done).ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(1, 9).Select(i => (long)i).ToArray(), steps, "each denoising step is reported as it finishes");

        var png = replies.OfType<Drawn>().Single().Png;
        var saved = Path.Combine(TestContext.TestRunResultsDirectory!, "drawn-fox.png");
        File.WriteAllBytes(saved, png);
        TestContext.AddResultFile(saved);   // to look at: the test checks its shape, a person checks what it shows
        var picture = PngReader.Read(png);
        Assert.AreEqual((1344, 768), (picture.Width, picture.Height));
        double mean = picture.Rgb.Average(b => (double)b);
        double spread = Math.Sqrt(picture.Rgb.Average(b => (b - mean) * (b - mean)));
        Assert.IsTrue(spread > 20, $"a picture, not a flat field (spread {spread:F1})");
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task TheSameSeedAndPrompt_DrawTheSamePicture()
    {
        var (client, model) = Host();

        async Task<byte[]> DrawAsync()
        {
            byte[]? png = null;
            await foreach (var reply in client.SendAsync(id => new Draw(id, model, "A lighthouse at dusk.", 1024, 1024, 99, RequestPriority.Job), TimeSpan.FromMinutes(5)))
                if (reply is Drawn drawn) png = drawn.Png;
            return png!;
        }

        CollectionAssert.AreEqual(PngReader.Read(await DrawAsync()).Rgb, PngReader.Read(await DrawAsync()).Rgb);
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task ADrawing_StopsBetweenSteps_WhenCancelled()
    {
        var (client, model) = Host();
        using var cancel = new CancellationTokenSource();

        var ex = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var reply in client.SendAsync(id => new Draw(id, model, "A city at night.", 1024, 1024, 5, RequestPriority.Job), TimeSpan.FromMinutes(5), cancel.Token))
                if (reply is Progress { Stage: DrawHandler.Drawing, Done: 2 }) cancel.Cancel();
        });
        Assert.IsNotNull(ex);
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task ASizeTheModelDoesNotDraw_IsRefused()
    {
        var (client, model) = Host();

        var ex = await Assert.ThrowsAsync<AiHostException>(async () =>
        {
            await foreach (var _ in client.SendAsync(id => new Draw(id, model, "A cat.", 1000, 1000, 1, RequestPriority.Job), TimeSpan.FromMinutes(1))) { }
        });
        StringAssert.Contains(ex.Message, "1000x1000");
    }

    [TestMethod]
    public void ThePromptEncoder_TokenisesAsQwen3Does()
    {
        string root = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_IMAGE") ?? "";
        if (root.Length == 0) Assert.Inconclusive("NEXAFLOW_TEST_IMAGE is not set.");
        var tokenizer = QwenTokenizer.FromJson(File.ReadAllText(Path.Combine(root, "encoder", "tokenizer.json")));
        const string template = "<|im_start|>user\n{0}<|im_end|>\n<|im_start|>assistant\n";

        // Token ids from the Hugging Face tokenizer Z-Image's pipeline uses, chat template applied.
        int[] french = [151644, 872, 198, 32, 51950, 42359, 2482, 5022, 429, 15804, 364, 2120, 266, 3845, 9682, 549, 40577, 266, 283, 4517,
                        220, 16, 17, 12984, 6, 304, 28485, 98389, 42359, 6524, 287, 13, 151645, 198, 151644, 77091, 198];
        int[] german = [151644, 872, 198, 32, 5938, 8592, 1841, 429, 15804, 364, 98922, 16719, 1055, 135786, 516, 220, 16, 24, 24, 24, 489,
                        4122, 11, 49371, 143721, 1959, 137208, 220, 16, 15, 13, 15, 15, 15, 0, 151645, 198, 151644, 77091, 198];

        CollectionAssert.AreEqual(french, tokenizer.EncodeToIds(string.Format(template,
            "A café chalkboard menu that reads 'Plat du jour : Ratatouille 12 €' in neat handwritten chalk lettering.")).ToArray());
        CollectionAssert.AreEqual(german, tokenizer.EncodeToIds(string.Format(template,
            "A German street sign that reads 'Bahnhofstraße', 1999 trams, Übergröße — Größe 10.000!")).ToArray());
    }
}

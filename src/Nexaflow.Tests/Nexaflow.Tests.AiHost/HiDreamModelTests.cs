using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Drawing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The host drawing with HiDream-O1's real graph. <c>NEXAFLOW_TEST_HIDREAM</c> names a folder holding the built model as
/// its catalog lays it out (<c>denoiser/</c>); <c>NEXAFLOW_TEST_RUNTIME_PACK</c> the CUDA pack, without which nothing is
/// drawn — the model is built for the GPU. What is asserted is the shape of a drawing — its steps, its size, that it is a
/// picture — not what it shows.
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
}

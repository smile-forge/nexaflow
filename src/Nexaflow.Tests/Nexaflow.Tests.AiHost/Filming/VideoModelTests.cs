using System.Text.Json.Nodes;
using Microsoft.ML.Tokenizers;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Filming;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost.Filming;

/// <summary>
/// The host filming with Wan2.2-TI2V's real graphs. <c>NEXAFLOW_TEST_VIDEO</c> names a folder holding the built model as
/// its catalog lays it out (<c>prompt-encoder/</c>, <c>denoiser/</c>, <c>denoiser-fast/</c>, <c>decoder/</c>,
/// <c>frame-encoder/</c>); <c>NEXAFLOW_TEST_RUNTIME_PACK</c> the CUDA pack, without which nothing is filmed — the model
/// is built for the GPU. What is asserted is the shape of a clip — its steps, its decoding, its size, length and codec —
/// not what it shows; the clips are attached to the run for a person to watch.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("owned-video")]
public class VideoModelTests
{
    private const int Width = 1280, Height = 704, Rate = 24;

    private static AiHostClient? _client;
    private static string? _root;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static void Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_VIDEO") is not { Length: > 0 } root) return;
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_RUNTIME_PACK") is not { Length: > 0 } pack) return;

        var aiDir = TestTemp.CreateSubdirectory("nexaflow-aihost-").FullName;
        _client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"), aiDir))
        {
            Environment = new AiHostEnvironment(pack),
        };
        _root = root;
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    /// <summary>The model's parts as the catalog places them, with FastWan's denoiser or the full model's.</summary>
    private static FilmingModel Model(bool distilled)
    {
        if (_client is null || _root is null) Assert.Inconclusive("NEXAFLOW_TEST_VIDEO and NEXAFLOW_TEST_RUNTIME_PACK are not both set.");
        string id = distilled ? "fastwan2.2-ti2v-5b" : "wan2.2-ti2v-5b";
        var parts = new Dictionary<string, ModelSpec>
        {
            [FilmingParts.PromptEncoder] = new($"{id}/promptEncoder", "wan-prompt-encoder", Path.Combine(_root, "prompt-encoder"), "prompt-encoder.onnx", null, 8L << 30, 0, Device: 0),
            [FilmingParts.Denoiser]      = new($"{id}/denoiser", "wan-denoiser", Path.Combine(_root, distilled ? "denoiser-fast" : "denoiser"), "denoiser.onnx", null, 14L << 30, 0, Device: 0),
            [FilmingParts.Decoder]       = new($"{id}/decoder", "wan-decoder", Path.Combine(_root, "decoder"), "decoder.onnx", null, 14L << 30, 0, Device: 0),
        };
        if (!distilled)
            parts[FilmingParts.FrameEncoder] = new($"{id}/frameEncoder", "wan-frame-encoder", Path.Combine(_root, "frame-encoder"), "frame-encoder.onnx", null, 3L << 30, 0, Device: 0);
        return distilled
            ? new FilmingModel(FilmingMethod.WanDistilled, parts, 3, 8f, 0f, [1000, 757, 522], "")
            : new FilmingModel(FilmingMethod.Wan, parts, 50, 5f, 5f, [], "色调艳丽，过曝，静态，细节模糊不清");
    }

    private async Task<(List<HostReply> Replies, Mp4Reader.Clip Clip)> FilmAsync(FilmingModel model, string prompt, int frames, byte[]? firstFrame,
                                                                                 string saveAs, CancellationToken ct = default)
    {
        var replies = new List<HostReply>();
        await foreach (var reply in _client!.SendAsync(id => new Film(id, model, prompt, Width, Height, frames, Rate, 7, RequestPriority.Job) { FirstFrame = firstFrame },
                                                       TimeSpan.FromMinutes(5), ct))
            replies.Add(reply);
        var mp4 = replies.OfType<Filmed>().Single().Mp4;
        var saved = Path.Combine(TestContext.TestRunResultsDirectory!, saveAs);
        File.WriteAllBytes(saved, mp4);
        TestContext.AddResultFile(saved);   // to watch: the test checks its shape, a person checks what it shows
        return (replies, Mp4Reader.Read(mp4));
    }

    private static long[] Done(IEnumerable<HostReply> replies, string stage)
        => [.. replies.OfType<Progress>().Where(p => p.Stage == stage && p.Done > 0).Select(p => p.Done)];

    [TestMethod]
    [Timeout(900_000, CooperativeCancellation = true)]
    public async Task FastWan_MakesAClipInItsThreeSteps_DecodedFrameByFrame_AsAnH264Mp4()
    {
        var model = Model(distilled: true);

        var (replies, clip) = await FilmAsync(model, "A red fox trots through fresh snow in a birch forest at dawn; the camera tracks alongside.",
                                             121, null, "fastwan-fox.mp4");

        CollectionAssert.AreEqual(new long[] { 1, 2, 3 }, Done(replies, FilmingStages.Filming), "each of its three steps is reported");
        CollectionAssert.AreEqual(Enumerable.Range(1, 31).Select(i => (long)i).ToArray(), Done(replies, FilmingStages.Decoding),
                                  "each of its 31 latent frames is reported as it is decoded");
        Assert.AreEqual(("avc1", Width, Height, 121), (clip.Codec, clip.Width, clip.Height, clip.Frames));
        Assert.AreEqual(121.0 / Rate, clip.Seconds, 1.0 / Rate, "five seconds at 24 frames a second");
    }

    [TestMethod]
    [Timeout(900_000, CooperativeCancellation = true)]
    public async Task TheFullModel_StartsAClipFromAPictureGiven_AndFastWanTakesNone()
    {
        var picture = Sunset();

        var (replies, clip) = await FilmAsync(Model(distilled: false), "The sun sets slowly over the sea as small waves roll in.", 25, picture,
                                             "wan-sunset-from-picture.mp4");

        Assert.AreEqual(50, Done(replies, FilmingStages.Filming).Length);
        Assert.AreEqual((Width, Height, 25), (clip.Width, clip.Height, clip.Frames), "a second, the picture its first frame");

        var ex = await Assert.ThrowsAsync<AiHostException>(async () =>
        {
            await foreach (var _ in _client!.SendAsync(id => new Film(id, Model(distilled: true), "The sun sets.", Width, Height, 25, Rate, 1, RequestPriority.Job)
                                                             { FirstFrame = picture }, TimeSpan.FromMinutes(1))) { }
        });
        StringAssert.Contains(ex.Message, "description alone", "FastWan, distilled from text, would cut away from the picture");
    }

    /// <summary>A sea at sunset, drawn: something to start a clip from.</summary>
    private static byte[] Sunset()
    {
        var picture = new byte[Width * Height * 3];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = (y * Width + x) * 3;
                bool sun = (x - 900) * (x - 900) + (y - 250) * (y - 250) < 90 * 90;
                (picture[i], picture[i + 1], picture[i + 2]) = sun ? ((byte)250, (byte)200, (byte)60)
                    : y > 450 ? ((byte)30, (byte)(70 + y / 8), (byte)120) : ((byte)(120 + y / 6), (byte)140, (byte)(200 - y / 6));
            }
        return picture;
    }

    [TestMethod]
    [Timeout(900_000, CooperativeCancellation = true)]
    public async Task TheFullModel_TakesItsFiftyGuidedSteps()
    {
        var model = Model(distilled: false);

        var (replies, clip) = await FilmAsync(model, "A paper boat drifting down a rain-filled gutter, close-up.", 25, null, "wan-boat.mp4");

        CollectionAssert.AreEqual(Enumerable.Range(1, 50).Select(i => (long)i).ToArray(), Done(replies, FilmingStages.Filming));
        Assert.AreEqual((Width, Height, 25), (clip.Width, clip.Height, clip.Frames));
    }

    [TestMethod]
    [Timeout(600_000, CooperativeCancellation = true)]
    public async Task AClip_StopsBetweenSteps_WhenCancelled()
    {
        var model = Model(distilled: false);
        using var cancel = new CancellationTokenSource();

        var ex = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var reply in _client!.SendAsync(id => new Film(id, model, "A city at night.", Width, Height, 25, Rate, 5, RequestPriority.Job),
                                                           TimeSpan.FromMinutes(5), cancel.Token))
                if (reply is Progress { Stage: FilmingStages.Filming, Done: 2 }) cancel.Cancel();
        });
        Assert.IsNotNull(ex);
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task AClipTheModelDoesNotMake_IsRefused()
    {
        var model = Model(distilled: true);

        var ex = await Assert.ThrowsAsync<AiHostException>(async () =>
        {
            await foreach (var _ in _client!.SendAsync(id => new Film(id, model, "A cat.", Width, Height, 120, Rate, 1, RequestPriority.Job), TimeSpan.FromMinutes(1))) { }
        });
        StringAssert.Contains(ex.Message, "120 frames");
    }

    [TestMethod]
    public void APrompt_IsTokenisedAsUmt5ReadsIt()
    {
        string root = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_VIDEO") ?? "";
        if (root.Length == 0) Assert.Inconclusive("NEXAFLOW_TEST_VIDEO is not set.");
        using var stream = File.OpenRead(Path.Combine(root, "prompt-encoder", "spiece.model"));
        var tokenizer = SentencePieceTokenizer.Create(stream, addBeginningOfSentence: false, addEndOfSentence: true);
        using var reference = typeof(VideoModelTests).Assembly.GetManifestResourceStream("wan-reference.json")!;

        foreach (var entry in JsonNode.Parse(reference)!["tokens"]!.AsArray())
        {
            string text = entry!["text"]!.GetValue<string>();
            int[] ids = [.. entry["ids"]!.AsArray().Select(i => i!.GetValue<int>())];
            CollectionAssert.AreEqual(ids, tokenizer.EncodeToIds(WanPromptEncoder.Clean(text), addBeginningOfSentence: false, addEndOfSentence: true).ToArray(), text);
        }
    }
}

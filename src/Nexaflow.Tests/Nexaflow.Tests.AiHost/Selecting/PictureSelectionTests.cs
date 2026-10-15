using System.Text.Json.Nodes;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost.Selecting;

/// <summary>
/// Picking things out of pictures through the real AI host, held to SAM 3.1's own still-picture paths: each click case's
/// mask, and each phrase's finds — how many, how sure, and their masks — against what the makers' paths made of the same
/// picture in fp32 (tools/ai-models/sam3.1/sam_reference.py), one session a picture, each answer from the one reading.
/// <para>
/// <c>NEXAFLOW_TEST_SAM</c> names the published graphs' folder, <c>NEXAFLOW_TEST_SAM_REFERENCE</c> the reference set and
/// <c>NEXAFLOW_TEST_RUNTIME_PACK</c> the CUDA pack; without them every test is inconclusive.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("picture-selection")]
public class PictureSelectionTests
{
    /// <summary>How alike a mask must be to the makers': the fp16 graphs move an edge by a pixel here and there.</summary>
    private const double Overlap = 0.95;

    private static AiHostClient? _client;
    private static string? _graphs, _reference;
    private static string _work = "";

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static void Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_SAM") is not { Length: > 0 } graphs) return;
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_SAM_REFERENCE") is not { Length: > 0 } reference) return;
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_RUNTIME_PACK") is not { Length: > 0 } pack) return;

        var aiDir = Directory.CreateTempSubdirectory("nexaflow-aihost-").FullName;
        _client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"), aiDir))
        {
            Environment = new AiHostEnvironment(pack),
        };
        (_graphs, _reference) = (graphs, reference);
        _work = Directory.CreateTempSubdirectory("nexaflow-selecting-").FullName;
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
        try { if (_work.Length > 0) Directory.Delete(_work, recursive: true); } catch (IOException) { }
    }

    private static IReadOnlyDictionary<string, ModelSpec> Parts()
    {
        if (_client is null || _graphs is null)
            Assert.Inconclusive("NEXAFLOW_TEST_SAM, NEXAFLOW_TEST_SAM_REFERENCE and NEXAFLOW_TEST_RUNTIME_PACK are not all set.");
        ModelSpec Part(string role, string runner, string folder, long mib)
            => new($"picture-selection/{role}", runner, Path.Combine(_graphs!, folder), $"{folder}.onnx", null, mib << 20, 0, Device: 0);
        // The published folder keeps the vocabulary apart; the catalog lays it beside the text encoder, where the runner
        // reads it — here a folder of the test's own holds it, the graph named by its full path.
        string text = Path.Combine(_work, "text-encoder");
        Directory.CreateDirectory(text);
        string vocabulary = Path.Combine(text, "bpe_simple_vocab_16e6.txt.gz");
        if (!File.Exists(vocabulary)) File.Copy(Path.Combine(_graphs!, "tokenizer", "bpe_simple_vocab_16e6.txt.gz"), vocabulary);
        return new Dictionary<string, ModelSpec>
        {
            [SelectingParts.ImageEncoder]    = Part(SelectingParts.ImageEncoder, "sam-image-encoder", "image-encoder", 2560),
            [SelectingParts.ClickDecoder]    = Part(SelectingParts.ClickDecoder, "sam-click-decoder", "click-decoder", 256),
            [SelectingParts.TextEncoder]     = Part(SelectingParts.TextEncoder, "sam-text-encoder", "text-encoder", 1024) with
                                               {
                                                   Folder = text,
                                                   File = Path.Combine(_graphs!, "text-encoder", "text-encoder.onnx"),
                                               },
            [SelectingParts.ExemplarEncoder] = Part(SelectingParts.ExemplarEncoder, "sam-exemplar-encoder", "exemplar-encoder", 128),
            [SelectingParts.Finder]          = Part(SelectingParts.Finder, "sam-finder", "finder", 1664),
        };
    }

    private static JsonObject Pictures()
    {
        _ = Parts();
        return JsonNode.Parse(File.ReadAllText(Path.Combine(_reference!, "reference.json")))!["pictures"]!.AsObject();
    }

    private static double Iou(byte[] a, byte[] b)
    {
        Assert.AreEqual(a.Length, b.Length, "the masks are not one size");
        long both = 0, either = 0;
        for (int i = 0; i < a.Length; i++)
        {
            bool x = a[i] != 0, y = b[i] != 0;
            if (x && y) both++;
            if (x || y) either++;
        }
        return either == 0 ? 1 : (double)both / either;
    }

    /// <summary>A session on one picture: asks written to it, each answer read back by its turn.</summary>
    private sealed class Session(IHostStream stream) : IAsyncDisposable
    {
        private readonly IAsyncEnumerator<HostReply> _replies = stream.ReadAsync().GetAsyncEnumerator();

        public long Id => stream.RequestId;

        public async Task<T> NextAsync<T>() where T : HostReply
        {
            while (await _replies.MoveNextAsync())
                if (_replies.Current is T wanted) return wanted;
            throw new AssertFailedException($"The session ended before its {typeof(T).Name}.");
        }

        public async Task<T> AskAsync<T>(RequestInput ask) where T : HostReply
        {
            await stream.WriteAsync(ask);
            return await NextAsync<T>();
        }

        public async Task EndAsync()
        {
            await stream.WriteAsync(new InputEnd(stream.RequestId));
            while (await _replies.MoveNextAsync()) { }
        }

        public async ValueTask DisposeAsync()
        {
            await _replies.DisposeAsync();
            await stream.DisposeAsync();
        }
    }

    private async Task<Session> OpenAsync(JsonNode picture)
    {
        var stream = await _client!.OpenStreamAsync(id => new SelectInPicture(id, Parts(), Path.Combine(_reference!, picture["pixels"]!.GetValue<string>()),
                                                                              picture["width"]!.GetValue<int>(), picture["height"]!.GetValue<int>(),
                                                                              RequestPriority.Interactive),
                                                    TimeSpan.FromMinutes(5));
        var session = new Session(stream);
        await session.NextAsync<PictureEncoded>();
        return session;
    }

    [TestMethod]
    public async Task EachClickCase_PicksWhatTheMakersPick_FromOneReading()
    {
        var ious = new List<double>();
        foreach (var (name, picture) in Pictures())
        {
            int width = picture!["width"]!.GetValue<int>(), height = picture["height"]!.GetValue<int>();
            await using var session = await OpenAsync(picture);
            int turn = 0;
            foreach (var c in picture["clicks"]!.AsArray())
            {
                var points = c!["points"]!.AsArray().Zip(c["labels"]!.AsArray(), (p, l) => new SelectionPoint(
                    p![0]!.GetValue<float>(), p[1]!.GetValue<float>(),
                    l!.GetValue<int>() switch { 1 => SelectionPointKind.Include, 0 => SelectionPointKind.Exclude, 2 => SelectionPointKind.BoxTopLeft, _ => SelectionPointKind.BoxBottomRight })).ToList();
                var into = Path.Combine(_work, $"{name}-{c["case"]}.mask");
                var selected = await session.AskAsync<PointsSelected>(new SelectByPoints(session.Id, ++turn, points, new MaskTarget(into, width, height)));

                Assert.AreEqual(turn, selected.Turn);
                double iou = Iou(File.ReadAllBytes(Path.Combine(_reference!, c["mask"]!.GetValue<string>())), File.ReadAllBytes(into));
                ious.Add(iou);
                TestContext.WriteLine($"{name} {c["case"]}: IoU {iou:F4} with the makers' mask");
                Assert.IsTrue(iou >= Overlap, $"{name} {c["case"]}: IoU {iou:F4}");
                Assert.IsNotNull(selected.Bounds, $"{name} {c["case"]}: something is picked");
            }
            await session.EndAsync();
        }
        Assert.IsTrue(ious.Average() >= 0.99, $"mean IoU {ious.Average():F4}");
    }

    [TestMethod]
    public async Task EachPhrase_FindsWhatTheMakersFind_AsSurely_WithTheirMasks()
    {
        var ious = new List<double>();
        foreach (var (name, picture) in Pictures())
        {
            int width = picture!["width"]!.GetValue<int>(), height = picture["height"]!.GetValue<int>();
            await using var session = await OpenAsync(picture);
            int turn = 0;
            foreach (var f in picture["finds"]!.AsArray())
            {
                // the reference's example boxes are the makers' centre, width and height from 0 to 1; a request's are pixels
                var examples = f!["boxes"]!.AsArray().Zip(f["labels"]!.AsArray(), (b, l) =>
                {
                    float cx = b![0]!.GetValue<float>() * width, cy = b[1]!.GetValue<float>() * height;
                    float w = b[2]!.GetValue<float>() * width, h = b[3]!.GetValue<float>() * height;
                    return new SelectionExample(new SelectionBox(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2), l!.GetValue<int>() == 1);
                }).ToList();
                var into = Path.Combine(_work, $"{name}-{f["case"]}.masks");
                var found = await session.AskAsync<PhraseFound>(new SelectByPhrase(session.Id, ++turn, f["phrase"]!.GetValue<string>(), examples,
                                                                                    new MaskTarget(into, width, height)));

                float[] theirs = [.. f["scores"]!.AsArray().Select(s => s!.GetValue<float>()).OrderByDescending(s => s)];
                Assert.AreEqual(theirs.Length, found.Found.Count, $"{name} '{f["phrase"]}': how many are found");
                for (int i = 0; i < theirs.Length; i++)
                    Assert.AreEqual(theirs[i], found.Found[i].Score, 0.01f, $"{name} '{f["phrase"]}': how sure");

                var all = File.ReadAllBytes(into);
                int size = width * height;
                var ours = Enumerable.Range(0, found.Found.Count).Select(i => all.AsSpan(i * size, size).ToArray()).ToList();
                foreach (var file in f["masks"]!.AsArray())
                {
                    var mask = File.ReadAllBytes(Path.Combine(_reference!, file!.GetValue<string>()));
                    double best = ours.Max(o => Iou(mask, o));
                    ious.Add(best);
                    Assert.IsTrue(best >= Overlap, $"{name} '{f["phrase"]}' {file}: IoU {best:F4}");
                }
                TestContext.WriteLine($"{name} '{f["phrase"]}' + {examples.Count} example(s): {found.Found.Count} found");
            }
            await session.EndAsync();
        }
        Assert.IsTrue(ious.Average() >= 0.99, $"mean IoU {ious.Average():F4}");
    }
}

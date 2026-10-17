using System.Text.Json.Nodes;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost.Restoring;

/// <summary>
/// The picture tools' restoration through the real AI host, held to upstream Real-ESRGAN and GFPGAN: each reference
/// picture at each scale, faces repaired or not, against what the Python pipelines made from it — in fp32, GFPGAN with
/// the noise it was trained with, the upscaler tiled as the host tiles it (tools/ai-models/real-esrgan-gfpgan makes them).
/// <para>
/// <c>NEXAFLOW_TEST_PICTURE_TOOLS</c> names the five graphs' folder, <c>NEXAFLOW_TEST_PICTURE_REFERENCE</c> the reference
/// set and <c>NEXAFLOW_TEST_RUNTIME_PACK</c> the CUDA pack; without them every test is inconclusive.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("picture-tools")]
public class PictureRestorationTests
{
    /// <summary>How close, in dB, a restored picture is to the Python pipeline's: past 50, the two differ only where a value
    /// rounds the other way.</summary>
    private const double Agreement = 50;

    private static AiHostClient? _client;
    private static string? _graphs, _reference;
    private static string _work = "";

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static void Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_PICTURE_TOOLS") is not { Length: > 0 } graphs) return;
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_PICTURE_REFERENCE") is not { Length: > 0 } reference) return;
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_RUNTIME_PACK") is not { Length: > 0 } pack) return;

        var aiDir = Directory.CreateTempSubdirectory("nexaflow-aihost-").FullName;
        _client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"), aiDir))
        {
            Environment = new AiHostEnvironment(pack),
        };
        (_graphs, _reference) = (graphs, reference);
        _work = Directory.CreateTempSubdirectory("nexaflow-restoring-").FullName;
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
            Assert.Inconclusive("NEXAFLOW_TEST_PICTURE_TOOLS, NEXAFLOW_TEST_PICTURE_REFERENCE and NEXAFLOW_TEST_RUNTIME_PACK are not all set.");
        ModelSpec Part(string role, string runner, string folder, long gib)
            => new($"picture-tools/{role}", runner, Path.Combine(_graphs!, folder), $"{folder}.onnx", null, gib << 30, 0, Device: 0);
        return new Dictionary<string, ModelSpec>
        {
            [RestoringParts.UpscalerX2]   = Part(RestoringParts.UpscalerX2, "esrgan-upscaler", "upscaler-x2", 2),
            [RestoringParts.UpscalerX4]   = Part(RestoringParts.UpscalerX4, "esrgan-upscaler", "upscaler-x4", 3),
            [RestoringParts.FaceDetector] = Part(RestoringParts.FaceDetector, "retinaface-detector", "face-detector", 2),
            [RestoringParts.FaceRestorer] = Part(RestoringParts.FaceRestorer, "gfpgan-restorer", "face-restorer", 2),
            [RestoringParts.FaceParser]   = Part(RestoringParts.FaceParser, "face-parser", "face-parser", 1),
        };
    }

    private static IEnumerable<JsonNode> Cases()
    {
        _ = Parts();   // inconclusive without the graphs
        return JsonNode.Parse(File.ReadAllText(Path.Combine(_reference!, "reference.json")))!["cases"]!.AsArray()!.Select(c => c!);
    }

    private async Task<(List<HostReply> Replies, byte[] Pixels)> RestoreAsync(string picture, int width, int height, int scale, bool faces,
                                                                             CancellationToken ct = default)
    {
        var into = Path.Combine(_work, $"{Path.GetFileNameWithoutExtension(picture)}-x{scale}{(faces ? "-faces" : "")}.rgba");
        var replies = new List<HostReply>();
        await foreach (var reply in _client!.SendAsync(id => new RestorePicture(id, Parts(), Path.Combine(_reference!, picture), width, height,
                                                                                scale, faces, into, RequestPriority.Job),
                                                       TimeSpan.FromMinutes(5), ct))
            replies.Add(reply);
        return (replies, File.ReadAllBytes(into));
    }

    /// <summary>Peak signal to noise, in dB, over every byte of two pictures of one size.</summary>
    private static double Psnr(byte[] a, byte[] b)
    {
        Assert.AreEqual(a.Length, b.Length, "the pictures are not one size");
        double sum = 0;
        for (int i = 0; i < a.Length; i++) sum += (a[i] - b[i]) * (a[i] - b[i]);
        return sum == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 * a.Length / sum);
    }

    [TestMethod]
    public async Task EachPicture_IsUpscaledAsRealEsrganUpscalesIt_TileByTile()
    {
        foreach (var c in Cases())
        {
            var (picture, scale) = (c["picture"]!.GetValue<string>(), c["scale"]!.GetValue<int>());
            int width = c["size"]![0]!.GetValue<int>(), height = c["size"]![1]!.GetValue<int>();
            var (replies, pixels) = await RestoreAsync(picture, width, height, scale, faces: false);

            double psnr = Psnr(File.ReadAllBytes(Path.Combine(_reference!, c["plain"]!.GetValue<string>())), pixels);
            TestContext.WriteLine($"{picture} x{scale}: {psnr:F1} dB from Real-ESRGAN's");
            var done = replies.OfType<PictureRestored>().Single();
            Assert.AreEqual((width * scale, height * scale, 0), (done.Width, done.Height, done.FacesRestored));
            Assert.IsTrue(replies.OfType<Progress>().Any(p => p.Stage == RestoringStages.Upscaling && p.Done == p.Total && p.Total > 0),
                          "every tile is reported");
            // The same picture but for a rounding here and there: 55-66 dB, the lowest where a picture kept at its size is brought
            // back with Lanczos, which OpenCV works in fixed point.
            Assert.IsTrue(psnr >= Agreement, $"{picture} x{scale} is {psnr:F1} dB from Real-ESRGAN's");
        }
    }

    [TestMethod]
    public async Task EachFace_IsFoundRemadeAndPastedBack_AsGfpganDoes()
    {
        foreach (var c in Cases())
        {
            var (picture, scale) = (c["picture"]!.GetValue<string>(), c["scale"]!.GetValue<int>());
            int width = c["size"]![0]!.GetValue<int>(), height = c["size"]![1]!.GetValue<int>();
            var (replies, pixels) = await RestoreAsync(picture, width, height, scale, faces: true);

            double psnr = Psnr(File.ReadAllBytes(Path.Combine(_reference!, c["faces"]!.GetValue<string>())), pixels);
            int found = c["found"]!.AsArray().Count;
            var done = replies.OfType<PictureRestored>().Single();
            TestContext.WriteLine($"{picture} x{scale} with faces: {done.FacesRestored} of {found} faces, {psnr:F1} dB from GFPGAN's");
            Assert.AreEqual(found, done.FacesRestored, $"{picture}: the faces GFPGAN's helper found");
            Assert.IsTrue(psnr >= Agreement, $"{picture} x{scale} with faces is {psnr:F1} dB from GFPGAN's");   // 54-66 dB
        }
    }
}

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
/// The five graphs are <c>real-esrgan-gfpgan</c>'s from the test store (<see cref="TestStore"/>), run on the CUDA pack there;
/// <c>NEXAFLOW_TEST_PICTURE_REFERENCE</c> names the reference set. Without both every test is inconclusive.
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
    private static string? _reference;
    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_PICTURE_REFERENCE") is not { Length: > 0 } reference) return;
        _client = await TestStore.HostAsync();
        _reference = reference;
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    /// <summary>The restoring tool, as the catalog names it: the host runs its five graphs where it places them.</summary>
    private static string Tool()
    {
        if (_client is null || _reference is null)
            Assert.Inconclusive($"{TestStore.Missing()} Or NEXAFLOW_TEST_PICTURE_REFERENCE is not set.");
        return "real-esrgan-gfpgan";
    }

    private static IEnumerable<JsonNode> Cases()
    {
        _ = Tool();   // inconclusive without the graphs
        return JsonNode.Parse(File.ReadAllText(Path.Combine(_reference!, "reference.json")))!["cases"]!.AsArray()!.Select(c => c!);
    }

    private async Task<(List<HostReply> Replies, byte[] Pixels)> RestoreAsync(string picture, int width, int height, int scale, bool faces,
                                                                             CancellationToken ct = default)
    {
        using var pixels = await _client!.ShareFileAsync(Path.Combine(_reference!, picture), ct);
        var replies = new List<HostReply>();
        await foreach (var reply in _client.SendAsync(id => new RestorePicture(id, Tool(), pixels, width, height, scale, faces, RequestPriority.Job),
                                                      TimeSpan.FromMinutes(5), ct))
            replies.Add(reply);
        using var restored = replies.OfType<PictureRestored>().Single().Pixels;
        return (replies, restored.ToArray());
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

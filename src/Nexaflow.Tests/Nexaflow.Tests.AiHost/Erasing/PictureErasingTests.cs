using System.Text.Json.Nodes;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost.Erasing;

/// <summary>
/// Erasing through the real AI host, held to LaMa's own predict: each reference picture with its drawn mask against what
/// predict made of it, in fp32 (tools/ai-models/big-lama makes them). Each mask reaches near every corner, so the host
/// works the whole picture as one region, as predict does.
/// <para>
/// The graph is big-lama's from the test store (<see cref="TestStore"/>), run on the CUDA pack there;
/// <c>NEXAFLOW_TEST_LAMA_REFERENCE</c> names the reference set. Without both every test is inconclusive.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("picture-erasing")]
public class PictureErasingTests
{
    /// <summary>How close, in dB, the filled pixels are to predict's: past 50, the two differ only where a value truncates the
    /// other way.</summary>
    private const double Agreement = 50;

    private static AiHostClient? _client;
    private static string? _reference;
    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_LAMA_REFERENCE") is not { Length: > 0 } reference) return;
        _client = await TestStore.HostAsync();
        _reference = reference;
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private static IReadOnlyDictionary<string, ModelSpec> Parts(int? device)
    {
        if (_client is null || _reference is null)
            Assert.Inconclusive($"{TestStore.Missing()} Or NEXAFLOW_TEST_LAMA_REFERENCE is not set.");
        return new Dictionary<string, ModelSpec>
        {
            [ErasingParts.Inpainter] = TestStore.Spec("big-lama", ErasingParts.Inpainter, device, 4L << 30),
        };
    }

    private static IEnumerable<(string Name, int Width, int Height)> Cases()
    {
        _ = Parts(0);   // inconclusive without the graph
        return JsonNode.Parse(File.ReadAllText(Path.Combine(_reference!, "reference.json")))!.AsObject()
            .Select(c => (c.Key, c.Value!["width"]!.GetValue<int>(), c.Value!["height"]!.GetValue<int>()));
    }

    private async Task<(List<HostReply> Replies, byte[] Pixels)> EraseAsync(string name, int width, int height, int? device)
    {
        using var pixels = await _client!.ShareFileAsync(Path.Combine(_reference!, $"{name}.rgba"));
        using var mask = await _client.ShareFileAsync(Path.Combine(_reference!, $"{name}.mask"));
        var replies = new List<HostReply>();
        await foreach (var reply in _client.SendAsync(id => new ErasePicture(id, Parts(device), pixels, mask, width, height, RequestPriority.Job),
                                                      TimeSpan.FromMinutes(5)))
            replies.Add(reply);
        using var erased = replies.OfType<PictureErased>().Single().Pixels;
        return (replies, erased.ToArray());
    }

    /// <summary>Peak signal to noise, in dB, over the colour of the pixels <paramref name="mask"/> fills.</summary>
    private static double PsnrInMask(byte[] a, byte[] b, byte[] mask)
    {
        double sum = 0;
        long values = 0;
        for (int p = 0; p < mask.Length; p++)
        {
            if (mask[p] == 0) continue;
            for (int c = 0; c < 3; c++) sum += (a[p * 4 + c] - b[p * 4 + c]) * (a[p * 4 + c] - b[p * 4 + c]);
            values += 3;
        }
        return sum == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 * values / sum);
    }

    private void AssertErasedAsPredictErases(string name, byte[] pixels, string where)
    {
        var picture = File.ReadAllBytes(Path.Combine(_reference!, $"{name}.rgba"));
        var mask = File.ReadAllBytes(Path.Combine(_reference!, $"{name}.mask"));
        var predicted = File.ReadAllBytes(Path.Combine(_reference!, $"{name}.erased.rgba"));
        Assert.AreEqual(picture.Length, pixels.Length, "the erased picture is not the picture's size");

        int changedOutside = 0;
        for (int p = 0; p < mask.Length; p++)
            if (mask[p] == 0 && (pixels[p * 4] != picture[p * 4] || pixels[p * 4 + 1] != picture[p * 4 + 1] || pixels[p * 4 + 2] != picture[p * 4 + 2]
                                 || pixels[p * 4 + 3] != picture[p * 4 + 3]))
                changedOutside++;
        double psnr = PsnrInMask(pixels, predicted, mask);
        TestContext.WriteLine($"{name} on {where}: {psnr:F1} dB from predict's inside the mask");
        Assert.AreEqual(0, changedOutside, $"{name}: pixels outside the mask changed");
        Assert.IsTrue(psnr >= Agreement, $"{name} is {psnr:F1} dB from predict's inside the mask");
    }

    [TestMethod]
    public async Task EachPicture_IsFilledAsLamasPredictFillsIt_OnTheGpu()
    {
        foreach (var (name, width, height) in Cases())
        {
            var (replies, pixels) = await EraseAsync(name, width, height, device: 0);
            Assert.AreEqual(1, replies.OfType<PictureErased>().Single().Regions, $"{name}: a mask reaching every corner is one region");
            Assert.IsTrue(replies.OfType<Progress>().Any(p => p.Stage == ErasingStages.Filling && p.Done == 1 && p.Total == 1), "the region is reported");
            AssertErasedAsPredictErases(name, pixels, "the GPU");
        }
    }

    [TestMethod]
    public async Task EachPicture_IsFilledTheSame_OnTheCpu()
    {
        var (name, width, height) = Cases().First();
        var (_, pixels) = await EraseAsync(name, width, height, device: null);
        AssertErasedAsPredictErases(name, pixels, "the CPU");
    }
}

using System.Text.Json.Nodes;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Drawing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.AiHost.Catalog;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The host editing with Qwen-Image-Edit's real graphs, <c>qwen-image-edit-2511</c> from the test store
/// (<see cref="TestStore"/>), on the CUDA pack there, without which nothing is edited — the model is built for the GPU.
/// What is asserted is the shape of an edit — its steps, its size, that it is a picture,
/// and under a mask that what the mask leaves is kept — not what it shows.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("owned-images")]
public class QwenEditModelTests
{
    private const int Steps = 40;

    /// <summary>The model, as the catalog names it: the host edits with its parts where it places them.</summary>
    private const string Model = "qwen-image-edit-2511";

    private static AiHostClient? _client;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
    _client = await TestStore.HostAsync();
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private static (AiHostClient Client, string Model) Host()
    {
        if (_client is null) Assert.Inconclusive(TestStore.Missing());
        return (_client, Model);
    }

    /// <summary>A picture with something in it: a sunlit gradient, a dark square in the middle, a pattern along the bottom.</summary>
    private static byte[] Scene(int width, int height)
    {
        var rgb = new byte[width * height * 3];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int at = (y * width + x) * 3;
                bool square = x > width / 3 && x < 2 * width / 3 && y > height / 3 && y < 2 * height / 3;
                bool pattern = y > height * 5 / 6 && (x / 32 + y / 32) % 2 == 0;
                rgb[at] = (byte)(square ? 60 : pattern ? 200 : 120 + 120 * x / width);
                rgb[at + 1] = (byte)(square ? 40 : pattern ? 60 : 160 + 80 * y / height);
                rgb[at + 2] = (byte)(square ? 30 : pattern ? 40 : 220);
            }
        return rgb;
    }

    private async Task<(List<HostReply> Replies, PngReader.Picture Picture)> EditAsync(Func<long, Draw> draw, string name)
    {
        var (client, _) = Host();
        var replies = new List<HostReply>();
        await foreach (var reply in client.SendAsync(draw, TimeSpan.FromMinutes(10))) replies.Add(reply);
        var png = replies.OfType<Drawn>().Single().AsPng();
        Keep(png, name);
        return (replies, PngReader.Read(png));
    }

    /// <summary>Keeps a picture edited with the test's results, to look at: the test checks its shape, a person checks what it shows.</summary>
    private void Keep(byte[] png, string name)
    {
        var saved = Path.Combine(TestContext.TestRunResultsDirectory!, name);
        File.WriteAllBytes(saved, png);
        TestContext.AddResultFile(saved);
    }

    [TestMethod]
    [Timeout(1_200_000, CooperativeCancellation = true)]
    public async Task APicture_IsChangedByAnInstruction_StepByStep_AtItsOwnSize()
    {
        var (_, model) = Host();
        const int width = 1184, height = 896;
        var source = Scene(width, height);

        var (replies, edited) = await EditAsync(id => new Draw(id, model, "Make it night, a full moon in the sky.", 0, 0, 4, RequestPriority.Job)
                                                     { Sources = [new DrawingSource(width, height, SharedBytes.Of(source))] },
                                                "edited-night-qwen.png");

        var steps = replies.OfType<Progress>().Where(p => p.Stage == DrawHandler.Drawing && p.Done > 0).Select(p => p.Done).ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(1, Steps).Select(i => (long)i).ToArray(), steps, "each denoising step is reported as it finishes");
        Assert.AreEqual((width, height), (edited.Width, edited.Height));
        Assert.IsTrue(edited.Rgb.Average(b => (double)b) < source.Average(b => (double)b) - 10, "night is darker than the day it was");
    }

    /// <summary>The same edit with the denoiser whole and streamed, where its folder holds the streamed build: one picture, to the
    /// rounding of fp16 arithmetic done in another order. Edited by the host's own drawing method in this process
    /// (<see cref="InProcessHost"/>), each denoiser placed by the test: a host's plan streams it only where its GPU lacks the room for it
    /// whole.</summary>
    [TestMethod]
    [Timeout(2_400_000, CooperativeCancellation = true)]
    [CoversNode("owned-images-streamed")]
    public async Task AStreamedDenoiser_EditsAsTheWholeOneEdits()
    {
        _ = Host();
        using var host = InProcessHost.OnTheGpu();
        var model = TestStore.DrawingModelOf(Model);
        var denoiser = model.Parts[DrawingParts.Denoiser];
        if (!File.Exists(TestStore.PathOf($"{denoiser.Folder}/denoiser.stream.json"))) Assert.Inconclusive("The denoiser's folder holds no streamed build.");
        var streamed = model with
        {
            Parts = new Dictionary<string, ModelSpec>(model.Parts) { [DrawingParts.Denoiser] = denoiser with { File = "denoiser.stream.json", MemoryBytes = 8L << 30 } },
        };
        const int width = 1184, height = 896;
        var source = Scene(width, height);
        var drawing = new QwenEditDrawing(host.Models, host.Scheduler);

        async Task<PngReader.Picture> EditInProcessAsync(DrawingModel edited, string name)
        {
            var picture = await host.DrawAsync(drawing, new Draw(1, Model, "Make it night, a full moon in the sky.", 0, 0, 4, RequestPriority.Job)
                                                        { Sources = [new DrawingSource(width, height, SharedBytes.Of(source))] }, edited);
            Keep(new Drawn(1, picture.Width, picture.Height, SharedBytes.Of(picture.Rgb)).AsPng(), name);
            return picture;
        }

        PngReader.Picture whole, stream;
        
        long streamedPeak, wholePeak;
        using (var watch = GpuMemoryWatch.Start()) { stream = await EditInProcessAsync(streamed, "night-qwen-streamed.png"); streamedPeak = watch.PeakBytes; }
        using (var watch = GpuMemoryWatch.Start()) { whole = await EditInProcessAsync(model, "night-qwen-whole.png"); wholePeak = watch.PeakBytes; }
        TestContext.WriteLine($"the GPU at its peak: streamed {streamedPeak / 1e9:0.00} GB, whole {wholePeak / 1e9:0.00} GB");
        Assert.IsTrue(streamedPeak < wholePeak, $"streamed, the GPU held {streamedPeak / 1e9:0.00} GB at its peak, whole {wholePeak / 1e9:0.00} GB");
        double mse = whole.Rgb.Zip(stream.Rgb).Average(p => (p.First - p.Second) * (double)(p.First - p.Second));
        double psnr = mse == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255 / mse);
        Assert.IsTrue(psnr >= 35, $"the streamed denoiser's picture is {psnr:0.0} dB from the whole one's");
    }

    [TestMethod]
    [Timeout(1_200_000, CooperativeCancellation = true)]
    public async Task UnderAMask_OnlyWhatItCovers_IsDrawnAgain()
    {
        var (_, model) = Host();
        const int width = 1024, height = 1024;
        var source = Scene(width, height);
        var mask = new byte[width * height];
        for (int y = 384; y < 640; y++)
            for (int x = 384; x < 640; x++)
                mask[y * width + x] = 255;

        var (_, filled) = await EditAsync(id => new Draw(id, model, "a bowl of oranges", 0, 0, 6, RequestPriority.Job)
                                                { Sources = [new DrawingSource(width, height, SharedBytes.Of(source))], Mask = SharedBytes.Of(mask) },
                                          "filled-oranges-qwen.png");

        // What the mask leaves comes back through the VAE: the same picture to within its rounding. What it covers is drawn again.
        double kept = Psnr(source, filled.Rgb, width, (x, y) => (x < 352 || x >= 672) && (y < 352 || y >= 672));
        double covered = Psnr(source, filled.Rgb, width, (x, y) => x is >= 416 and < 608 && y is >= 416 and < 608);
        Assert.IsTrue(kept > 28, $"what the mask leaves is kept ({kept:F1} dB)");
        Assert.IsTrue(covered < kept - 6, $"what the mask covers is drawn again ({covered:F1} dB against {kept:F1} dB)");
    }

    [TestMethod]
    [Timeout(120_000, CooperativeCancellation = true)]
    public async Task APromptAlone_IsRefused_ForAModelThatOnlyChangesPictures()
    {
        var (client, model) = Host();

        var ex = await Assert.ThrowsAsync<AiHostException>(async () =>
        {
            await foreach (var _ in client.SendAsync(id => new Draw(id, model, "A cat.", 1024, 1024, 1, RequestPriority.Job), TimeSpan.FromMinutes(1))) { }
        });
        StringAssert.Contains(ex.Message, "1024x1024");
    }

    [TestMethod]
    public void AnEditsPrompt_IsWrittenAsThePipelineWritesIt_EachPictureNumberedWithItsTokens()
    {
        var tokenizer = QwenTokenizer.FromJson(File.ReadAllText(Path.Combine(TestStore.PartFolder("qwen-image-edit-2511", DrawingParts.PromptEncoder), "tokenizer.json")));
        var encoder = new QwenEditPromptEncoder(null!, null!, tokenizer);
        using var stream = typeof(QwenEditModelTests).Assembly.GetManifestResourceStream("qwen-image-edit.json")!;
        var prompts = JsonNode.Parse(stream)!["prompts"]!;

        // The processor's tokens for the pipeline's template, two pictures and the text — and the empty prompt with them.
        foreach (var name in new[] { "prompt", "empty" })
        {
            var p = prompts[name]!;
            int[] counts = [.. p["grids"]!.AsArray().Select(g => g![0]!.GetValue<int>() * g[1]!.GetValue<int>())];
            long[] expected = [.. p["ids"]!.AsArray().Select(v => v!.GetValue<long>())];

            CollectionAssert.AreEqual(expected, encoder.PromptIds(p["text"]!.GetValue<string>(), counts), name);
        }
    }

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

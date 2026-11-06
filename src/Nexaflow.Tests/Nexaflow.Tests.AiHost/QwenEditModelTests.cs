using System.Text.Json.Nodes;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Drawing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The host editing with Qwen-Image-Edit's real graphs. <c>NEXAFLOW_TEST_QWEN_EDIT</c> names a folder holding the built
/// model as its catalog lays it out (<c>picture-reader/</c>, <c>prompt-encoder/</c>, <c>picture-encoder/</c>,
/// <c>denoiser/</c>, <c>decoder/</c>); <c>NEXAFLOW_TEST_RUNTIME_PACK</c> the CUDA pack, without which nothing is edited —
/// the model is built for the GPU. What is asserted is the shape of an edit — its steps, its size, that it is a picture,
/// and under a mask that what the mask leaves is kept — not what it shows.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("owned-images")]
public class QwenEditModelTests
{
    private const int Steps = 40;

    private static AiHostClient? _client;
    private static DrawingModel? _model;

    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_QWEN_EDIT") is not { Length: > 0 } root) return;
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_RUNTIME_PACK") is not { Length: > 0 } pack) return;

        var aiDir = TestTemp.CreateSubdirectory("nexaflow-aihost-").FullName;
        _client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"), aiDir))
        {
            Environment = new AiHostEnvironment(pack),
        };
        ModelSpec Part(string folder, string runner, long bytes) =>
            new($"qwen-image-edit-2511/{folder}", runner, Path.Combine(root, folder), $"{folder}.onnx", null, bytes, 0, Device: 0);
        _model = new DrawingModel(DrawingMethod.QwenImageEdit, new Dictionary<string, ModelSpec>
        {
            [DrawingParts.PictureReader]  = Part("picture-reader", "qwen-edit-picture-reader", 2L << 30),
            [DrawingParts.PromptEncoder]  = Part("prompt-encoder", "qwen-edit-prompt-encoder", 10L << 30),
            [DrawingParts.PictureEncoder] = Part("picture-encoder", "qwen-edit-picture-encoder", 4L << 30),
            [DrawingParts.Denoiser]       = Part("denoiser", "qwen-edit-denoiser", 24L << 30),
            [DrawingParts.Decoder]        = Part("decoder", "qwen-edit-decoder", 6L << 30),
        }, Steps, Shift: 0f, Guidance: 4f);
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    private static (AiHostClient Client, DrawingModel Model) Host()
    {
        if (_client is null || _model is null) Assert.Inconclusive("NEXAFLOW_TEST_QWEN_EDIT and NEXAFLOW_TEST_RUNTIME_PACK are not both set.");
        return (_client, _model);
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
        var png = replies.OfType<Drawn>().Single().Png;
        var saved = Path.Combine(TestContext.TestRunResultsDirectory!, name);
        File.WriteAllBytes(saved, png);
        TestContext.AddResultFile(saved);   // to look at: the test checks its shape, a person checks what it shows
        return (replies, PngReader.Read(png));
    }

    [TestMethod]
    [Timeout(1_200_000, CooperativeCancellation = true)]
    public async Task APicture_IsChangedByAnInstruction_StepByStep_AtItsOwnSize()
    {
        var (_, model) = Host();
        const int width = 1184, height = 896;
        var source = Scene(width, height);

        var (replies, edited) = await EditAsync(id => new Draw(id, model, "Make it night, a full moon in the sky.", width, height, 4, RequestPriority.Job)
                                                     { Sources = [new DrawingSource(width, height, source)] },
                                                "edited-night-qwen.png");

        var steps = replies.OfType<Progress>().Where(p => p.Stage == DrawHandler.Drawing && p.Done > 0).Select(p => p.Done).ToArray();
        CollectionAssert.AreEqual(Enumerable.Range(1, Steps).Select(i => (long)i).ToArray(), steps, "each denoising step is reported as it finishes");
        Assert.AreEqual((width, height), (edited.Width, edited.Height));
        Assert.IsTrue(edited.Rgb.Average(b => (double)b) < source.Average(b => (double)b) - 10, "night is darker than the day it was");
    }

    /// <summary>The same edit with the denoiser whole and streamed, where its folder holds the streamed build: one picture, to the
    /// rounding of fp16 arithmetic done in another order.</summary>
    [TestMethod]
    [Timeout(2_400_000, CooperativeCancellation = true)]
    [CoversNode("owned-images-streamed")]
    public async Task AStreamedDenoiser_EditsAsTheWholeOneEdits()
    {
        var (_, model) = Host();
        var denoiser = model.Parts[DrawingParts.Denoiser];
        if (!File.Exists(Path.Combine(denoiser.Folder, "denoiser.stream.json"))) Assert.Inconclusive("The denoiser's folder holds no streamed build.");
        var streamed = model with
        {
            Parts = new Dictionary<string, ModelSpec>(model.Parts) { [DrawingParts.Denoiser] = denoiser with { File = "denoiser.stream.json", MemoryBytes = 8L << 30 } },
        };
        const int width = 1184, height = 896;
        var source = Scene(width, height);
        Draw Edit(long id, DrawingModel drawing) => new(id, drawing, "Make it night, a full moon in the sky.", width, height, 4, RequestPriority.Job)
                                                    { Sources = [new DrawingSource(width, height, source)] };

        PngReader.Picture whole, stream;
        
        long streamedPeak, wholePeak;
        using (var watch = GpuMemoryWatch.Start()) { stream = (await EditAsync(id => Edit(id, streamed), "night-qwen-streamed.png")).Picture; streamedPeak = watch.PeakBytes; }
        using (var watch = GpuMemoryWatch.Start()) { whole = (await EditAsync(id => Edit(id, model), "night-qwen-whole.png")).Picture; wholePeak = watch.PeakBytes; }
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

        var (_, filled) = await EditAsync(id => new Draw(id, model, "a bowl of oranges", width, height, 6, RequestPriority.Job)
                                                { Sources = [new DrawingSource(width, height, source)], Mask = mask },
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
        string root = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_QWEN_EDIT") ?? "";
        if (root.Length == 0) Assert.Inconclusive("NEXAFLOW_TEST_QWEN_EDIT is not set.");
        var tokenizer = QwenTokenizer.FromJson(File.ReadAllText(Path.Combine(root, "prompt-encoder", "tokenizer.json")));
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

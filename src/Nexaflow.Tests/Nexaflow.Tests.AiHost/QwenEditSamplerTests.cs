using System.Text.Json.Nodes;
using Nexaflow.AiHost.Drawing;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The arithmetic around Qwen-Image-Edit's graphs, against what diffusers' pipeline and Qwen2-VL's processor do
/// (qwen-image-edit.json, from tools/ai-models/qwen-image-edit/edit_reference.py): the sizes pictures are read at, the
/// vision tower's patches and windows, the prompt's positions, the image tokens' positions, the noise levels, true
/// guidance and how a mask lies over the latents.
/// </summary>
[TestClass]
[CoversNode("owned-images")]
public class QwenEditSamplerTests
{
    private static readonly JsonObject Reference = Load();

    private static JsonObject Load()
    {
        using var stream = typeof(QwenEditSamplerTests).Assembly.GetManifestResourceStream("qwen-image-edit.json")
                           ?? throw new InvalidOperationException("qwen-image-edit.json is not embedded.");
        return JsonNode.Parse(stream)!.AsObject();
    }

    private static long[] Longs(JsonNode? node) => [.. node!.AsArray().Select(v => v!.GetValue<long>())];

    private static float[] Floats(JsonNode? node) => [.. node!.AsArray().Select(v => (float)v!.GetValue<double>())];

    private static List<(int Rows, int Cols)> Grids(JsonNode? node) =>
        [.. node!.AsArray().Select(g => ((int)g![0]!.GetValue<long>(), (int)g[1]!.GetValue<long>()))];

    [TestMethod]
    public void APicture_IsReadWithinTheVisionTowersAreaAndTheVaesArea_EachSideRoundedTo32()
    {
        foreach (var s in Reference["sizes"]!.AsArray())
        {
            int width = s!["width"]!.GetValue<int>(), height = s["height"]!.GetValue<int>();
            var condition = Longs(s["condition"]);
            var vae = Longs(s["vae"]);

            Assert.AreEqual(((int)condition[0], (int)condition[1]), QwenEditSampler.SizeWithin(QwenEditSampler.ConditionArea, width, height), $"{width}x{height}");
            Assert.AreEqual(((int)vae[0], (int)vae[1]), QwenEditSampler.SizeWithin(QwenEditSampler.VaeArea, width, height), $"{width}x{height}");
        }
    }

    [TestMethod]
    public void APicture_IsResizedAndCutIntoTheVisionTowersPatches_AsQwen2VlsProcessorDoes()
    {
        var reader = Reference["reader"]!;
        int width = reader["width"]!.GetValue<int>(), height = reader["height"]!.GetValue<int>();
        int rows = reader["rows"]!.GetValue<int>(), cols = reader["cols"]!.GetValue<int>();
        byte[] rgb = [.. reader["pixels"]!.AsArray().Select(v => (byte)v!.GetValue<int>())];
        var mean = Floats(reader["mean"]);
        var deviation = Floats(reader["std"]);
        var bytes = reader["patch_bytes"]!.AsArray().Select(v => v!.GetValue<int>()).ToArray();

        var (readWidth, readHeight) = QwenEditSampler.ProcessorSize(width, height);
        var resized = PictureResampling.ResizeAsTorch(rgb, width, height, 3, readWidth, readHeight, PilFilter.Bicubic);
        var patches = QwenEditSampler.ReaderPatches(resized, readWidth, readHeight);

        Assert.AreEqual((cols * QwenEditSampler.ReaderPatch, rows * QwenEditSampler.ReaderPatch), (readWidth, readHeight), "each side rounded to 28");
        Assert.AreEqual(bytes.Length, patches.Length);
        int channelValues = 2 * QwenEditSampler.ReaderPatch * QwenEditSampler.ReaderPatch;
        for (int i = 0; i < bytes.Length; i++)
        {
            int channel = i % QwenEditSampler.ReaderPatchWidth / channelValues;
            Assert.AreEqual((bytes[i] / 255f - mean[channel]) / deviation[channel], patches[i], 1e-5f, $"value {i}");
        }
    }

    [TestMethod]
    public void EachPatch_IsPlacedAndWindowed_AsQwen25VlsVisionTowerDoes()
    {
        foreach (var g in Reference["grids"]!.AsArray())
        {
            int rows = g!["rows"]!.GetValue<int>(), cols = g["cols"]!.GetValue<int>();

            var (positions, order, windows) = QwenEditSampler.ReaderLayout(rows, cols);

            CollectionAssert.AreEqual(Longs(g["positions"]), positions, $"the rotary rows and columns, {rows}x{cols}");
            CollectionAssert.AreEqual(Longs(g["order"]), order, $"the windows' order, {rows}x{cols}");
            CollectionAssert.AreEqual(Longs(g["windows"]), windows, $"each patch's window, {rows}x{cols}");
        }
    }

    [TestMethod]
    public void AnEditsPrompt_IsNumberedAsQwen25VlNumbersIt_EachPictureAGrid()
    {
        foreach (var name in new[] { "prompt", "empty" })
        {
            var p = Reference["prompts"]![name]!;
            var ids = Longs(p["ids"]);
            var expected = p["positions"]!.AsArray().SelectMany(Longs).ToArray();

            CollectionAssert.AreEqual(expected, VisionPromptPositions.Of(ids, QwenEditSampler.ImageToken, Grids(p["grids"])), name);
        }
    }

    [TestMethod]
    public void TheDenoisersTokens_ArePlacedAsQwenEmbedRopePlacesThem_CentredPictureByPicture_ThePromptPastTheirHalfSides()
    {
        var d = Reference["denoiser"]!;
        var grids = Grids(d["grids"]);

        CollectionAssert.AreEqual(Longs(d["image_positions"]), QwenEditSampler.ImagePositions(grids));
        CollectionAssert.AreEqual(Longs(d["prompt_positions"]), QwenEditSampler.DenoiserPromptPositions(d["prompt_tokens"]!.GetValue<int>(), grids));
    }

    [TestMethod]
    public void TheNoiseLevels_AreTheSchedulersForFortySteps_ShiftedByThePicturesSize()
    {
        foreach (var s in Reference["schedules"]!.AsArray())
        {
            int tokens = s!["tokens"]!.GetValue<int>();
            var expected = Floats(s["sigmas"]);

            var sigmas = QwenEditSampler.Sigmas(40, tokens);

            Assert.AreEqual(expected.Length, sigmas.Length);
            for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], sigmas[i], 1e-6f, $"{tokens} tokens, level {i}");
        }
    }

    [TestMethod]
    public void AMask_LiesOverTheLatentsAsTheInpaintPipelineLaysIt_HalvedSampledAndPacked()
    {
        var m = Reference["mask"]!;
        int width = m["width"]!.GetValue<int>(), height = m["height"]!.GetValue<int>();
        byte[] mask = [.. m["mask"]!.AsArray().Select(v => (byte)v!.GetValue<int>())];
        var quads = Floats(m["quads"]);

        var values = QwenEditSampler.TokenMask(mask, width, height);

        Assert.AreEqual(quads.Length * 16, values.Length);
        for (int t = 0; t < quads.Length / 4; t++)
            for (int ch = 0; ch < 16; ch++)
                for (int q = 0; q < 4; q++)
                    Assert.AreEqual(quads[4 * t + q], values[t * QwenEditSampler.Channels + ch * 4 + q], $"token {t}, channel {ch}, place {q}");
    }

    [TestMethod]
    public void TrueGuidance_PushesTowardThePrompt_AndKeepsEachTokensLengthThePromptsOwn()
    {
        var with = new float[QwenEditSampler.Channels];
        var without = new float[QwenEditSampler.Channels];
        with[0] = 3f; with[1] = 4f;          // length 5
        without[0] = 1f;

        var guided = QwenEditSampler.Guided(with, without, 4f);

        // 1 + 4 (3 − 1) = 9 and 4 · 4 = 16, brought back to length 5.
        double length = Math.Sqrt(guided.Sum(v => (double)v * v));
        Assert.AreEqual(5.0, length, 1e-5);
        Assert.AreEqual(9f / 16f, guided[0] / guided[1], 1e-5f, "its direction is the guided one");
    }

    [TestMethod]
    [DataRow(1024, 1024, true)]
    [DataRow(1376, 768, true)]
    [DataRow(1024, 1040, true)]
    [DataRow(2048, 2048, false)]
    [DataRow(1000, 1000, false)]
    public void AnEdit_IsDrawnAtWholeTokensAboutAMegapixel(int width, int height, bool reads)
        => Assert.AreEqual(reads, QwenEditSampler.Reads(width, height));
}

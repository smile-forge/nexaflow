using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Providers.Support;
using System.Buffers.Binary;
using System.IO.Compression;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Providers;

/// <summary>"Nexaflow" drawing: Z-Image Turbo run in the AI host, its three parts loaded for a picture and its steps shown;
/// MOSS-Image, with a negative prompt and its LoRAs.</summary>
[TestClass]
public class OwnedImageGenerationProviderTests
{
    private const string ModelId = "z-image-turbo";

    private static ComputeHardwareReport Nvidia() => new([new ComputeAdapter("gpu", ComputeVendor.Nvidia, 32L << 30, IsIntegrated: false, IsNpu: false)], CudaRuntimePack.MinimumDriver);

    private static ComputeHardwareReport Amd() => new([new ComputeAdapter("gpu", ComputeVendor.Amd, 32L << 30, IsIntegrated: false, IsNpu: false)], null);

    private static OwnedImageGenerationProvider Provider(FakeModelStore store, FakeAiHost host, FakeActivityManager? activities = null)
        => new(activities ?? new FakeActivityManager(), store, host, new HostModelSetup(host), Nvidia(), new ProviderModel(ModelId));

    /// <summary>The model's files and the CUDA pack it runs from.</summary>
    private static FakeModelStore Here() => new([.. Files, .. CudaRuntimePack.Files]);

    private static IReadOnlyList<ModelAsset> Files => Provider(new FakeModelStore(), new FakeAiHost()).Catalog.Single(m => m.Id == ModelId).Files;

    private static FakeAiHost Drawing(params HostReply[] replies) => new()
    {
        Reply = (r, _) => r is Draw d ? [.. replies.Select(reply => reply with { RequestId = d.RequestId }), new Completed(d.RequestId)] : [],
    };

    [TestMethod]
    public void ItIsNexaflowsOwn_DrawingFromAPromptAlone()
    {
        var provider = Provider(new FakeModelStore(), new FakeAiHost());

        Assert.AreEqual(("Nexaflow", ModelHarness.Owned, DataResidency.StaysOnMachine), (provider.Name, provider.Harness, provider.Residency));
        CollectionAssert.AreEqual(new[] { ModelKind.ImageGeneration }, ModelKinds.Of(typeof(OwnedImageGenerationProvider)).ToArray());
        Assert.AreEqual((0, false), (provider.MaxSources, provider.Masks));
        Assert.IsFalse(provider.IsAvailable, "not until its files are here");
        Assert.IsTrue(Provider(new FakeModelStore(Files), new FakeAiHost()).IsAvailable);
    }

    /// <summary>Each card's budget and the models it is offered, each part whole where the budget holds it and else as its lighter
    /// build — a denoiser streamed, a prompt encoder on the CPU.</summary>
    [TestMethod]
    [CoversNode("owned-images-streamed")]
    [DataRow(8, new string[0])]
    [DataRow(10, new[] { "z-image-turbo", "moss-image", "flux2-klein-4b" })]
    [DataRow(12, new[] { "z-image-turbo", "moss-image", "flux2-klein-4b" })]
    [DataRow(16, new[] { "z-image-turbo", "hidream-o1-image", "moss-image", "flux2-klein-4b" })]
    public void EachCard_ListsThePictureModelsItHolds(int gigabytes, string[] listed)
    {
        var card = new ComputeHardwareReport([new ComputeAdapter("gpu", ComputeVendor.Nvidia, (long)gigabytes << 30, IsIntegrated: false, IsNpu: false)], CudaRuntimePack.MinimumDriver);
        var provider = new OwnedImageGenerationProvider(new FakeActivityManager(), new FakeModelStore(), new FakeAiHost(), new HostModelSetup(new FakeAiHost()), card, new ProviderModel(ModelId));

        CollectionAssert.AreEqual(listed, provider.Catalog.Select(m => m.Id).ToArray());
    }

    [TestMethod]
    public void OnAMachineWithoutCuda_ThereIsNoModelToDraw()
    {
        var host = new FakeAiHost();
        var provider = new OwnedImageGenerationProvider(new FakeActivityManager(), new FakeModelStore(Files), host, new HostModelSetup(host), Amd(), new ProviderModel(ModelId));

        Assert.AreEqual(0, provider.Catalog.Count, "the model is built for CUDA alone");
        Assert.IsFalse(provider.IsAvailable, "even with its files here, nothing of it runs on this machine");
    }

    [TestMethod]
    public void AModelThatSaysNoSizes_OnlyChangesPictures_AndOneThatDoes_DrawsFromPromptsToo()
    {
        var provider = Provider(new FakeModelStore(), new FakeAiHost());

        Assert.IsFalse(provider.DrawsFromPromptsOf("qwen-image-edit-2511"), "Qwen-Image-Edit serves the Image Editing row alone");
        Assert.IsTrue(provider.DrawsFromPromptsOf("flux2-klein-4b"), "klein edits and draws");
        Assert.IsTrue(provider.MaxSourcesOf("qwen-image-edit-2511") > 0);
    }

    [TestMethod]
    public async Task APicture_IsDrawnInTheHost_AtTheJobPriority_WithItsThreePartsAndSampler()
    {
        var host = Drawing(new Drawn(0, [0x89, 0x50]));

        var made = await Provider(Here(), host).GenerateImageAsync(new ImageGenerationRequest("a fox", new MediaAspect(1344, 768)));

        var draw = (Draw)host.Requests.Single();
        Assert.AreEqual((RequestPriority.Job, "a fox", 1344, 768), (draw.Priority, draw.Prompt, draw.Width, draw.Height));
        var parts = draw.Model.Parts;
        Assert.AreEqual(("z-image-prompt-encoder", "z-image-denoiser", "z-image-decoder"),
                        (parts[DrawingParts.PromptEncoder].Runner, parts[DrawingParts.Denoiser].Runner, parts[DrawingParts.Decoder].Runner));
        Assert.AreEqual((DrawingMethod.ZImage, 9, 3f, 0f), (draw.Model.Method, draw.Model.Steps, draw.Model.Shift, draw.Model.Guidance));
        StringAssert.StartsWith(parts[DrawingParts.Denoiser].Folder, @"C:\ai\models\z-image-turbo", "the folder is where the store keeps its files");
        Assert.AreEqual(0, parts[DrawingParts.Denoiser].Device, "on the GPU the host found");
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50 }, made.Media.Bytes);
        Assert.AreEqual("image/png", made.Media.MimeType);
    }

    [TestMethod]
    [DataRow(MediaShape.Square, 1024, 1024)]
    [DataRow(MediaShape.Portrait, 768, 1344)]
    public async Task EachShape_IsDrawnAtAboutAMegapixel(MediaShape shape, int width, int height)
    {
        var host = Drawing(new Drawn(0, [1]));

        await Provider(Here(), host).GenerateImageAsync(new ImageGenerationRequest("a fox", new MediaAspect(width, height)));

        var draw = (Draw)host.Requests.Single();
        Assert.AreEqual((width, height), (draw.Width, draw.Height));
    }

    [TestMethod]
    public async Task ItOffersTheShapesItWasTrainedOn_AndDrawsFromTheSeedItIsGiven()
    {
        var host     = Drawing(new Drawn(0, [1]));
        var provider = Provider(Here(), host);
        CollectionAssert.AreEqual(new[] { "1:1", "7:4", "4:7" }, provider.Settings.Aspects.Select(a => a.ToString()).ToArray());
        Assert.IsTrue(provider.Settings.Seeds);

        await provider.GenerateImageAsync(new ImageGenerationRequest("a fox", new(4, 7)) { Seed = 1234 });

        var draw = (Draw)host.Requests.Single();
        Assert.AreEqual((768, 1344, 1234L), (draw.Width, draw.Height, draw.Seed));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.GenerateImageAsync(new ImageGenerationRequest("a fox", new(16, 9))),
                                                           "proportions it was not trained on");
    }

    [TestMethod]
    public async Task EachStep_IsShownOnTheActivity()
    {
        var activities = new FakeActivityManager();
        var host = Drawing(new Progress(0, "drawing", 1, 9), new Progress(0, "drawing", 2, 9), new Drawn(0, [1]));

        await Provider(Here(), host, activities).GenerateImageAsync(new ImageGenerationRequest("a fox", new(1, 1)));

        Assert.AreEqual(1, activities.StartedCount);
        CollectionAssert.AreEqual(new[] { "step 1 of 9", "step 2 of 9" }, activities.Updates.Select(u => u[(u.LastIndexOf('—') + 2)..]).ToArray());
    }

    [TestMethod]
    public async Task AHostThatCannotDraw_FailsAsTheProvider()
    {
        var host = new FakeAiHost { Reply = (r, _) => [new Failed(r.RequestId, HostErrorKind.Refused, "not enough room on the GPU")] };

        var ex = await Assert.ThrowsExactlyAsync<LlmProviderException>(() =>
            Provider(Here(), host).GenerateImageAsync(new ImageGenerationRequest("a fox", new(1, 1))));

        StringAssert.Contains(ex.Message, "not enough room on the GPU");
    }

    [TestMethod]
    public async Task OnAGpuTooSmallForItsLargestPart_NoPictureIsDrawn()
    {
        var host = new FakeAiHost { Gpus = [new HostGpu(0, "NVIDIA GeForce GTX 1080", null, 8L << 30, 6L << 30, 7L << 30)] };

        var ex = await Assert.ThrowsExactlyAsync<LlmProviderException>(() =>
            Provider(Here(), host).GenerateImageAsync(new ImageGenerationRequest("a fox", new(1, 1))));

        StringAssert.Contains(ex.Message, "more than any GPU here has for local models");
        Assert.AreEqual(0, host.Requests.Count, "nothing is asked of a host that could not hold it");
    }

    [TestMethod]
    public async Task PicturesToWorkFrom_AreRefused_ForAModelThatOnlyDraws()
    {
        var request = new ImageGenerationRequest("night", new(1, 1)) { Sources = [new MediaBytes([1], "image/png")] };

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Provider(Here(), new FakeAiHost()).GenerateImageAsync(request));
    }

    [TestMethod]
    public async Task WarmingUp_LoadsNothing()
    {
        var host = new FakeAiHost();

        await ((IAiProvider)Provider(Here(), host)).WarmupAsync();

        Assert.AreEqual(0, host.Requests.Count, "an image model loads with its first picture, not beside the resident models");
    }

    private static OwnedImageGenerationProvider Moss(FakeModelStore store, FakeAiHost host)
        => new(new FakeActivityManager(), store, host, new HostModelSetup(host), Nvidia(), new ProviderModel("moss-image"));

    /// <summary>MOSS-Image's files and the CUDA pack — none of its LoRAs.</summary>
    private static FakeModelStore MossHere() =>
        new([.. Moss(new FakeModelStore(), new FakeAiHost()).Catalog.Single(m => m.Id == "moss-image").Files, .. CudaRuntimePack.Files]);

    [TestMethod]
    public void MossImage_OffersANegativePromptAndItsLoras_WhereZImageOffersNeither()
    {
        var moss = Moss(MossHere(), new FakeAiHost()).Settings;
        var zImage = Provider(Here(), new FakeAiHost()).Settings;

        Assert.IsTrue(moss.NegativePrompts);
        Assert.AreEqual(8, moss.Loras.Count);
        Assert.IsFalse(moss.LoraOf("moss-fast-8step")!.NegativePrompts, "the distillation draws with no negative prompt");
        Assert.IsTrue(moss.LoraOf("moss-nsfw")!.Adult);
        StringAssert.Contains(moss.LoraOf("moss-anime")!.Description, "anime");
        Assert.AreEqual((false, 0), (zImage.NegativePrompts, zImage.Loras.Count));
    }

    [TestMethod]
    public async Task ALora_IsFetchedTheFirstTimeAPictureAsksForIt_AndDrawnWithItsTriggerLeadingThePrompt()
    {
        var store = MossHere();
        var host = Drawing(new Drawn(0, [1]));
        var provider = Moss(store, host);
        Assert.IsTrue(provider.IsAvailable, "the model is here with none of its LoRAs");

        await provider.GenerateImageAsync(new ImageGenerationRequest("a fox", new(1, 1)) { Lora = "moss-anime", NegativePrompt = "people" });

        var draw = (Draw)host.Requests.Single();
        Assert.AreEqual((DrawingMethod.MossImage, 50, 5f, 3f), (draw.Model.Method, draw.Model.Steps, draw.Model.Shift, draw.Model.Guidance));
        Assert.AreEqual(("moss_seinen_style, a fox", "people"), (draw.Prompt, draw.NegativePrompt));
        Assert.AreEqual((FakeModelStore.Root + @"models\moss-image\loras\moss-anime.onnx_adapter", 1f, (string?)null),
                        (draw.Lora!.Adapter, draw.Lora.Strength, draw.Lora.Sampler));
        CollectionAssert.AreEqual(new[] { "models/moss-image/loras/moss-anime.onnx_adapter" }, store.Fetched.Single().Select(f => f.RelativePath).ToArray(),
                                  "only the LoRA asked for is fetched");
    }

    [TestMethod]
    public async Task TheDistillation_BringsItsOwnSampler_AndIsDrawnWithNoNegativePrompt()
    {
        var host = Drawing(new Drawn(0, [1]));
        var provider = Moss(MossHere(), host);

        await provider.GenerateImageAsync(new ImageGenerationRequest("a fox", new(1, 1)) { Lora = "moss-fast-8step" });

        var draw = (Draw)host.Requests.Single();
        Assert.AreEqual(("a fox", (string?)null), (draw.Prompt, draw.NegativePrompt), "it has no trigger, and nothing was kept out");
        StringAssert.EndsWith(draw.Lora!.Sampler, @"loras\moss-fast-8step.sampler.json");
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.GenerateImageAsync(
            new ImageGenerationRequest("a fox", new(1, 1)) { Lora = "moss-fast-8step", NegativePrompt = "people" }), "it is told nothing to keep out");
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => provider.GenerateImageAsync(
            new ImageGenerationRequest("a fox", new(1, 1)) { Lora = "moss-watercolour" }), "a LoRA it does not have");
        Assert.AreEqual(1, host.Requests.Count, "nothing refused reaches the host");
    }

    [TestMethod]
    public async Task APictureWithNoLora_FetchesNone_AndIsDrawnWithoutOne()
    {
        var store = MossHere();
        var host = Drawing(new Drawn(0, [1]));

        await Moss(store, host).GenerateImageAsync(new ImageGenerationRequest("a fox", new(1, 1)));

        Assert.AreEqual(0, store.Fetched.Count);
        Assert.IsNull(((Draw)host.Requests.Single()).Lora);
    }

    private const string KleinId = "flux2-klein-4b";

    private static OwnedImageGenerationProvider Klein(FakeModelStore store, FakeAiHost host)
        => new(new FakeActivityManager(), store, host, new HostModelSetup(host), Nvidia(), new ProviderModel(KleinId));

    /// <summary>FLUX.2 [klein]'s files and the CUDA pack.</summary>
    private static FakeModelStore KleinHere() =>
        new([.. Klein(new FakeModelStore(), new FakeAiHost()).Catalog.Single(m => m.Id == KleinId).Files, .. CudaRuntimePack.Files]);

    [TestMethod]
    public void FluxKlein_WorksFromTwoPictures_UnderAMask_AtTheFirstOnesProportions_WhereZImageOnlyDraws()
    {
        var klein = Klein(KleinHere(), new FakeAiHost());
        var zImage = Provider(Here(), new FakeAiHost());

        Assert.AreEqual((2, true, true), (klein.MaxSources, klein.Masks, klein.Settings.KeepsSourceProportions));
        Assert.AreEqual((2, 0), (zImage.MaxSourcesOf(KleinId), zImage.MaxSourcesOf(ModelId)), "any instance says what each model works from");
        Assert.IsFalse(zImage.Settings.KeepsSourceProportions);
    }

    [TestMethod]
    public async Task AnEdit_SendsItsPictureReadWithinAMegapixel_AndWhatTheMaskLeavesTransparentAsWhatIsDrawnAgain()
    {
        var host = Drawing(new Drawn(0, [1]));
        var source = PngEncoder.EncodeRgb(new byte[2000 * 1000 * 3], 2000, 1000);
        var mask = RgbaPng(2000, 1000, (x, _) => x < 1000 ? (byte)0 : (byte)255);   // the left half drawn again

        await Klein(KleinHere(), host).GenerateImageAsync(new ImageGenerationRequest("a lake", new(1, 1))
        {
            Sources = [new MediaBytes(source, "image/png")],
            Mask    = new MediaBytes(mask, "image/png"),
        });

        var draw = (Draw)host.Requests.Single();
        Assert.AreEqual((1440, 720), (draw.Width, draw.Height), "2000x1000 at its proportions within a megapixel, each side a multiple of 16");
        Assert.AreEqual((1440, 720, 1440 * 720 * 3), (draw.Sources.Single().Width, draw.Sources.Single().Height, draw.Sources.Single().Rgb.Length));
        Assert.AreEqual((DrawingMethod.Flux2Klein, 4, 0f), (draw.Model.Method, draw.Model.Steps, draw.Model.Shift));
        Assert.AreEqual(1440 * 720, draw.Mask!.Length);
        Assert.AreEqual(255, draw.Mask[360 * 1440 + 100], "where the mask is transparent, drawn again");
        Assert.AreEqual(0, draw.Mask[360 * 1440 + 1300], "where it is opaque, kept");
        Assert.AreEqual("klein-picture-encoder", draw.Model.Parts[DrawingParts.PictureEncoder].Runner);
    }

    [TestMethod]
    public async Task FluxKlein_RefusesMorePicturesThanItReads_AndAMaskWithNoPictureToLieOver()
    {
        var picture = new MediaBytes(PngEncoder.EncodeRgb(new byte[64 * 64 * 3], 64, 64), "image/png");

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Klein(KleinHere(), new FakeAiHost())
            .GenerateImageAsync(new ImageGenerationRequest("x", new(1, 1)) { Sources = [picture, picture, picture] }));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Klein(KleinHere(), new FakeAiHost())
            .GenerateImageAsync(new ImageGenerationRequest("x", new(1, 1)) { Mask = picture }));
    }

    /// <summary>A PNG of black pixels whose transparency <paramref name="alpha"/> gives at each pixel.</summary>
    private static byte[] RgbaPng(int width, int height, Func<int, int, byte> alpha)
    {
        var raw = new byte[height * (width * 4 + 1)];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                raw[y * (width * 4 + 1) + 1 + x * 4 + 3] = alpha(x, y);
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;   // bits a channel
        header[9] = 6;   // RGBA
        Chunk(png, "IHDR", header);
        using (var deflated = new MemoryStream())
        {
            using (var z = new ZLibStream(deflated, CompressionLevel.Fastest, leaveOpen: true)) z.Write(raw);
            Chunk(png, "IDAT", deflated.ToArray());
        }
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        var typed = new byte[4 + data.Length];
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(typed, 0);
        data.CopyTo(typed, 4);
        png.Write(typed);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(typed));
        png.Write(crc);
    }

    /// <summary>PNG's CRC-32 of a chunk's type and data.</summary>
    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }
}

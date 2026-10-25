using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Providers.Support;

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

    [TestMethod]
    public void OnAMachineWithoutCuda_ThereIsNoModelToDraw()
    {
        var host = new FakeAiHost();
        var provider = new OwnedImageGenerationProvider(new FakeActivityManager(), new FakeModelStore(Files), host, new HostModelSetup(host), Amd(), new ProviderModel(ModelId));

        Assert.AreEqual(0, provider.Catalog.Count, "the model is built for CUDA alone");
        Assert.IsFalse(provider.IsAvailable, "even with its files here, nothing of it runs on this machine");
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

        StringAssert.Contains(ex.Message, "needs a GPU with");
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
}

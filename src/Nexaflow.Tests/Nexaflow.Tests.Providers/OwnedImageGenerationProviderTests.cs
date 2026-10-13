using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>"Nexaflow" drawing: Z-Image Turbo run in the AI host, its three parts loaded for a picture and its steps shown.</summary>
[TestClass]
public class OwnedImageGenerationProviderTests
{
    private const string ModelId = "z-image-turbo";

    private static ComputeHardwareReport Nvidia() => new([new ComputeAdapter("gpu", ComputeVendor.Nvidia, 32L << 30, IsIntegrated: false, IsNpu: false)], CudaRuntimePack.MinimumDriver);

    private static ComputeHardwareReport Amd() => new([new ComputeAdapter("gpu", ComputeVendor.Amd, 32L << 30, IsIntegrated: false, IsNpu: false)], null);

    private static OwnedImageGenerationProvider Provider(FakeModelStore store, FakeAiHost host, FakeActivityManager? activities = null)
        => new(activities ?? new FakeActivityManager(), store, host, Nvidia(), new ProviderModel(ModelId));

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
        var provider = new OwnedImageGenerationProvider(new FakeActivityManager(), new FakeModelStore(Files), new FakeAiHost(), Amd(), new ProviderModel(ModelId));

        Assert.AreEqual(0, provider.Catalog.Count, "the model is built for CUDA alone");
        Assert.IsFalse(provider.IsAvailable, "even with its files here, nothing of it runs on this machine");
    }

    [TestMethod]
    public async Task APicture_IsDrawnInTheHost_AtTheJobPriority_WithItsThreePartsAndSampler()
    {
        var host = Drawing(new Drawn(0, [0x89, 0x50]));

        var made = await Provider(new FakeModelStore(Files), host).GenerateImageAsync(new ImageGenerationRequest("a fox", MediaShape.Landscape));

        var draw = (Draw)host.Requests.Single();
        Assert.AreEqual((RequestPriority.Job, "a fox", 1344, 768), (draw.Priority, draw.Prompt, draw.Width, draw.Height));
        Assert.AreEqual(("z-image-prompt-encoder", "z-image-denoiser", "z-image-decoder"),
                        (draw.Model.PromptEncoder.Runner, draw.Model.Denoiser.Runner, draw.Model.Decoder.Runner));
        Assert.AreEqual((9, 3f), (draw.Model.Steps, draw.Model.Shift));
        StringAssert.StartsWith(draw.Model.Denoiser.Folder, @"C:\ai\models\z-image-turbo", "the folder is where the store keeps its files");
        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50 }, made.Media.Bytes);
        Assert.AreEqual("image/png", made.Media.MimeType);
    }

    [TestMethod]
    [DataRow(MediaShape.Square, 1024, 1024)]
    [DataRow(MediaShape.Portrait, 768, 1344)]
    public async Task EachShape_IsDrawnAtAboutAMegapixel(MediaShape shape, int width, int height)
    {
        var host = Drawing(new Drawn(0, [1]));

        await Provider(new FakeModelStore(Files), host).GenerateImageAsync(new ImageGenerationRequest("a fox", shape));

        var draw = (Draw)host.Requests.Single();
        Assert.AreEqual((width, height), (draw.Width, draw.Height));
    }

    [TestMethod]
    public async Task EachStep_IsShownOnTheActivity()
    {
        var activities = new FakeActivityManager();
        var host = Drawing(new Progress(0, "drawing", 1, 9), new Progress(0, "drawing", 2, 9), new Drawn(0, [1]));

        await Provider(new FakeModelStore(Files), host, activities).GenerateImageAsync(new ImageGenerationRequest("a fox"));

        Assert.AreEqual(1, activities.StartedCount);
        CollectionAssert.AreEqual(new[] { "step 1 of 9", "step 2 of 9" }, activities.Updates.Select(u => u[(u.LastIndexOf('—') + 2)..]).ToArray());
    }

    [TestMethod]
    public async Task AHostThatCannotDraw_FailsAsTheProvider()
    {
        var host = new FakeAiHost { Reply = (r, _) => [new Failed(r.RequestId, HostErrorKind.Refused, "not enough room on the GPU")] };

        var ex = await Assert.ThrowsExactlyAsync<LlmProviderException>(() =>
            Provider(new FakeModelStore(Files), host).GenerateImageAsync(new ImageGenerationRequest("a fox")));

        StringAssert.Contains(ex.Message, "not enough room on the GPU");
    }

    [TestMethod]
    public async Task PicturesToWorkFrom_AreRefused_ForAModelThatOnlyDraws()
    {
        var request = new ImageGenerationRequest("night") { Sources = [new MediaBytes([1], "image/png")] };

        await Assert.ThrowsExactlyAsync<ArgumentException>(() => Provider(new FakeModelStore(Files), new FakeAiHost()).GenerateImageAsync(request));
    }

    [TestMethod]
    public async Task WarmingUp_FetchesTheFiles_ButLoadsNothing()
    {
        var host = new FakeAiHost();

        await Provider(new FakeModelStore(Files), host).WarmupAsync();

        Assert.AreEqual(0, host.Requests.Count, "an image model loads with its first picture, not beside the resident models");
    }
}

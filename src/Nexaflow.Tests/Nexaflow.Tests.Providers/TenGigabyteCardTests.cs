using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// What a machine with a 10 GB card and 32 GB of memory is offered: a model of every kind Nexaflow runs itself — a language
/// model, speech in and out, a picture drawn from a prompt and one changed, a clip — and every picture tool, each part whole
/// where the card's budget holds it and else lighter: a mixture's experts in memory, a denoiser streamed, a decoder in tiles,
/// a prompt encoder on the CPU.
/// </summary>
[TestClass]
[CoversNode("local-ai-setup")]
public class TenGigabyteCardTests
{
    private static readonly ComputeHardwareReport Card =
        new([new ComputeAdapter("NVIDIA GeForce RTX 3080", ComputeVendor.Nvidia, 10L << 30, IsIntegrated: false, IsNpu: false)], CudaRuntimePack.MinimumDriver);

    private static FakeAiHost Host() => new() { Memory = HostMemory.Of(32L << 30, AiHostProtocol.DefaultMemoryShare) };

    private static ProviderModel First(ModelKind kind) => new(OwnedModels.OfKind(kind).First().Id);

    [TestMethod]
    public void EveryKindOfModel_HasOneItRuns()
    {
        var host = Host();
        var setup = new HostModelSetup(host);
        var store = new FakeModelStore();
        var activities = new FakeActivityManager();

        Assert.AreNotEqual(0, new OwnedLlmProvider(activities, store, host, setup, Card, First(ModelKind.Language)).Catalog.Count, "a language model");
        Assert.AreNotEqual(0, new OwnedSpeechToTextProvider(activities, store, host, setup, Card, First(ModelKind.SpeechToText)).Catalog.Count, "speech in");
        Assert.AreNotEqual(0, new OwnedTextToSpeechProvider(activities, store, host, setup, Card, First(ModelKind.TextToSpeech)).Catalog.Count, "speech out");
        Assert.AreNotEqual(0, new OwnedVideoGenerationProvider(activities, store, host, setup, Card, First(ModelKind.VideoGeneration)).Catalog.Count, "a clip");

        var pictures = new OwnedImageGenerationProvider(activities, store, host, setup, Card, First(ModelKind.ImageGeneration)).Catalog.Select(m => m.Id).ToHashSet();
        var images = OwnedModels.OfKind(ModelKind.ImageGeneration).Where(m => pictures.Contains(m.Id)).ToList();
        Assert.IsTrue(images.Any(m => m.Sampler!.Sizes.Count > 0), "a picture drawn from a prompt");
        Assert.IsTrue(images.Any(m => m.Sampler!.Editing is not null), "a picture changed");
    }

    [TestMethod]
    public void EveryPictureTool_Runs()
    {
        var host = Host();
        var setup = new HostModelSetup(host);
        var store = new FakeModelStore();
        var activities = new FakeActivityManager();

        OwnedToolFront[] tools =
        [
            new OwnedPictureTools(activities, store, host, setup, Card),
            new OwnedPictureSelection(activities, store, host, setup, Card),
            new OwnedPageReading(activities, store, host, setup, Card),
            new OwnedPictureErasing(activities, store, host, setup, Card),
            new OwnedPictureChecking(activities, store, host, setup, Card),
            new OwnedSceneDepth(activities, store, host, setup, Card),
        ];
        foreach (var tool in tools) Assert.IsTrue(tool.RunsHere, tool.GetType().Name);
    }
}

using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;

using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// "Nexaflow" video: Wan2.2-TI2V-5B run in the AI host — FastWan's three steps from a description, the full model's fifty
/// from a description or a picture to start from — its parts loaded for a clip, its steps and decoding shown.
/// </summary>
[TestClass]
public class OwnedVideoGenerationProviderTests
{
    private const string FastWan = "fastwan2.2-ti2v-5b";
    private const string Wan     = "wan2.2-ti2v-5b";

    private static ComputeHardwareReport Nvidia() => new([new ComputeAdapter("gpu", ComputeVendor.Nvidia, 32L << 30, IsIntegrated: false, IsNpu: false)], CudaRuntimePack.MinimumDriver);

    private static ComputeHardwareReport Amd() => new([new ComputeAdapter("gpu", ComputeVendor.Amd, 32L << 30, IsIntegrated: false, IsNpu: false)], null);

    /// <summary>A host on a 32 GB card: room for the decoder, the largest part.</summary>
    private static FakeAiHost Roomy(FakeAiHost host)
    {
        host.Gpus = [new HostGpu(0, "NVIDIA GeForce RTX 5090", null, 32L << 30, 30L << 30, 31L << 30)];
        return host;
    }

    private static OwnedVideoGenerationProvider Provider(string modelId, FakeModelStore store, FakeAiHost host, FakeActivityManager? activities = null)
        => new(activities ?? new FakeActivityManager(), store, host, new HostModelSetup(host), Nvidia(), new ProviderModel(modelId));

    /// <summary>Both models' files and the CUDA pack they run from.</summary>
    private static FakeModelStore Here() => new([.. Files(FastWan).Concat(Files(Wan)).DistinctBy(f => f.RelativePath), .. CudaRuntimePack.Files]);

    private static IReadOnlyList<ModelAsset> Files(string modelId)
        => Provider(modelId, new FakeModelStore(), new FakeAiHost()).Catalog.Single(m => m.Id == modelId).Files;

    private static FakeAiHost Filming(params HostReply[] replies) => Roomy(new FakeAiHost
    {
        Reply = (r, _) => r is Film f ? [.. replies.Select(reply => reply with { RequestId = f.RequestId }), new Completed(f.RequestId)] : [],
    });

    private static MediaBytes Picture(int width, int height) => new(PngEncoder.EncodeRgb(new byte[width * height * 3], width, height), "image/png");

    [TestMethod]
    public void ItIsNexaflowsOwn_FastWanFromADescription_TheFullModelFromAPictureToo()
    {
        var fast = Provider(FastWan, new FakeModelStore(), new FakeAiHost());
        var full = Provider(Wan, new FakeModelStore(), new FakeAiHost());

        Assert.AreEqual(("Nexaflow", ModelHarness.Owned, DataResidency.StaysOnMachine), (fast.Name, fast.Harness, fast.Residency));
        CollectionAssert.AreEqual(new[] { ModelKind.VideoGeneration }, ModelKinds.Of(typeof(OwnedVideoGenerationProvider)).ToArray());
        Assert.AreEqual((false, true), (fast.StartsFromImages, full.StartsFromImages));
        CollectionAssert.AreEqual(new[] { 5 }, fast.Lengths.ToArray());
        Assert.AreEqual((704, 1280), full.FirstFrameSize(MediaShape.Portrait), "the picture becomes the clip's first frame, at its size");
        Assert.IsFalse(fast.IsAvailable, "not until its files are here");
        Assert.IsTrue(Provider(FastWan, new FakeModelStore(Files(FastWan)), new FakeAiHost()).IsAvailable);
        Assert.IsFalse(Provider(Wan, new FakeModelStore(Files(FastWan)), new FakeAiHost()).IsAvailable,
                       "FastWan's files hold neither the full model's denoiser nor its frame encoder");
    }

    [TestMethod]
    public void OnAMachineWithoutCuda_ThereIsNoModelToFilmWith()
    {
        var host = new FakeAiHost();
        var provider = new OwnedVideoGenerationProvider(new FakeActivityManager(), Here(), host, new HostModelSetup(host), Amd(), new ProviderModel(FastWan));

        Assert.AreEqual(0, provider.Catalog.Count, "the models are built for CUDA alone");
        Assert.IsFalse(provider.IsAvailable);
    }

    [TestMethod]
    public async Task FastWan_FilmsInTheHost_AtTheJobPriority_InItsThreeDistilledSteps()
    {
        var host = Filming(new Filmed(0, [0x00, 0x00, 0x00, 0x18]));

        var made = await Provider(FastWan, Here(), host).GenerateVideoAsync(new VideoGenerationRequest("a fox in the snow", MediaShape.Landscape, 5));

        var film = (Film)host.Requests.Single();
        Assert.AreEqual((RequestPriority.Job, "a fox in the snow", 1280, 704, 121, 24), (film.Priority, film.Prompt, film.Width, film.Height, film.Frames, film.FramesPerSecond));
        Assert.IsNull(film.FirstFrame);
        Assert.AreEqual((FilmingMethod.WanDistilled, 8f, 0f), (film.Model.Method, film.Model.Shift, film.Model.Guidance));
        CollectionAssert.AreEqual(new[] { 1000, 757, 522 }, film.Model.Timesteps.ToArray());
        CollectionAssert.AreEquivalent(new[] { FilmingParts.PromptEncoder, FilmingParts.Denoiser, FilmingParts.Decoder }, film.Model.Parts.Keys.ToArray());
        StringAssert.EndsWith(film.Model.Parts[FilmingParts.Denoiser].Folder, @"wan2.2-ti2v-5b\denoiser-fast");
        Assert.AreEqual(0, film.Model.Parts[FilmingParts.Decoder].Device, "on the GPU the host found");
        Assert.AreEqual("video/mp4", made.Media.MimeType);
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x00, 0x00, 0x18 }, made.Media.Bytes);
    }

    [TestMethod]
    public async Task TheFullModel_StartsFromAPicture_ItsFirstFrame_AndSteersFromItsNegativePrompt()
    {
        var host = Filming(new Filmed(0, [1]));
        var request = new VideoGenerationRequest("the waves roll in", MediaShape.Portrait, 5) { FirstFrame = Picture(704, 1280) };

        await Provider(Wan, Here(), host).GenerateVideoAsync(request);

        var film = (Film)host.Requests.Single();
        Assert.AreEqual((704, 1280), (film.Width, film.Height));
        Assert.AreEqual(704 * 1280 * 3, film.FirstFrame!.Length, "the picture goes to the host as its pixels");
        Assert.AreEqual((FilmingMethod.Wan, 50, 5f, 5f), (film.Model.Method, film.Model.Steps, film.Model.Shift, film.Model.Guidance));
        StringAssert.StartsWith(film.Model.NegativePrompt, "色调艳丽");
        Assert.IsTrue(film.Model.Parts.ContainsKey(FilmingParts.FrameEncoder), "the picture is encoded into the clip's first latent frame");
    }

    [TestMethod]
    public async Task APicture_IsRefused_ByFastWan_AndAtAnySizeButTheClips()
    {
        var picture = new VideoGenerationRequest("the waves roll in", MediaShape.Landscape, 5) { FirstFrame = Picture(1280, 704) };
        var wrongSize = picture with { FirstFrame = Picture(640, 352) };

        var fast = await Assert.ThrowsExactlyAsync<ArgumentException>(() => Provider(FastWan, Here(), Filming()).GenerateVideoAsync(picture));
        var size = await Assert.ThrowsExactlyAsync<ArgumentException>(() => Provider(Wan, Here(), Filming()).GenerateVideoAsync(wrongSize));

        StringAssert.Contains(fast.Message, "from a description alone");
        StringAssert.Contains(size.Message, "not the clip's 1280x704");
    }

    [TestMethod]
    public async Task AClip_IsMadeOnlyAtALengthTheModelMakes()
    {
        var ex = await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            Provider(FastWan, Here(), Filming()).GenerateVideoAsync(new VideoGenerationRequest("a fox", MediaShape.Landscape, 8)));

        StringAssert.Contains(ex.Message, "5 seconds");
    }

    [TestMethod]
    public async Task EachStep_AndTheDecoding_AreShownOnTheActivity()
    {
        var activities = new FakeActivityManager();
        var host = Filming(new Progress(0, FilmingStages.Filming, 1, 3), new Progress(0, FilmingStages.Decoding, 16, 31), new Filmed(0, [1]));

        await Provider(FastWan, Here(), host, activities).GenerateVideoAsync(new VideoGenerationRequest("a fox", MediaShape.Landscape, 5));

        Assert.AreEqual(1, activities.StartedCount);
        CollectionAssert.AreEqual(new[] { "step 1 of 3", "decoding, 51%" }, activities.Updates.Select(u => u[(u.LastIndexOf('—') + 2)..]).ToArray());
    }

    [TestMethod]
    public async Task OnAGpuTooSmallForTheDecoder_NoClipIsMade()
    {
        var host = new FakeAiHost { Gpus = [new HostGpu(0, "NVIDIA GeForce RTX 4080", null, 16L << 30, 14L << 30, 15L << 30)] };

        var ex = await Assert.ThrowsExactlyAsync<LlmProviderException>(() =>
            Provider(FastWan, Here(), host).GenerateVideoAsync(new VideoGenerationRequest("a fox", MediaShape.Landscape, 5)));

        StringAssert.Contains(ex.Message, "needs a GPU with");
        Assert.AreEqual(0, host.Requests.Count, "nothing is asked of a host that could not hold it");
    }
}

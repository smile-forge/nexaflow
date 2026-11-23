using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Media.Png;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;

using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>
/// "Nexaflow" video: Wan2.2-TI2V-5B run in the AI host — FastWan's three steps from a description, the full model's fifty
/// from a description or a picture to start from — named to it by its catalog id, its steps and decoding shown.
/// </summary>
[TestClass]
public class OwnedVideoGenerationProviderTests
{
    private const string FastWan = "fastwan2.2-ti2v-5b";
    private const string Wan     = "wan2.2-ti2v-5b";

    private static MachineCards Nvidia() => new([new MachineCard("gpu", 32L << 30)], CudaRuntimePack.MinimumDriver);

    private static MachineCards Amd() => MachineCards.None;

    /// <summary>A host on a 32 GB card: room for the decoder, the largest part.</summary>
    private static FakeAiHost Roomy(FakeAiHost host)
    {
        host.Gpus = [new HostGpu(0, "NVIDIA GeForce RTX 5090", null, 32L << 30, 30L << 30, 31L << 30)];
        return host;
    }

    private static OwnedVideoGenerationProvider Provider(string modelId, FakeModelStore store, FakeAiHost host, FakeActivityManager? activities = null)
        => new(activities ?? new FakeActivityManager(), store, host, new HostHolds(host), Nvidia(), new ProviderModel(modelId));

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
        Assert.AreEqual((704, 1280), full.FirstFrameSize(new MediaAspect(704, 1280), null), "the picture becomes the clip's first frame, at its size");
        Assert.IsFalse(fast.IsAvailable, "not until its files are here");
        Assert.IsTrue(Provider(FastWan, new FakeModelStore(Files(FastWan)), new FakeAiHost()).IsAvailable);
        Assert.IsFalse(Provider(Wan, new FakeModelStore(Files(FastWan)), new FakeAiHost()).IsAvailable,
                       "FastWan's files hold neither the full model's denoiser nor its frame encoder");
    }

    [TestMethod]
    public void OnAMachineWithoutCuda_ThereIsNoModelToFilmWith()
    {
        var host = new FakeAiHost();
        var provider = new OwnedVideoGenerationProvider(new FakeActivityManager(), Here(), host, new HostHolds(host), Amd(), new ProviderModel(FastWan));

        Assert.AreEqual(0, provider.Catalog.Count, "the models are built for CUDA alone");
        Assert.IsFalse(provider.IsAvailable);
    }

    /// <summary>What a host sends of a clip: <paramref name="count"/> grey frames of the size asked.</summary>
    private static FilmedFrames Frames(int width, int height, int count)
    {
        var rgb = new byte[width * height * 3 * count];
        Array.Fill(rgb, (byte)128);
        return new FilmedFrames(0, SharedBytes.Of(rgb));
    }

    [TestMethod]
    public async Task FastWan_FilmsInTheHost_AtTheJobPriority_ByItsCatalogId()
    {
        var host = Filming(Frames(1280, 704, 2));

        var made = await Provider(FastWan, Here(), host).GenerateVideoAsync(new VideoGenerationRequest("a fox in the snow", new MediaAspect(1280, 704), 5));

        var film = (Film)host.Requests.Single();
        Assert.AreEqual((RequestPriority.Job, "a fox in the snow", 1280, 704, 121, 24), (film.Priority, film.Prompt, film.Width, film.Height, film.Frames, film.FramesPerSecond));
        Assert.AreEqual((FastWan, (string?)null), (film.Model, film.NegativePrompt),
                        "the model by the catalog's id — its parts, its steps and where each runs are the host's to take from it — and nothing asked kept out");
        Assert.IsNull(film.FirstFrame);
        Assert.IsTrue(host.Environment.Cuda, "the host runs from the pack");
        Assert.AreEqual(0, host.Holds.Count, "a clip's parts are loaded by the host for it, not held beside the resident models");
        Assert.AreEqual("video/mp4", made.Media.MimeType);
        CollectionAssert.AreEqual("ftyp"u8.ToArray(), made.Media.Bytes[4..8], "the host's frames, encoded as an MP4 as they came");
    }

    [TestMethod]
    public async Task TheFullModel_StartsFromAPicture_ItsFirstFrame()
    {
        var host = Filming(Frames(704, 1280, 1));
        var request = new VideoGenerationRequest("the waves roll in", new MediaAspect(704, 1280), 5) { FirstFrame = Picture(704, 1280) };

        await Provider(Wan, Here(), host).GenerateVideoAsync(request);

        var film = (Film)host.Requests.Single();
        Assert.AreEqual((704, 1280), (film.Width, film.Height));
        Assert.AreEqual(704 * 1280 * 3, film.FirstFrame!.Length, "the picture goes to the host as its pixels");
        Assert.AreEqual((Wan, (string?)null), (film.Model, film.NegativePrompt), "nothing asked kept out: the catalog's own negative prompt is the host's to add");
    }

    [TestMethod]
    public async Task TheFullModel_PassesOnWhatItIsToldToKeepOut_FromTheSeedItIsGiven()
    {
        var host = Filming(Frames(1280, 704, 1));

        await Provider(Wan, Here(), host).GenerateVideoAsync(new VideoGenerationRequest("the waves roll in", new MediaAspect(1280, 704), 5)
        {
            NegativePrompt = " boats ",
            Seed           = 99,
        });

        var film = (Film)host.Requests.Single();
        Assert.AreEqual("boats", film.NegativePrompt, "what the user asks kept out, trimmed: the host adds it to the catalog's own");
        Assert.AreEqual(99L, film.Seed);
    }

    [TestMethod]
    public async Task FastWan_IsToldNothingToKeepOut_ForItIsNotGuided()
    {
        var fast = Provider(FastWan, Here(), Filming());
        Assert.IsFalse(fast.Settings.NegativePrompts);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            fast.GenerateVideoAsync(new VideoGenerationRequest("a fox", new MediaAspect(1280, 704), 5) { NegativePrompt = "rain" }));
    }

    [TestMethod]
    public async Task APicture_IsRefused_ByFastWan_AndAtAnySizeButTheClips()
    {
        var picture = new VideoGenerationRequest("the waves roll in", new MediaAspect(1280, 704), 5) { FirstFrame = Picture(1280, 704) };
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
            Provider(FastWan, Here(), Filming()).GenerateVideoAsync(new VideoGenerationRequest("a fox", new MediaAspect(1280, 704), 8)));

        StringAssert.Contains(ex.Message, "5 seconds");
    }

    [TestMethod]
    public async Task EachStep_AndTheDecoding_AreShownOnTheActivity()
    {
        var activities = new FakeActivityManager();
        var host = Filming(new Progress(0, FilmingStages.Filming, 1, 3), new Progress(0, FilmingStages.Decoding, 16, 31), Frames(1280, 704, 1));

        await Provider(FastWan, Here(), host, activities).GenerateVideoAsync(new VideoGenerationRequest("a fox", new MediaAspect(1280, 704), 5));

        Assert.AreEqual(1, activities.StartedCount);
        CollectionAssert.AreEqual(new[] { "step 1 of 3", "decoding, 51%" }, activities.Updates.Select(u => u[(u.LastIndexOf('—') + 2)..]).ToArray());
    }

    [TestMethod]
    public async Task AHostThatCannotFilm_FailsAsTheProvider()
    {
        var activities = new FakeActivityManager();
        var host = new FakeAiHost { Reply = (r, _) => [new Failed(r.RequestId, HostErrorKind.Refused, "no GPU has room for its decoder")] };

        var ex = await Assert.ThrowsExactlyAsync<LlmProviderException>(() =>
            Provider(FastWan, Here(), host, activities).GenerateVideoAsync(new VideoGenerationRequest("a fox", new MediaAspect(1280, 704), 5)));

        StringAssert.Contains(ex.Message, "no GPU has room for its decoder", "where each part runs, and whether it fits, is the host's to say");
        Assert.AreEqual(1, activities.StartedCount);
    }

    [TestMethod]
    public async Task AClipAskedBeforeTheModelIsHere_IsRefused_BeforeTheHostIsAsked()
    {
        var host = Filming(Frames(1280, 704, 1));
        var store = new FakeModelStore(CudaRuntimePack.Files);

        var ex = await Assert.ThrowsExactlyAsync<LlmProviderException>(() =>
            Provider(FastWan, store, host).GenerateVideoAsync(new VideoGenerationRequest("a fox", new MediaAspect(1280, 704), 5)));

        StringAssert.Contains(ex.Message, "is not on this machine yet");
        Assert.AreEqual(0, host.Requests.Count, "nothing is asked of a host without the model");
        Assert.AreEqual(0, store.Fetched.Count, "a download is the user's to start");
    }
}

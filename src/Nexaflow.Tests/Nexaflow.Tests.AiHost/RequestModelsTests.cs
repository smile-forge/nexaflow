using Nexaflow.AiHost;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Drawing;
using Nexaflow.AiHost.Models;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// What a request's catalog id comes to in the host (<see cref="RequestModels"/>): the parts, sampling, LoRA, voices and tool parts a
/// picture, a clip, a listen, a voice or a tool runs on — each from the real catalog, where it runs on the GPU it finds — and the
/// refusals where no GPU here has room for a part.
/// </summary>
[TestClass]
[CoversNode("local-ai-setup")]
public class RequestModelsTests
{
    private static readonly Func<string, Task> Quiet = _ => Task.CompletedTask;

    /// <summary>The budget the host gives models on one NVIDIA card of <paramref name="gb"/> GB.</summary>
    private static long CardOf(int gb)
        => ModelPlacement.Expected(new MachineCards([new MachineCard("A test card", (long)gb << 30)], CudaRuntimePack.MinimumDriver))[0].BudgetBytes;

    private static (HeldModelsTests.Host Host, RequestModels Requests) On(long budget, string? runtime = null)
    {
        var host = HeldModelsTests.HostWith(budget, runtime);
        return (host, new RequestModels(host.Held));
    }

    private static HostedPart PartOf(string model, string role, string runtime = HostCatalog.Cuda)
        => HostCatalog.Find(model)!.PartsFor(runtime).First(p => p.Role == role);

    [TestMethod]
    public async Task APicture_IsDrawnWithItsModelsPartsAndSampler_FromTheCatalog()
    {
        var (host, requests) = On(CardOf(24));
        using var _ = host;
        var (model, sampler) = requests.Drawer("z-image-turbo");

        var drawing = await requests.DrawingAsync(model, null, Quiet, default);

        Assert.AreEqual((DrawingMethod.ZImage, 9, 3f, 0f), (drawing.Method, drawing.Steps, drawing.Shift, drawing.Guidance));
        Assert.AreEqual((sampler.Steps, sampler.Shift, sampler.Guidance), (drawing.Steps, drawing.Shift, drawing.Guidance));
        CollectionAssert.AreEquivalent(DrawingParts.Of(DrawingMethod.ZImage).ToList(), drawing.Parts.Keys.ToList());
        foreach (var (role, spec) in drawing.Parts)
            Assert.AreEqual(PartOf("z-image-turbo", role).Runner, spec.Runner, $"the {role} runs as its catalog says");
        StringAssert.StartsWith(drawing.Parts[DrawingParts.Denoiser].Folder, "models/z-image-turbo/", "the store's name for it: where it lies is the host's");
        Assert.AreEqual(0, drawing.Parts[DrawingParts.Denoiser].Device, "on the GPU, which has room for it");
        Assert.IsNull(drawing.Lora);
    }

    [TestMethod]
    public async Task ALora_IsDrawnWithAtItsStrength_FromItsFilesInTheStore_AndADistillationBringsItsOwnSampler()
    {
        var (host, requests) = On(CardOf(24));
        using var _ = host;
        var (model, _) = requests.Drawer("moss-image");

        var anime = await requests.DrawingAsync(model, new DrawingLora("moss-anime", 0.8f), Quiet, default);
        var fast  = await requests.DrawingAsync(model, new DrawingLora("moss-fast-8step", 1f), Quiet, default);

        Assert.AreEqual((DrawingMethod.MossImage, 50, 5f, 3f), (anime.Method, anime.Steps, anime.Shift, anime.Guidance), "drawn with MOSS's own sampler");
        Assert.AreEqual(new LoraFiles("models/moss-image/loras/moss-anime.onnx_adapter", 0.8f, null), anime.Lora);
        Assert.AreEqual(new LoraFiles("models/moss-image/loras/moss-fast-8step.onnx_adapter", 1f, "models/moss-image/loras/moss-fast-8step.sampler.json"),
                        fast.Lora, "the distillation's sampler, in place of the model's own");
    }

    [TestMethod]
    public async Task ALoraTheModelDoesNotHave_OrWhoseFilesAreNotHere_IsNotFound()
    {
        var (host, requests) = On(CardOf(24));
        using var _ = host;
        var (model, _) = requests.Drawer("moss-image");
        host.Store.Missing.Add("models/moss-image/loras/moss-gouache.onnx_adapter");

        var unknown = await Assert.ThrowsExactlyAsync<HostRequestException>(() => requests.DrawingAsync(model, new DrawingLora("no-such-lora", 1f), Quiet, default));
        var absent  = await Assert.ThrowsExactlyAsync<HostRequestException>(() => requests.DrawingAsync(model, new DrawingLora("moss-gouache", 1f), Quiet, default));

        Assert.AreEqual((HostErrorKind.NotFound, HostErrorKind.NotFound), (unknown.Kind, absent.Kind));
        Assert.AreEqual(0, host.Store.Fetched.Count, "a LoRA's files are the app's to ask for");
    }

    [TestMethod]
    public void AModelThatDrawsNothing_IsRefusedADrawing_AndOneTheCatalogDoesNotName_IsNotFound()
    {
        var (host, requests) = On(CardOf(24));
        using var _ = host;

        Assert.AreEqual(HostErrorKind.Invalid, Assert.ThrowsExactly<HostRequestException>(() => requests.Drawer("gemma-4-e4b-it")).Kind);
        Assert.AreEqual(HostErrorKind.NotFound, Assert.ThrowsExactly<HostRequestException>(() => requests.Drawer("no-such-model")).Kind);
    }

    [TestMethod]
    public async Task OnACardTooSmallForAPicturesLargestPart_EvenLighter_ThePictureIsRefused()
    {
        var (host, requests) = On(CardOf(8));
        using var _ = host;
        var (model, _) = requests.Drawer("z-image-turbo");

        var refused = await Assert.ThrowsExactlyAsync<HostRequestException>(() => requests.DrawingAsync(model, null, Quiet, default));

        Assert.AreEqual(HostErrorKind.Refused, refused.Kind);
        StringAssert.Contains(refused.Message, "more than any GPU here has for models");
    }

    [TestMethod]
    public async Task TheDistilledVideoModel_FilmsInItsThreeTimesteps_WithItsMakersNegativePrompt_AndWhatTheClipKeepsOutBesides()
    {
        var (host, requests) = On(CardOf(24));
        using var _ = host;
        var (model, filming) = requests.Filmer("fastwan2.2-ti2v-5b");

        var plain = await requests.FilmingAsync(model, null, Quiet, default);
        var told  = await requests.FilmingAsync(model, "  boats ", Quiet, default);

        Assert.AreEqual((FilmingMethod.WanDistilled, 8f, 0f), (plain.Method, plain.Shift, plain.Guidance));
        CollectionAssert.AreEqual(new[] { 1000, 757, 522 }, plain.Timesteps.ToArray());
        CollectionAssert.AreEquivalent(FilmingParts.Of(FilmingMethod.WanDistilled).ToList(), plain.Parts.Keys.ToList());
        Assert.AreEqual("models/wan2.2-ti2v-5b/denoiser-fast", plain.Parts[FilmingParts.Denoiser].Folder);
        Assert.AreEqual(0, plain.Parts[FilmingParts.Decoder].Device);
        Assert.AreEqual(filming.NegativePrompt, plain.NegativePrompt, "its makers' own");
        Assert.AreEqual($"{filming.NegativePrompt}, boats", told.NegativePrompt, "and the clip's besides");
    }

    [TestMethod]
    public async Task TheFullVideoModel_StartsFromAPicture_GuidedByItsMakersNegativePrompt()
    {
        var (host, requests) = On(CardOf(24));
        using var _ = host;
        var (model, _) = requests.Filmer("wan2.2-ti2v-5b");

        var film = await requests.FilmingAsync(model, null, Quiet, default);

        Assert.AreEqual((FilmingMethod.Wan, 50, 5f, 5f), (film.Method, film.Steps, film.Shift, film.Guidance));
        StringAssert.StartsWith(film.NegativePrompt, "色调艳丽");
        Assert.IsTrue(film.Parts.ContainsKey(FilmingParts.FrameEncoder), "it reads the picture it starts from");
    }

    [TestMethod]
    public async Task OnA10GbCard_AClip_RunsItsPartsLighter_ThePromptEncoderOnTheCpu()
    {
        var (host, requests) = On(CardOf(10));
        using var _ = host;
        var (model, _) = requests.Filmer("fastwan2.2-ti2v-5b");

        var film = await requests.FilmingAsync(model, null, Quiet, default);

        Assert.IsNull(film.Parts[FilmingParts.PromptEncoder].Device, "its CPU build");
        Assert.AreEqual((0, "denoiser.stream.json"), (film.Parts[FilmingParts.Denoiser].Device, film.Parts[FilmingParts.Denoiser].File), "streamed");
        Assert.AreEqual((0, 16), (film.Parts[FilmingParts.Decoder].Device, film.Parts[FilmingParts.Decoder].TileCells), "decoded in tiles");
    }

    [TestMethod]
    public async Task OnACardTooSmallForTheDecoderEvenInTiles_AClipIsRefused_SayingWhatItNeeds()
    {
        var (host, requests) = On(CardOf(8));
        using var _ = host;
        var (model, _) = requests.Filmer("fastwan2.2-ti2v-5b");

        var refused = await Assert.ThrowsExactlyAsync<HostRequestException>(() => requests.FilmingAsync(model, null, Quiet, default));

        Assert.AreEqual(HostErrorKind.Refused, refused.Kind);
        StringAssert.Contains(refused.Message, "needs 7 GB of GPU memory for its decoder");
    }

    [TestMethod]
    public async Task Listening_RunsOnTheSpeechModelsFourParts_TheTranscriberBuiltForTheRuntime()
    {
        var (host, requests) = On(CardOf(24));
        using var _ = host;

        var parts = await requests.ListeningAsync(new Listen(1, "nemotron-whisper-turbo", "en", UtteranceEnd.Turn, RequestPriority.Voice), Quiet, default);

        Assert.AreEqual(("silero-vad", "silero_vad.onnx", (int?)null), (parts.VoiceActivity.Runner, parts.VoiceActivity.File, parts.VoiceActivity.Device),
                        "the voice activity detector, on the CPU");
        Assert.AreEqual("models/nemotron-3.5-asr-streaming", parts.Recognizer.Folder);
        Assert.AreEqual(("models/whisper-large-v3-turbo-cuda", (int?)0), (parts.Transcriber!.Folder, parts.Transcriber.Device), "the CUDA build, on the GPU");
        Assert.AreEqual("1", parts.Transcriber.CudaOptions!["sdpa_kernel"]);
        Assert.AreEqual("smart-turn", parts.TurnDetector!.Runner);

        var (cpuHost, onCpu) = On(CardOf(24), HostCatalog.Cpu);
        using var __ = cpuHost;
        var pause = await onCpu.ListeningAsync(new Listen(2, "nemotron-whisper-turbo", "en", UtteranceEnd.Pause, RequestPriority.Interactive), Quiet, default);
        Assert.AreEqual("models/whisper-large-v3-turbo-cpu", pause.Transcriber!.Folder, "the CPU build, where the host runs on the CPU");
    }

    [TestMethod]
    public async Task Speaking_GivesEachLanguageItsCatalogsVoice_ItsLexiconInItsPartsFolder()
    {
        var (host, requests) = On(CardOf(24));
        using var _ = host;

        var voices = await requests.VoicesAsync("kokoro-82m", Quiet, default);

        var english = voices[0];
        Assert.AreEqual(("en", "kokoro", "onnx/model.onnx", "voices/af_heart.bin", "models/kokoro-82m", (string?)null),
                        (english.Language, english.Model.Runner, english.Model.File, english.VoiceFile, english.Model.Folder, english.Lexicon),
                        "the first voice reads any language none is for");
        var german = voices.Single(v => v.Language == "de");
        Assert.AreEqual("kokoro-82m/german", german.Model.Id, "German has a part of its own");
        Assert.AreEqual($"{german.Model.Folder}/lexicons/de.tsv.gz", german.Lexicon);
    }

    [TestMethod]
    public async Task ATool_RunsTheParts_ItsCatalogNames_WhereTheyRun()
    {
        var (host, _) = On(CardOf(24));
        using var _ = host;
        var held = host.Held;

        var finder = await held.ResolveAsync(held.ToolPart("unlimited-ocr", ReadingParts.TextFinder), Quiet, default);
        var reader = await held.ResolveAsync(held.ToolPart("unlimited-ocr", ReadingParts.Reader), Quiet, default);
        var classifier = await held.ResolveAsync(held.ToolPart("image-safety-classifier-m", CheckingParts.SafetyClassifier), Quiet, default);

        Assert.AreEqual(("text-finder", (int?)null), (finder.Runner, finder.Device), "text is found on the CPU");
        Assert.AreEqual("unlimited-ocr-q8_0.gguf", reader.File);
        Assert.IsNull(classifier.Device, "a picture is checked on the CPU");
        foreach (var role in SelectingParts.All) held.ToolPart("sam3.1", role);
        foreach (var role in RestoringParts.Of(4, faces: true)) held.ToolPart("real-esrgan-gfpgan", role);
        held.ToolPart("depth-anything-v2-small", SceneDepthParts.Estimator);
    }
}

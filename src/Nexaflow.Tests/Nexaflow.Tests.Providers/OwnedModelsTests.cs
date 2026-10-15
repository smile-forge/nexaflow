using System.Text;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Providers;

/// <summary>The catalog of models Nexaflow runs itself.</summary>
[TestClass]
public class OwnedModelsTests
{
    [TestMethod]
    public void TheShippedLanguageModel_SaysHowItIsRun_AndKeepsItsFilesInItsFolder()
    {
        var gemma = OwnedModels.All.Single(m => m.Id == "gemma-4-e4b-it");
        var chat  = gemma.Part(OwnedRoles.Chat, OwnedModels.Cuda);

        Assert.AreEqual((ModelKind.Language, "llama-chat", "models/gemma-4-e4b-it-qat"), (gemma.Kind, chat.Runner, chat.Folder));
        Assert.IsTrue(chat is { MemoryBytes: > 0, ContextTokens: > 0, Adapter: null } && gemma is { Images: true, Audio: true, ToolCalls: true });
        Assert.AreEqual(chat, gemma.Part(OwnedRoles.Chat, OwnedModels.Cpu), "one build runs on both");
        CollectionAssert.AreEquivalent(new[] { $"{chat.Folder}/{chat.File}", $"{chat.Folder}/{chat.Projector}" },
                                       gemma.Catalog.Files.Select(f => f.RelativePath).ToArray(), "the GGUF and the projector it sees and hears through");
    }

    [TestMethod]
    public void TheShippedSpeechModels_AreWhisperTurboSmallAndSmallEnglish_EachWithEveryPartOnEachRuntime_FetchingOnlyTheBuildsForThisOne()
    {
        var speech = OwnedModels.OfKind(ModelKind.SpeechToText).ToList();
        CollectionAssert.AreEqual(new[] { "nemotron-whisper-turbo", "nemotron-whisper-small", "nemotron-whisper-small-en" }, speech.Select(m => m.Id).ToArray(),
                                  "turbo first: a workspace starts on it");

        foreach (var model in speech)
        {
            var whisper = model.Part(OwnedRoles.Transcriber, OwnedModels.Cuda).Folder[..^"-cuda".Length];
            foreach (var runtime in new[] { OwnedModels.Cuda, OwnedModels.Cpu })
                CollectionAssert.AreEquivalent(OwnedRoles.Of(ModelKind.SpeechToText).ToArray(), model.PartsFor(runtime).Select(p => p.Role).ToArray(), model.Id);

            var onCuda = model.CatalogFor(OwnedModels.Cuda).Files.Select(f => f.RelativePath).ToList();
            var onCpu  = model.CatalogFor(OwnedModels.Cpu).Files.Select(f => f.RelativePath).ToList();
            Assert.IsTrue(onCuda.Any(f => f.StartsWith(whisper + "-cuda/")) && !onCuda.Any(f => f.StartsWith(whisper + "-cpu/")), model.Id);
            Assert.IsTrue(onCpu.Any(f => f.StartsWith(whisper + "-cpu/")) && !onCpu.Any(f => f.StartsWith(whisper + "-cuda/")), model.Id);
            CollectionAssert.Contains(onCpu, "models/nemotron-3.5-asr-streaming/silero_vad.onnx", "a part for either runtime is fetched on both");
            Assert.AreEqual("1", model.Part(OwnedRoles.Transcriber, OwnedModels.Cuda).CudaOptions!["sdpa_kernel"]);
            Assert.IsTrue(model.Catalog.Files.Where(f => f.RelativePath.StartsWith(whisper)).All(f => f.Source.AbsoluteUri.Contains("/resolve/") && f.Sha256.Length == 64),
                          "each Whisper build is pinned and hashed");
        }
    }

    [TestMethod]
    public void TheShippedVoices_SpeakEnglishFrenchAndGerman_EachWithItsFilesAndLexicon()
    {
        var kokoro = OwnedModels.OfKind(ModelKind.TextToSpeech).Single();
        var files  = kokoro.Catalog.Files.Select(f => f.RelativePath).ToArray();

        Assert.AreEqual("en", kokoro.Voices[0].Language, "English reads any language none of the voices is for");
        foreach (var language in new[] { "en", "fr", "de" })
        {
            var voice = kokoro.Voices.Single(v => v.Language == language);
            var part  = kokoro.PartNamed(voice.Part, OwnedModels.Cuda);
            CollectionAssert.Contains(files, $"{part.Folder}/{voice.File}");
            if (language != "en") CollectionAssert.Contains(files, $"{part.Folder}/{voice.Lexicon}", $"{language} is read from its lexicon");
        }
        Assert.AreEqual("models/kokoro-82m-german", kokoro.PartNamed(kokoro.Voices.Single(v => v.Language == "de").Part, OwnedModels.Cpu).Folder,
                        "German is spoken by its own fine-tune");
    }

    [TestMethod]
    public void TheShippedImageModel_IsZImageTurbo_BuiltForCuda_WithItsSamplerAndTokenizer()
    {
        var image = OwnedModels.OfKind(ModelKind.ImageGeneration).Single(m => m.Id == "z-image-turbo");

        Assert.AreEqual((DrawingMethod.ZImage, 9, 3f, 0f), (image.Sampler!.Method, image.Sampler.Steps, image.Sampler.Shift, image.Sampler.Guidance));
        Assert.AreEqual((1344, 768), image.Sampler.Sizes[MediaShape.Landscape]);
        Assert.AreEqual(ModelTier.Standard, image.Catalog.Tier);
        Assert.IsTrue(image.RunsOn(OwnedModels.Cuda));
        Assert.IsFalse(image.RunsOn(OwnedModels.Cpu), "its fp16 graphs are built for the GPU");
        CollectionAssert.AreEquivalent(new[] { "z-image-prompt-encoder", "z-image-denoiser", "z-image-decoder" },
                                       image.PartsFor(OwnedModels.Cuda).Select(p => p.Runner).ToArray());
        Assert.IsTrue(image.Parts.All(p => p.MemoryBytes > 0), "each part's peak is what admission counts");
        var files = image.Catalog.Files.Select(f => f.RelativePath).ToArray();
        CollectionAssert.Contains(files, "models/z-image-turbo/encoder/tokenizer.json");
        Assert.IsTrue(image.Catalog.Files.All(f => f.Source.ToString().Contains("smile-forge/z-image-turbo-onnx/resolve/4e7bc3c4")),
                      "every file from the one published revision");
    }

    [TestMethod]
    public void TheShippedHighQualityImageModel_IsHiDreamO1_ItsDenoiserAlone_BuiltForCuda()
    {
        var image = OwnedModels.OfKind(ModelKind.ImageGeneration).Single(m => m.Id == "hidream-o1-image");

        Assert.AreEqual(ModelTier.HighQuality, image.Catalog.Tier);
        Assert.AreEqual((DrawingMethod.HiDream, 50, 3f, 5f), (image.Sampler!.Method, image.Sampler.Steps, image.Sampler.Shift, image.Sampler.Guidance));
        Assert.AreEqual((2048, 2048), image.Sampler.Sizes[MediaShape.Square]);
        Assert.AreEqual((2560, 1440), image.Sampler.Sizes[MediaShape.Landscape]);
        Assert.IsTrue(image.RunsOn(OwnedModels.Cuda));
        Assert.IsFalse(image.RunsOn(OwnedModels.Cpu), "its graph is built for the GPU");
        Assert.AreEqual("hidream-denoiser", image.Part(OwnedRoles.Denoiser, OwnedModels.Cuda).Runner);
        var files = image.Catalog.Files.Select(f => f.RelativePath).ToArray();
        CollectionAssert.Contains(files, "models/hidream-o1-image/denoiser/tokenizer.json", "the host reads the prompt with it");
        Assert.IsTrue(image.Catalog.Files.All(f => f.Source.AbsoluteUri.Contains("/smile-forge/hidream-o1-image-onnx/resolve/1749a0c0")),
                      "every file pinned to the published revision");
    }

    [TestMethod]
    public void HiDreamO1_AlsoShipsWithFp16Weights_ForAGpuWithTheRoom_AfterThe8BitBuild()
    {
        var images = OwnedModels.OfKind(ModelKind.ImageGeneration).Select(m => m.Id).ToList();
        var eight  = OwnedModels.All.Single(m => m.Id == "hidream-o1-image");
        var full   = OwnedModels.All.Single(m => m.Id == "hidream-o1-image-fp16");

        Assert.IsTrue(images.IndexOf("hidream-o1-image") < images.IndexOf("hidream-o1-image-fp16"), "the 8-bit build is offered first, the default");
        Assert.AreEqual(ModelTier.HighQuality, full.Catalog.Tier);
        Assert.AreEqual((full.Sampler!.Method, full.Sampler.Steps, full.Sampler.Guidance), (eight.Sampler!.Method, eight.Sampler.Steps, eight.Sampler.Guidance));
        Assert.IsTrue(full.Part(OwnedRoles.Denoiser, OwnedModels.Cuda).MemoryBytes > eight.Part(OwnedRoles.Denoiser, OwnedModels.Cuda).MemoryBytes,
                      "it needs more room, so it is offered only where a GPU has it");
        CollectionAssert.Contains(full.Catalog.Files.Select(f => f.RelativePath).ToArray(), "models/hidream-o1-image/denoiser-fp16/tokenizer.json");
    }

    private const string Hash = "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002";

    private static string File(string path) => $$"""{ "path": "{{path}}", "url": "https://h/w", "size": 1, "sha256": "{{Hash}}" }""";

    private const string Chat = """{ "role": "chat", "runner": "llama-chat", "folder": "models/m", "memoryBytes": 1, "contextTokens": 1 }""";

    private const string Image = """
        { "role": "promptEncoder", "runner": "e", "folder": "models/m", "memoryBytes": 1, "executionProvider": "cuda" },
        { "role": "denoiser", "runner": "d", "folder": "models/d", "memoryBytes": 1, "executionProvider": "cuda" },
        { "role": "decoder", "runner": "v", "folder": "models/v", "memoryBytes": 1, "executionProvider": "cuda" }
        """;

    private const string Sampler = """{ "method": "zImage", "steps": 9, "shift": 3, "guidance": 0, "sizes": { "square": [1024, 1024], "landscape": [1344, 768], "portrait": [768, 1344] } }""";

    [TestMethod]
    [DataRow("""{ "id": "m", "parts": [CHAT], "files": [IN] }""", DisplayName = "no kind")]
    [DataRow("""{ "id": "m", "kind": "language", "parts": [], "files": [IN] }""", DisplayName = "no part")]
    [DataRow("""{ "id": "m", "kind": "language", "parts": [{ "role": "chat", "folder": "models/m", "memoryBytes": 1, "contextTokens": 1 }], "files": [IN] }""", DisplayName = "no runner")]
    [DataRow("""{ "id": "m", "kind": "language", "parts": [{ "role": "chat", "runner": "llama-chat", "folder": "models/m" }], "files": [IN] }""", DisplayName = "no budget")]
    [DataRow("""{ "id": "m", "kind": "language", "parts": [CHAT], "files": [IN, OUT] }""", DisplayName = "a file astray")]
    [DataRow("""{ "id": "m", "kind": "language", "parts": [CHAT, { "role": "chat", "runner": "llama-chat", "folder": "models/n", "memoryBytes": 1, "contextTokens": 1 }], "files": [IN] }""", DisplayName = "two of a part")]
    [DataRow("""{ "id": "m", "kind": "speechToText", "parts": [{ "role": "recognizer", "runner": "r", "folder": "models/m" }], "files": [IN] }""", DisplayName = "parts missing")]
    [DataRow("""{ "id": "m", "kind": "imageGeneration", "parts": [IMAGE], "files": [IN, IN2, IN3] }""", DisplayName = "an image model that does not say how it samples")]
    [DataRow("""{ "id": "m", "kind": "imageGeneration", "sampler": SAMPLER, "parts": [{ "role": "promptEncoder", "runner": "e", "folder": "models/m" }, { "role": "denoiser", "runner": "d", "folder": "models/d", "memoryBytes": 1 }, { "role": "decoder", "runner": "v", "folder": "models/v", "memoryBytes": 1 }], "files": [IN, IN2, IN3] }""", DisplayName = "an image part without its memory")]
    [DataRow("""{ "id": "m", "kind": "imageGeneration", "sampler": { "steps": 9, "shift": 3, "sizes": { "square": [1024, 1024], "landscape": [1344, 768], "portrait": [768, 1344] } }, "parts": [IMAGE], "files": [IN, IN2, IN3] }""", DisplayName = "an image model that does not say how it draws")]
    [DataRow("""{ "id": "m", "kind": "imageGeneration", "sampler": { "method": "zImage", "steps": 9, "shift": 3, "sizes": { "square": [1024, 1024] } }, "parts": [IMAGE], "files": [IN, IN2, IN3] }""", DisplayName = "an image model with a shape it gives no size")]
    [DataRow("""{ "id": "m", "kind": "imageGeneration", "sampler": { "method": "hiDream", "steps": 50, "shift": 3, "guidance": 5, "sizes": { "square": [2048, 2048], "landscape": [2560, 1440], "portrait": [1440, 2560] } }, "parts": [IMAGE], "files": [IN, IN2, IN3] }""", DisplayName = "an image model with parts its method does not draw with")]
    [DataRow("""{ "id": "m", "kind": "language", "parts": [{ "role": "chat", "runner": "llama-chat", "folder": "models/m", "memoryBytes": 1, "contextTokens": 1, "executionProvider": "npu" }], "files": [IN] }""", DisplayName = "a runtime the host lacks")]
    [DataRow("""{ "id": "m", "kind": "textToSpeech", "parts": [{ "role": "synthesizer", "runner": "kokoro", "folder": "models/m" }], "files": [IN] }""", DisplayName = "a voice model with no voice")]
    [DataRow("""{ "id": "m", "kind": "language", "images": true, "parts": [CHAT], "files": [IN] }""", DisplayName = "a model that sees with no projector")]
    [DataRow("""{ "id": "m", "kind": "textToSpeech", "parts": [{ "role": "synthesizer", "runner": "kokoro", "folder": "models/m" }], "voices": [{ "language": "en", "part": "synthesizer", "file": "voices/x.bin" }], "files": [IN] }""", DisplayName = "a voice not among its files")]
    [DataRow("""{ "id": "m", "kind": "textToSpeech", "parts": [{ "role": "synthesizer", "runner": "kokoro", "folder": "models/m" }], "voices": [{ "language": "fr", "part": "synthesizer", "file": "w.bin", "lexicon": "fr.tsv.gz" }], "files": [IN] }""", DisplayName = "a lexicon not among its files")]
    [DataRow("""{ "id": "m", "kind": "textToSpeech", "parts": [{ "role": "synthesizer", "runner": "kokoro", "folder": "models/m" }, { "role": "synthesizer", "runner": "kokoro", "folder": "models/m" }], "voices": [{ "language": "en", "part": "synthesizer", "file": "w.bin" }], "files": [IN] }""", DisplayName = "two synthesizers of one name")]
    [DataRow("""{ "id": "m", "kind": "language", "tokenSampling": { "answering": { "temperature": 1, "topK": 20, "topP": 0.95 } }, "parts": [CHAT], "files": [IN] }""", DisplayName = "token sampling with nothing for reasoning")]
    [DataRow("""{ "id": "m", "kind": "language", "tokenSampling": { "thinking": { "temperature": 0, "topK": 20, "topP": 0.95 } }, "parts": [CHAT], "files": [IN] }""", DisplayName = "a temperature no sampler takes")]
    [DataRow("""{ "id": "m", "kind": "language", "tokenSampling": { "thinking": { "temperature": 1, "topK": 20, "topP": 1.5 } }, "parts": [CHAT], "files": [IN] }""", DisplayName = "a top-p past 1")]
    [DataRow("""{ "id": "m", "kind": "imageGeneration", "sampler": SAMPLER, "tokenSampling": { "thinking": { "temperature": 1, "topK": 20, "topP": 0.95 } }, "parts": [IMAGE], "files": [IN, IN2, IN3] }""", DisplayName = "token sampling for a model that writes none")]
    public void AModelThatDoesNotSayWhatItIsOrHowItRuns_OrStraysFromItsFolders_IsRefused(string model)
    {
        var json = $$"""{ "models": [ {{model.Replace("CHAT", Chat).Replace("IMAGE", Image).Replace("SAMPLER", Sampler).Replace("IN2", File("models/d/w.bin")).Replace("IN3", File("models/v/w.bin"))
                                           .Replace("IN", File("models/m/w.bin")).Replace("OUT", File("elsewhere/w.bin"))}} ] }""";

        Assert.ThrowsExactly<InvalidDataException>(() => OwnedModels.Read(Encoding.UTF8.GetBytes(json)));
    }

    [TestMethod]
    public void AModel_MayBeBuiltForCudaAlone_AndThenDoesNotRunOnTheCpu()
    {
        var json = $$"""
            { "models": [ { "id": "m", "kind": "imageGeneration", "sampler": {{Sampler}}, "parts": [ {{Image}} ],
                "files": [ {{File("models/m/e.onnx")}}, {{File("models/d/d.onnx")}}, {{File("models/v/v.onnx")}} ] } ] }
            """;

        var model = OwnedModels.Read(Encoding.UTF8.GetBytes(json)).Single();

        Assert.IsTrue(model.RunsOn(OwnedModels.Cuda));
        Assert.IsFalse(model.RunsOn(OwnedModels.Cpu));
        Assert.AreEqual((DrawingMethod.ZImage, 9, 3f, 0f), (model.Sampler!.Method, model.Sampler.Steps, model.Sampler.Shift, model.Sampler.Guidance));
        CollectionAssert.AreEquivalent(new[] { (1024, 1024), (1344, 768), (768, 1344) }, model.Sampler.Sizes.Values.ToArray());
        Assert.AreEqual(0, model.CatalogFor(OwnedModels.Cpu).Files.Count, "nothing is fetched for a runtime it has no build for");
    }

    [TestMethod]
    public void AModelDrawnTheHiDreamWay_IsItsDenoiserAlone_WithItsGuidanceAndTier()
    {
        var json = $$"""
            { "models": [ { "id": "h", "kind": "imageGeneration", "tier": "highQuality",
                "sampler": { "method": "hiDream", "steps": 50, "shift": 3, "guidance": 5,
                             "sizes": { "square": [2048, 2048], "landscape": [2560, 1440], "portrait": [1440, 2560] } },
                "parts": [ { "role": "denoiser", "runner": "hidream-denoiser", "folder": "models/h", "memoryBytes": 1, "executionProvider": "cuda" } ],
                "files": [ {{File("models/h/denoiser.onnx")}} ] } ] }
            """;

        var model = OwnedModels.Read(Encoding.UTF8.GetBytes(json)).Single();

        Assert.AreEqual(ModelTier.HighQuality, model.Catalog.Tier);
        Assert.AreEqual((DrawingMethod.HiDream, 50, 3f, 5f), (model.Sampler!.Method, model.Sampler.Steps, model.Sampler.Shift, model.Sampler.Guidance));
        Assert.AreEqual((2560, 1440), model.Sampler.Sizes[MediaShape.Landscape]);
        CollectionAssert.AreEqual(new[] { OwnedRoles.Denoiser }, model.PartsFor(OwnedModels.Cuda).Select(p => p.Role).ToArray());
    }

    [TestMethod]
    public void AVoiceModel_MayHaveASynthesizerForEachSetOfVoices_EachVoiceNamingItsPartAndLexicon()
    {
        var json = $$"""
            { "models": [ { "id": "m", "kind": "textToSpeech",
                "parts": [ { "role": "synthesizer", "runner": "kokoro", "folder": "models/m" },
                           { "role": "synthesizer", "name": "german", "runner": "kokoro", "folder": "models/de" } ],
                "voices": [ { "language": "fr", "part": "synthesizer", "file": "voices/ff.bin", "lexicon": "lexicons/fr.tsv.gz" },
                            { "language": "de", "part": "german", "file": "voices/df.bin", "lexicon": "lexicons/de.tsv.gz" } ],
                "files": [ {{File("models/m/voices/ff.bin")}}, {{File("models/m/lexicons/fr.tsv.gz")}},
                           {{File("models/de/voices/df.bin")}}, {{File("models/de/lexicons/de.tsv.gz")}} ] } ] }
            """;

        var model = OwnedModels.Read(Encoding.UTF8.GetBytes(json)).Single();

        var german = model.Voices.Single(v => v.Language == "de");
        Assert.AreEqual(("german", "lexicons/de.tsv.gz"), (german.Part, german.Lexicon));
        Assert.AreEqual("models/de", model.PartNamed("german", OwnedModels.Cuda).Folder);
        Assert.AreEqual("synthesizer", model.PartNamed("synthesizer", OwnedModels.Cpu).Name, "a part's name is its role unless it is given one");
    }

    [TestMethod]
    public void EachLanguageModel_ChoosesItsTokensAsItsMakersRecommend_ForReasoningAndForAnsweringStraightAway()
    {
        TokenSampling? Of(string id, bool thinks) => OwnedModels.All.Single(m => m.Id == id).TokenSampling?.For(thinks);

        foreach (var gemma in new[] { "gemma-4-e4b-it", "gemma-4-26b-a4b-it", "gemma-4-31b-it" })
        {
            Assert.AreEqual(new TokenSampling(1.0f, 64, 0.95f), Of(gemma, thinks: true), gemma);
            Assert.AreEqual(Of(gemma, thinks: true), Of(gemma, thinks: false), "Google recommends the one way");
        }
        Assert.AreEqual(new TokenSampling(1.0f, 20, 0.95f, 1.5f), Of("qwen-3.5-9b", thinks: true));
        Assert.AreEqual(new TokenSampling(0.7f, 20, 0.8f, 1.5f), Of("qwen-3.5-9b", thinks: false), "Qwen's non-thinking settings");
        Assert.AreEqual(new TokenSampling(1.0f, 20, 0.95f), Of("qwen-3.8-27b", thinks: true));
        Assert.AreEqual(new TokenSampling(0.6f, 20, 0.95f), Of("mimo-v2.6-9b", thinks: false));
        Assert.IsNull(Of("phi-4", thinks: true), "its makers recommend nothing: the likeliest token each time");
    }
}

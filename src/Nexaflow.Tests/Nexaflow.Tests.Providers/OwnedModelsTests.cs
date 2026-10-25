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

    [TestMethod]
    public void MossImage_ShipsAsAHighQualityModel_ReadingItsPromptWithAnExistingGguf_WithItsCuratedLoras()
    {
        var moss = OwnedModels.OfKind(ModelKind.ImageGeneration).Single(m => m.Id == "moss-image");

        Assert.AreEqual(ModelTier.HighQuality, moss.Catalog.Tier);
        Assert.AreEqual((DrawingMethod.MossImage, 50, 5f, 3f, true),
                        (moss.Sampler!.Method, moss.Sampler.Steps, moss.Sampler.Shift, moss.Sampler.Guidance, moss.Sampler.NegativePrompts),
                        "MossImagePipeline's own sampling, steering from a negative prompt");
        Assert.AreEqual((1344, 768), moss.Sampler.Sizes[MediaShape.Landscape]);
        Assert.IsFalse(moss.RunsOn(OwnedModels.Cpu), "its graphs are built for the GPU");
        CollectionAssert.AreEquivalent(new[] { "moss-image-prompt-encoder", "moss-image-denoiser", "moss-image-decoder" },
                                       moss.PartsFor(OwnedModels.Cuda).Select(p => p.Runner).ToArray());
        Assert.IsTrue(moss.Parts.Max(p => p.MemoryBytes) < 11L << 30, "its largest part, the denoiser with a LoRA, fits a 12 GB card");

        var gguf = moss.Catalog.Files.Single(f => f.RelativePath.EndsWith(".gguf", StringComparison.Ordinal));
        Assert.AreEqual("models/moss-image/encoder/Qwen3.5-9B-Q8_0.gguf", gguf.RelativePath);
        StringAssert.Contains(gguf.Source.AbsoluteUri, "/unsloth/Qwen3.5-9B-GGUF/resolve/3885219b", "Qwen3.5-9B unchanged, pinned where the recipe compared it");
        Assert.IsTrue(moss.Catalog.Files.Where(f => f != gguf).All(f => f.Source.AbsoluteUri.Contains("/smile-forge/moss-image-onnx/resolve/12f40c02")),
                      "every graph pinned to the published revision");

        CollectionAssert.AreEqual(new[] { "moss-fast-8step", "moss-anime", "moss-gouache", "moss-psychedelic", "moss-beauty", "moss-nsfw", "moss-animated", "moss-impressionism" },
                                  moss.Loras.Select(l => l.Offer.Id).ToArray());
        var fast = moss.Loras[0];
        Assert.IsNotNull(fast.Sampler, "the distillation brings its own sampler");
        Assert.IsFalse(fast.Offer.NegativePrompts, "and draws in its own steps, with no negative prompt");
        Assert.IsTrue(moss.Loras.Skip(1).All(l => l.Sampler is null && l.Offer.NegativePrompts), "a style is drawn with the model's own sampler");
        CollectionAssert.AreEqual(new[] { "moss-nsfw" }, moss.Loras.Where(l => l.Offer.Adult).Select(l => l.Offer.Id).ToArray(), "one meant for adults only");
        Assert.AreEqual("moss_seinen_style, a fox", moss.Loras.Single(l => l.Offer.Id == "moss-anime").Prompted("a fox"), "its trigger leads the prompt");
        Assert.AreEqual("a fox", moss.Loras.Single(l => l.Offer.Id == "moss-impressionism").Prompted("a fox"), "one with no trigger leaves it as it is");
        Assert.AreEqual(0.7f, moss.Loras.Single(l => l.Offer.Id == "moss-nsfw").Strength, "at the strength its makers recommend");

        var loraFiles = moss.Loras.SelectMany(l => l.Files).ToList();
        Assert.IsTrue(loraFiles.All(f => f.Source.AbsoluteUri.Contains("/smile-forge/moss-image-onnx/resolve/12f40c02") && f.Source.AbsoluteUri.Contains("/loras/")));
        Assert.IsFalse(loraFiles.Any(moss.Catalog.Files.Contains), "a LoRA is fetched when a picture first asks for it, not with the model");
    }

    [TestMethod]
    public void TheOtherShippedImageModels_TakeNoNegativePromptAndNoLora()
    {
        foreach (var image in OwnedModels.OfKind(ModelKind.ImageGeneration).Where(m => m.Id != "moss-image"))
            Assert.AreEqual((false, 0), (image.Sampler!.NegativePrompts, image.Loras.Count), image.Id);
    }

    private const string MossSampler = """{ "method": "mossImage", "steps": 50, "shift": 5, "guidance": 3, "negativePrompts": true, "sizes": { "square": [1024, 1024], "landscape": [1344, 768], "portrait": [768, 1344] } }""";

    private static string LoraFile(string path) => $$"""{ "path": "{{path}}", "url": "https://h/l", "size": 1, "sha256": "{{Hash}}" }""";

    [TestMethod]
    public void ALora_IsReadWithItsFilesTriggerStrengthAndSampler()
    {
        var json = $$"""
            { "models": [ { "id": "m", "kind": "imageGeneration", "sampler": {{MossSampler}}, "parts": [ {{Image}} ],
                "files": [ {{File("models/m/e.gguf")}}, {{File("models/d/d.onnx")}}, {{File("models/v/v.onnx")}} ],
                "loras": [
                  { "id": "fast", "displayName": "Fast", "description": "In eight steps.", "strength": 1.0, "adapter": "models/l/fast.onnx_adapter",
                    "sampler": "models/l/fast.sampler.json", "files": [ {{LoraFile("models/l/fast.onnx_adapter")}}, {{LoraFile("models/l/fast.sampler.json")}} ] },
                  { "id": "ink", "displayName": "Ink", "description": "Ink and wash.", "trigger": "ink_style", "strength": 0.8, "adult": true,
                    "adapter": "models/l/ink.onnx_adapter", "files": [ {{LoraFile("models/l/ink.onnx_adapter")}} ] } ] } ] }
            """;

        var model = OwnedModels.Read(Encoding.UTF8.GetBytes(json)).Single();

        var (fast, ink) = (model.Loras[0], model.Loras[1]);
        Assert.AreEqual(("fast", "Fast", "In eight steps.", false, false), (fast.Offer.Id, fast.Offer.Name, fast.Offer.Description, fast.Offer.Adult, fast.Offer.NegativePrompts));
        Assert.AreEqual(("models/l/fast.onnx_adapter", "models/l/fast.sampler.json"), (fast.Adapter.RelativePath, fast.Sampler!.RelativePath));
        Assert.AreEqual(("ink_style", 0.8f, true, true, (ModelAsset?)null), (ink.Trigger, ink.Strength, ink.Offer.Adult, ink.Offer.NegativePrompts, ink.Sampler));
        Assert.AreEqual(3, model.Catalog.Files.Count, "a LoRA's files are its own, not the model's");
    }

    [TestMethod]
    [DataRow("""{ "id": "a", "displayName": "A", "description": "d", "strength": 1, "adapter": "models/l/a.onnx_adapter", "files": [ LORA_B ] }""", DisplayName = "an adapter not among its files")]
    [DataRow("""{ "id": "a", "displayName": "A", "description": "d", "strength": 1, "adapter": "models/l/a.onnx_adapter", "files": [ LORA_A, LORA_B ] }""", DisplayName = "a file that is neither adapter nor sampler")]
    [DataRow("""{ "id": "a", "displayName": "A", "description": "d", "strength": 1, "adapter": "models/l/a.onnx_adapter", "sampler": "models/l/b", "files": [ LORA_A ] }""", DisplayName = "a sampler not among its files")]
    [DataRow("""{ "id": "a", "displayName": "A", "strength": 1, "adapter": "models/l/a.onnx_adapter", "files": [ LORA_A ] }""", DisplayName = "nothing said of what it does")]
    [DataRow("""{ "id": "a", "displayName": "A", "description": "d", "strength": 0, "adapter": "models/l/a.onnx_adapter", "files": [ LORA_A ] }""", DisplayName = "no strength")]
    [DataRow("""{ "id": "a", "displayName": "A", "description": "d", "strength": 1, "adapter": "models/l/a.onnx_adapter", "files": [ LORA_A ] }, { "id": "a", "displayName": "B", "description": "d", "strength": 1, "adapter": "models/l/a.onnx_adapter", "files": [ LORA_A ] }""", DisplayName = "two of one id")]
    public void ALoraThatDoesNotSayWhatItIsOrWhichFileIsWhich_IsRefused(string loras)
    {
        var json = $$"""
            { "models": [ { "id": "m", "kind": "imageGeneration", "sampler": {{MossSampler}}, "parts": [ {{Image}} ],
                "files": [ {{File("models/m/e.gguf")}}, {{File("models/d/d.onnx")}}, {{File("models/v/v.onnx")}} ],
                "loras": [ {{loras.Replace("LORA_A", LoraFile("models/l/a.onnx_adapter")).Replace("LORA_B", LoraFile("models/l/b"))}} ] } ] }
            """;

        Assert.ThrowsExactly<InvalidDataException>(() => OwnedModels.Read(Encoding.UTF8.GetBytes(json)));
    }

    [TestMethod]
    public void LorasDrawnInTheModelsOwnSteps_AreRefused_ForAModelThatTakesNoNegativePrompt_AsAreLorasForAModelThatDoesNotDraw()
    {
        const string style = """{ "id": "a", "displayName": "A", "description": "d", "strength": 1, "adapter": "models/l/a.onnx_adapter", "files": [ LORA ] }""";
        var unsteered = $$"""
            { "models": [ { "id": "m", "kind": "imageGeneration", "sampler": {{Sampler}}, "parts": [ {{Image}} ],
                "files": [ {{File("models/m/e.onnx")}}, {{File("models/d/d.onnx")}}, {{File("models/v/v.onnx")}} ],
                "loras": [ {{style.Replace("LORA", LoraFile("models/l/a.onnx_adapter"))}} ] } ] }
            """;
        var talker = $$"""
            { "models": [ { "id": "m", "kind": "language", "parts": [ { "role": "chat", "runner": "llama-chat", "folder": "models/m", "memoryBytes": 1, "bytesPerToken": 1, "contextTokens": 8192 } ], "files": [ {{File("models/m/w.bin")}} ],
                "loras": [ {{style.Replace("LORA", LoraFile("models/l/a.onnx_adapter"))}} ] } ] }
            """;

        StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => OwnedModels.Read(Encoding.UTF8.GetBytes(unsteered))).Message, "no negative prompt");
        StringAssert.Contains(Assert.ThrowsExactly<InvalidDataException>(() => OwnedModels.Read(Encoding.UTF8.GetBytes(talker))).Message, "only a model that draws");
    }

    [TestMethod]
    public void TheShippedVideoModels_AreFastWanFirst_FromADescription_ThenWan22_FromAPictureToo()
    {
        var videos = OwnedModels.OfKind(ModelKind.VideoGeneration).Select(m => m.Id).ToList();
        var fast   = OwnedModels.All.Single(m => m.Id == "fastwan2.2-ti2v-5b");
        var full   = OwnedModels.All.Single(m => m.Id == "wan2.2-ti2v-5b");

        Assert.IsTrue(videos.IndexOf(fast.Id) < videos.IndexOf(full.Id), "FastWan is offered first, the default");
        Assert.AreEqual(ModelTier.Standard, fast.Catalog.Tier);
        Assert.AreEqual(ModelTier.HighQuality, full.Catalog.Tier);
        Assert.AreEqual((FilmingMethod.WanDistilled, 3, 8f, 0f, false),
                        (fast.Filming!.Method, fast.Filming.Steps, fast.Filming.Shift, fast.Filming.Guidance, fast.Filming.StartsFromPictures),
                        "distilled from text alone, at the shift it was trained with, unguided");
        CollectionAssert.AreEqual(new[] { 1000, 757, 522 }, fast.Filming.Timesteps.ToArray());
        Assert.AreEqual((FilmingMethod.Wan, 50, 5f, 5f, true),
                        (full.Filming!.Method, full.Filming.Steps, full.Filming.Shift, full.Filming.Guidance, full.Filming.StartsFromPictures));
        StringAssert.StartsWith(full.Filming.NegativePrompt, "色调艳丽", "it steers from Wan's own negative prompt");

        foreach (var model in new[] { fast, full })
        {
            Assert.AreEqual(121, model.Filming!.Frames(model.Filming.Seconds.Single()), "five seconds at 24 frames a second");
            Assert.AreEqual((1280, 704), model.Filming.Sizes[MediaShape.Landscape]);
            Assert.AreEqual((704, 1280), model.Filming.Sizes[MediaShape.Portrait]);
            Assert.IsFalse(model.RunsOn(OwnedModels.Cpu), "its graphs are built for the GPU");
            Assert.IsTrue(model.Parts.Max(p => p.MemoryBytes) < 24L << 30, "its largest part, the decoder, fits a 24 GB card");
            Assert.IsTrue(model.Catalog.Files.All(f => f.Source.AbsoluteUri.Contains("/smile-forge/wan2.2-ti2v-5b-onnx/resolve/3d2b1386")),
                          "every file pinned to the published revision");
        }
        CollectionAssert.AreEquivalent(new[] { "wan-prompt-encoder", "wan-denoiser", "wan-decoder" },
                                       fast.PartsFor(OwnedModels.Cuda).Select(p => p.Runner).ToArray());
        CollectionAssert.AreEquivalent(new[] { "wan-prompt-encoder", "wan-denoiser", "wan-decoder", "wan-frame-encoder" },
                                       full.PartsFor(OwnedModels.Cuda).Select(p => p.Runner).ToArray());

        var shared = fast.Catalog.Files.Select(f => f.RelativePath).Intersect(full.Catalog.Files.Select(f => f.RelativePath)).ToArray();
        CollectionAssert.Contains(shared, "models/wan2.2-ti2v-5b/prompt-encoder/spiece.model", "the host reads the prompt with it");
        CollectionAssert.Contains(shared, "models/wan2.2-ti2v-5b/decoder/caches.json", "the host shapes the decoder's cache by it");
        Assert.IsFalse(shared.Any(p => p.Contains("/denoiser")), "only the denoiser is each build's own; everything else is downloaded once");
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

    private const string Clip = """
        { "role": "promptEncoder", "runner": "wan-prompt-encoder", "folder": "models/w/p", "memoryBytes": 1, "executionProvider": "cuda" },
        { "role": "denoiser", "runner": "wan-denoiser", "folder": "models/w/d", "memoryBytes": 1, "executionProvider": "cuda" },
        { "role": "decoder", "runner": "wan-decoder", "folder": "models/w/v", "memoryBytes": 1, "executionProvider": "cuda" }
        """;

    private const string ClipFrame = """{ "role": "frameEncoder", "runner": "wan-frame-encoder", "folder": "models/w/f", "memoryBytes": 1, "executionProvider": "cuda" }""";

    private static string ClipFiles(bool frame) => string.Join(", ", new[] { "p", "d", "v" }.Concat(frame ? ["f"] : []).Select(f => File($"models/w/{f}/g.onnx")));

    private const string ClipSizes = """{ "landscape": [1280, 704], "portrait": [704, 1280] }""";

    [TestMethod]
    public void AVideoModel_SaysHowItFilms_ADistilledOneByItsTimesteps_AGuidedOneFromItsMakersNegativePrompt()
    {
        var json = $$"""
            { "models": [
              { "id": "fast", "kind": "videoGeneration",
                "filming": { "method": "wanDistilled", "steps": 3, "shift": 5, "guidance": 0, "timesteps": [1000, 757, 522],
                             "framesPerSecond": 24, "seconds": [5], "sizes": {{ClipSizes}} },
                "parts": [ {{Clip}} ], "files": [ {{ClipFiles(frame: false)}} ] },
              { "id": "full", "kind": "videoGeneration", "tier": "highQuality",
                "filming": { "method": "wan", "steps": 50, "shift": 5, "guidance": 5, "negativePrompt": "静态",
                             "framesPerSecond": 24, "seconds": [5], "sizes": {{ClipSizes}}, "startsFromPictures": true },
                "parts": [ {{Clip}}, {{ClipFrame}} ], "files": [ {{ClipFiles(frame: true)}} ] } ] }
            """;

        var models = OwnedModels.Read(Encoding.UTF8.GetBytes(json));
        var fast = models.Single(m => m.Id == "fast").Filming!;
        var full = models.Single(m => m.Id == "full");

        Assert.AreEqual((FilmingMethod.WanDistilled, 0f, false), (fast.Method, fast.Guidance, fast.StartsFromPictures), "distilled from text, it takes no picture");
        CollectionAssert.AreEqual(new[] { 1000, 757, 522 }, fast.Timesteps.ToArray());
        Assert.AreEqual(121, fast.Frames(5), "the first frame, then 24 a second");
        Assert.AreEqual((1280, 704), fast.Sizes[MediaShape.Landscape]);
        Assert.AreEqual((FilmingMethod.Wan, 50, 5f, "静态", true), (full.Filming!.Method, full.Filming.Steps, full.Filming.Guidance, full.Filming.NegativePrompt, full.Filming.StartsFromPictures));
        Assert.AreEqual(ModelTier.HighQuality, full.Catalog.Tier);
        CollectionAssert.AreEquivalent(FilmingParts.Of(FilmingMethod.Wan).ToArray(), full.PartsFor(OwnedModels.Cuda).Select(p => p.Role).ToArray());
        Assert.IsTrue(full.LoadsOnDemand, "a clip's parts load for it, as a picture's do");
    }

    [TestMethod]
    [DataRow("""{ "id": "w", "kind": "videoGeneration", "parts": [CLIP], "files": [FILES] }""", DisplayName = "a video model that does not say how it films")]
    [DataRow("""{ "id": "w", "kind": "imageGeneration", "sampler": SAMPLER, "filming": DISTILLED, "parts": [CLIP], "files": [FILES] }""", DisplayName = "an image model that films")]
    [DataRow("""{ "id": "w", "kind": "videoGeneration", "filming": { "method": "wanDistilled", "steps": 3, "shift": 5, "guidance": 0, "timesteps": [1000, 757], "framesPerSecond": 24, "seconds": [5], "sizes": SIZES }, "parts": [CLIP], "files": [FILES] }""", DisplayName = "a distilled model a timestep short")]
    [DataRow("""{ "id": "w", "kind": "videoGeneration", "filming": { "method": "wanDistilled", "steps": 3, "shift": 5, "guidance": 5, "timesteps": [1000, 757, 522], "framesPerSecond": 24, "seconds": [5], "sizes": SIZES }, "parts": [CLIP], "files": [FILES] }""", DisplayName = "a distilled model that is guided")]
    [DataRow("""{ "id": "w", "kind": "videoGeneration", "filming": { "method": "wan", "steps": 50, "shift": 5, "guidance": 5, "framesPerSecond": 24, "seconds": [5], "sizes": SIZES }, "parts": [CLIP, FRAME], "files": [FILES, FRAMEFILE] }""", DisplayName = "a guided model with no negative prompt")]
    [DataRow("""{ "id": "w", "kind": "videoGeneration", "filming": { "method": "wanDistilled", "steps": 3, "shift": 5, "guidance": 0, "timesteps": [1000, 757, 522], "framesPerSecond": 24, "seconds": [5], "sizes": { "square": [704, 704], "landscape": [1280, 704], "portrait": [704, 1280] } }, "parts": [CLIP], "files": [FILES] }""", DisplayName = "a square clip")]
    [DataRow("""{ "id": "w", "kind": "videoGeneration", "filming": { "method": "wanDistilled", "steps": 3, "shift": 5, "guidance": 0, "timesteps": [1000, 757, 522], "framesPerSecond": 24, "sizes": SIZES }, "parts": [CLIP], "files": [FILES] }""", DisplayName = "no length")]
    [DataRow("""{ "id": "w", "kind": "videoGeneration", "filming": DISTILLED, "parts": [CLIP, FRAME], "files": [FILES, FRAMEFILE] }""", DisplayName = "a distilled model with a frame encoder it does not use")]
    public void AVideoModelThatDoesNotSayHowItFilms_OrFilmsInAWayItCannot_IsRefused(string model)
    {
        const string distilled = """{ "method": "wanDistilled", "steps": 3, "shift": 5, "guidance": 0, "timesteps": [1000, 757, 522], "framesPerSecond": 24, "seconds": [5], "sizes": SIZES }""";
        var json = $$"""{ "models": [ {{model.Replace("DISTILLED", distilled).Replace("SIZES", ClipSizes).Replace("CLIP", Clip).Replace("FRAMEFILE", File("models/w/f/g.onnx"))
                                           .Replace("FRAME", ClipFrame).Replace("FILES", ClipFiles(frame: false)).Replace("SAMPLER", Sampler)}} ] }""";

        Assert.ThrowsExactly<InvalidDataException>(() => OwnedModels.Read(Encoding.UTF8.GetBytes(json)));
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

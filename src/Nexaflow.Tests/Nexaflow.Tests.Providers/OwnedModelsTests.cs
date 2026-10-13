using System.Text;
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

        Assert.AreEqual((ModelKind.Language, "models/gemma-4-e4b-it", "gemma4-onnx-community-q4f16"), (gemma.Kind, chat.Folder, chat.Adapter));
        Assert.IsTrue(chat is { MemoryBytes: > 0, ContextTokens: > 0 } && gemma is { Images: true, Audio: true });
        Assert.AreEqual(chat, gemma.Part(OwnedRoles.Chat, OwnedModels.Cpu), "one build runs on both");
        CollectionAssert.IsSubsetOf(new[] { "models/gemma-4-e4b-it/genai_config.native.json", "models/gemma-4-e4b-it/chat_template.jinja" },
                                    gemma.Catalog.Files.Select(f => f.RelativePath).ToArray(), "what the adapter and the host read");
    }

    [TestMethod]
    public void TheShippedSpeechModel_HasEveryPartOnEachRuntime_AndFetchesOnlyTheBuildsForThisOne()
    {
        var speech = OwnedModels.OfKind(ModelKind.SpeechToText).Single();

        foreach (var runtime in new[] { OwnedModels.Cuda, OwnedModels.Cpu })
            CollectionAssert.AreEquivalent(OwnedRoles.Of(ModelKind.SpeechToText).ToArray(), speech.PartsFor(runtime).Select(p => p.Role).ToArray());

        var onCuda = speech.CatalogFor(OwnedModels.Cuda).Files.Select(f => f.RelativePath).ToList();
        var onCpu  = speech.CatalogFor(OwnedModels.Cpu).Files.Select(f => f.RelativePath).ToList();
        Assert.IsTrue(onCuda.Any(f => f.StartsWith("models/whisper-large-v3-turbo-cuda/")) && !onCuda.Any(f => f.StartsWith("models/whisper-large-v3-turbo-cpu/")));
        Assert.IsTrue(onCpu.Any(f => f.StartsWith("models/whisper-large-v3-turbo-cpu/")) && !onCpu.Any(f => f.StartsWith("models/whisper-large-v3-turbo-cuda/")));
        CollectionAssert.Contains(onCpu, "models/nemotron-3.5-asr-streaming/silero_vad.onnx", "a part for either runtime is fetched on both");
        Assert.AreEqual("1", speech.Part(OwnedRoles.Transcriber, OwnedModels.Cuda).CudaOptions!["sdpa_kernel"]);
    }

    [TestMethod]
    public void TheShippedVoice_HasAnEnglishVoiceAmongItsFiles()
    {
        var kokoro = OwnedModels.OfKind(ModelKind.TextToSpeech).Single();

        var english = kokoro.Voices.Single(v => v.Language == "en");
        Assert.AreEqual(OwnedRoles.Synthesizer, english.Part);
        CollectionAssert.Contains(kokoro.Catalog.Files.Select(f => f.RelativePath).ToArray(), "models/kokoro-82m/" + english.File);
    }

    private const string Hash = "a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002";

    private static string File(string path) => $$"""{ "path": "{{path}}", "url": "https://h/w", "size": 1, "sha256": "{{Hash}}" }""";

    private const string Chat = """{ "role": "chat", "runner": "genai-chat", "folder": "models/m", "memoryBytes": 1, "contextTokens": 1 }""";

    [TestMethod]
    [DataRow("""{ "id": "m", "parts": [CHAT], "files": [IN] }""", DisplayName = "no kind")]
    [DataRow("""{ "id": "m", "kind": "language", "parts": [], "files": [IN] }""", DisplayName = "no part")]
    [DataRow("""{ "id": "m", "kind": "language", "parts": [{ "role": "chat", "folder": "models/m", "memoryBytes": 1, "contextTokens": 1 }], "files": [IN] }""", DisplayName = "no runner")]
    [DataRow("""{ "id": "m", "kind": "language", "parts": [{ "role": "chat", "runner": "genai-chat", "folder": "models/m" }], "files": [IN] }""", DisplayName = "no budget")]
    [DataRow("""{ "id": "m", "kind": "language", "parts": [CHAT], "files": [IN, OUT] }""", DisplayName = "a file astray")]
    [DataRow("""{ "id": "m", "kind": "language", "parts": [CHAT, { "role": "chat", "runner": "genai-chat", "folder": "models/n", "memoryBytes": 1, "contextTokens": 1 }], "files": [IN] }""", DisplayName = "two of a part")]
    [DataRow("""{ "id": "m", "kind": "speechToText", "parts": [{ "role": "recognizer", "runner": "r", "folder": "models/m" }], "files": [IN] }""", DisplayName = "parts missing")]
    [DataRow("""{ "id": "m", "kind": "language", "parts": [{ "role": "chat", "runner": "genai-chat", "folder": "models/m", "memoryBytes": 1, "contextTokens": 1, "executionProvider": "cuda" }], "files": [IN] }""", DisplayName = "none for the CPU")]
    [DataRow("""{ "id": "m", "kind": "language", "parts": [{ "role": "chat", "runner": "genai-chat", "folder": "models/m", "memoryBytes": 1, "contextTokens": 1, "executionProvider": "npu" }], "files": [IN] }""", DisplayName = "a runtime the host lacks")]
    [DataRow("""{ "id": "m", "kind": "textToSpeech", "parts": [{ "role": "synthesizer", "runner": "kokoro", "folder": "models/m" }], "files": [IN] }""", DisplayName = "a voice model with no voice")]
    [DataRow("""{ "id": "m", "kind": "textToSpeech", "parts": [{ "role": "synthesizer", "runner": "kokoro", "folder": "models/m" }], "voices": [{ "language": "en", "part": "synthesizer", "file": "voices/x.bin" }], "files": [IN] }""", DisplayName = "a voice not among its files")]
    [DataRow("""{ "id": "m", "kind": "textToSpeech", "parts": [{ "role": "synthesizer", "runner": "kokoro", "folder": "models/m" }], "voices": [{ "language": "fr", "part": "synthesizer", "file": "w.bin", "lexicon": "fr.tsv.gz" }], "files": [IN] }""", DisplayName = "a lexicon not among its files")]
    [DataRow("""{ "id": "m", "kind": "textToSpeech", "parts": [{ "role": "synthesizer", "runner": "kokoro", "folder": "models/m" }, { "role": "synthesizer", "runner": "kokoro", "folder": "models/m" }], "voices": [{ "language": "en", "part": "synthesizer", "file": "w.bin" }], "files": [IN] }""", DisplayName = "two synthesizers of one name")]
    public void AModelThatDoesNotSayWhatItIsOrHowItRuns_OrStraysFromItsFolders_IsRefused(string model)
    {
        var json = $$"""{ "models": [ {{model.Replace("CHAT", Chat).Replace("IN", File("models/m/w.bin")).Replace("OUT", File("elsewhere/w.bin"))}} ] }""";

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
}

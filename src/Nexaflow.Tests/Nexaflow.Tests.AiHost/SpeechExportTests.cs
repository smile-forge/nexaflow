using Nexaflow.AiHost;
using Nexaflow.AiHost.Models;
using Nexaflow.AiHost.Speech;
using Nexaflow.AiHost.Speech.Spectra;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The speech models on ONNX Runtime, without the models: the spectrograms their encoders read, what their exports say of
/// themselves, and their vocabularies read back into text.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class SpeechExportTests
{
    private string _folder = null!;

    [TestInitialize]
    public void Init() => _folder = TestTemp.CreateSubdirectory("nexaflow-speech-").FullName;

    [TestCleanup]
    public void Done() => Directory.Delete(_folder, recursive: true);

    [TestMethod]
    public void APowerSpectrum_IsTheZeroPaddedTransformOfTheWindowedFrame()
    {
        var random = new Random(7);
        var window = SpectralWindows.SymmetricHann(400);
        var frame = Enumerable.Range(0, 400).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();
        var spectrum = new PowerSpectrum(512, window);
        var power = new float[spectrum.Bins];

        spectrum.Compute(frame, power);

        Assert.AreEqual(257, spectrum.Bins);
        foreach (int k in new[] { 0, 1, 37, 128, 256 })
        {
            double re = 0, im = 0;
            for (int n = 0; n < 400; n++)
            {
                re += frame[n] * window[n] * Math.Cos(2 * Math.PI * k * n / 512);
                im -= frame[n] * window[n] * Math.Sin(2 * Math.PI * k * n / 512);
            }
            double expected = re * re + im * im;
            Assert.AreEqual(expected, power[k], expected * 1e-4 + 1e-4, $"bin {k}");
        }
    }

    [TestMethod]
    public void WhisperFeatures_FillTheEncodersWindow_PastTheAudioWithTheirFloor()
    {
        var tone = Enumerable.Range(0, 16_000).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / 16_000.0))).ToArray();
        var features = new WhisperLogMel(128).Compute(tone);

        Assert.AreEqual(128 * WhisperLogMel.Frames, features.Length);
        float peak = features.Max();
        float floor = features[^1];
        Assert.AreEqual(peak - 2f, floor, 1e-5, "past the audio, every frame holds 8 below the peak, scaled by a quarter");
        int loudest = Enumerable.Range(0, 128).MaxBy(m => features[m * WhisperLogMel.Frames + 50]);
        Assert.IsTrue(loudest is > 30 and < 60, $"a 1 kHz tone is loudest in the bands around 1 kHz, not band {loudest}");
    }

    [TestMethod]
    public void NemoFeatures_GiveAFrameEveryHop_AndSilenceItsLogGuard()
    {
        var mel = new NemoStreamingMel(mels: 128, fftSize: 512, hop: 160, windowLength: 400, sampleRate: 16_000, preemphasis: 0.97f, logGuard: 5.96046448e-08f);

        var (features, frames) = mel.Process(new float[8960]);

        Assert.AreEqual(56, frames, "a 560 ms chunk is 56 frames");
        Assert.AreEqual(56 * 128, features.Length);
        Assert.IsTrue(features.All(v => Math.Abs(v - MathF.Log(5.96046448e-08f)) < 1e-3), "silence is the log of the guard alone");
    }

    [TestMethod]
    public void NemoFeatures_CarryTheirStateFromChunkToChunk()
    {
        var tone = Enumerable.Range(0, 2 * 8960).Select(i => (float)Math.Sin(2 * Math.PI * 440 * i / 16_000.0)).ToArray();
        var streamed = new NemoStreamingMel(128, 512, 160, 400, 16_000, 0.97f, 5.96046448e-08f);
        streamed.Process(tone.AsSpan(0, 8960));
        var (second, _) = streamed.Process(tone.AsSpan(8960));
        var (fresh, _) = new NemoStreamingMel(128, 512, 160, 400, 16_000, 0.97f, 5.96046448e-08f).Process(tone.AsSpan(8960));

        Assert.IsTrue(second.Zip(fresh).Take(128).Any(p => Math.Abs(p.First - p.Second) > 1e-3),
                      "the second chunk's first frame reads the first chunk's end, not silence");
    }

    [TestMethod]
    public void AWhisperExport_NamesEachLayersKeysAndValues()
    {
        File.WriteAllText(Path.Combine(_folder, WhisperExport.ConfigFile), """
            { "model": { "vocab_size": 51866, "eos_token_id": 50257,
                "encoder": { "filename": "encoder.onnx", "inputs": { "audio_features": "audio_features" },
                             "outputs": { "cross_present_key_names": "present_key_cross_%d", "cross_present_value_names": "present_value_cross_%d" } },
                "decoder": { "filename": "decoder.onnx", "num_hidden_layers": 2,
                             "inputs": { "input_ids": "input_ids", "past_key_names": "past_key_self_%d", "past_value_names": "past_value_self_%d",
                                         "cross_past_key_names": "past_key_cross_%d", "cross_past_value_names": "past_value_cross_%d" },
                             "outputs": { "logits": "logits", "present_key_names": "present_key_self_%d", "present_value_names": "present_value_self_%d" } } },
              "search": { "max_length": 448 } }
            """);

        var export = WhisperExport.Read(_folder);

        CollectionAssert.AreEqual(new[] { "present_key_cross_0", "present_key_cross_1", "present_value_cross_0", "present_value_cross_1" }, export.CrossOutputs.ToArray());
        CollectionAssert.AreEqual(new[] { "past_key_self_0", "past_key_self_1", "past_value_self_0", "past_value_self_1" }, export.SelfPasts.ToArray());
        CollectionAssert.AreEqual(new[] { "present_key_self_0", "present_key_self_1", "present_value_self_0", "present_value_self_1" }, export.SelfPresents.ToArray());
        Assert.AreEqual((51866, 50257, 448), (export.Vocabulary, export.EndOfText, export.MaxLength));
    }

    [TestMethod]
    public void AnExportWithNoConfig_IsNotFound_AndOneThatLeavesSomethingOut_IsInvalid()
    {
        Assert.AreEqual(Nexaflow.AiHost.Contracts.HostErrorKind.NotFound,
                        Assert.ThrowsExactly<HostRequestException>(() => NemotronExport.Read(_folder)).Kind);

        File.WriteAllText(Path.Combine(_folder, NemotronExport.ConfigFile), """{ "model": { "num_mels": 128 } }""");
        Assert.AreEqual(Nexaflow.AiHost.Contracts.HostErrorKind.Invalid,
                        Assert.ThrowsExactly<HostRequestException>(() => NemotronExport.Read(_folder)).Kind);
    }

    [TestMethod]
    public void UnigramPieces_ReadBackWithTheirWordMarkersAsSpaces()
    {
        string path = Path.Combine(_folder, "tokenizer.json");
        File.WriteAllText(path, """{ "model": { "type": "Unigram", "unk_id": 0, "vocab": [["<unk>", 0.0], ["▁Hallo", -1.0], ["<de-DE>", -2.0], ["!", -3.0]] } }""");

        var pieces = UnigramPieces.Read(path);

        Assert.AreEqual(" Hallo<de-DE>!", pieces.Decode(1) + pieces.Decode(2) + pieces.Decode(3));
    }

    [TestMethod]
    public void AByteLevelTokenizer_KeepsItsAddedTokensWhole_AndReadsBytesBack()
    {
        var tokenizer = ByteLevelBpe.FromJson("""
            { "normalizer": null,
              "pre_tokenizer": { "type": "ByteLevel", "add_prefix_space": false, "trim_offsets": true, "use_regex": true },
              "model": { "type": "BPE", "vocab": { "h": 0, "i": 1, "Ġ": 2, "hi": 3, "Ġhi": 4 }, "merges": ["h i", "Ġ hi"] },
              "added_tokens": [ { "id": 5, "content": "<|startoftranscript|>", "special": true }, { "id": 6, "content": "<|en|>", "special": true } ] }
            """);

        CollectionAssert.AreEqual(new[] { 5, 6 }, tokenizer.EncodeToIds("<|startoftranscript|><|en|>").ToArray());
        Assert.AreEqual(" hi", tokenizer.Decode([5, 4], considerSpecialTokens: false));
    }
}

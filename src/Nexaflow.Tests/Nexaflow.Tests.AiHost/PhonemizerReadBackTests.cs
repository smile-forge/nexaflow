using System.Text.Json;
using MisakiSharp;
using Nexaflow.AiHost;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Speech;
using Nexaflow.AiHost.Speech.Phonemes;
using Nexaflow.IO.Common.Audio;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// How well French and German replies are understood when the voices read them: real reply sentences phonemised —
/// by our phonemiser, by espeak-ng reading each sentence whole, and for French by MisakiSharp's espeak-free replay —
/// spoken by Kokoro, heard back by Whisper large-v3-turbo, and scored by the words it gets wrong. Needs
/// <c>NEXAFLOW_TEST_SPEECH</c> (the speech models, with <c>kokoro-82m</c> holding <c>voices/ff_siwis.bin</c> and
/// <c>kokoro-82m-german</c> the German fine-tune) and <c>NEXAFLOW_TEST_LEXICONS</c> (the lexicons and the sentences with
/// espeak-ng's readings); <c>NEXAFLOW_TEST_RUNTIME_PACK</c> runs Whisper on the GPU.
/// </summary>
[TestClass]
[TestCategory("OwnedModel")]
[CoversNode("owned-speech")]
public class PhonemizerReadBackTests
{
    private const int Sentences = 100;

    private static AiHostClient? _client;
    private static ListenModels? _models;
    private static string _root = "", _lexicons = "";

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_SPEECH") is not { Length: > 0 } root
            || Environment.GetEnvironmentVariable("NEXAFLOW_TEST_LEXICONS") is not { Length: > 0 } lexicons) return;
        _root = root;
        _lexicons = lexicons;
        string? pack = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_RUNTIME_PACK") is { Length: > 0 } p ? p : null;
        _client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"),
                                                             Directory.CreateTempSubdirectory("nexaflow-aihost-").FullName))
        {
            Environment = new AiHostEnvironment(pack),
        };
        string nemotron = Path.Combine(root, "nemotron-3.5-asr-streaming");
        _models = new ListenModels(
            new ModelSpec("speech/voice-activity", "silero-vad", nemotron, "silero_vad.onnx", null, 0, 0),
            new ModelSpec("speech/recognizer", "genai-streaming-asr", nemotron, null, "genai-native", 2L << 30, 0),
            pack is null
                ? new ModelSpec("speech/transcriber", "genai-whisper", Path.Combine(root, "whisper-large-v3-turbo-cpu"), null, "genai-native", 0, 0)
                : new ModelSpec("speech/transcriber", "genai-whisper", Path.Combine(root, "whisper-large-v3-turbo-cuda"), null, "genai-native", 4L << 30, 0,
                                new Dictionary<string, string> { ["sdpa_kernel"] = "1" }),
            new ModelSpec("speech/turn", "smart-turn", Path.Combine(root, "smart-turn-v3"), "smart-turn-v3.2-cpu.onnx", null, 0, 0));
        foreach (var model in new[] { _models.VoiceActivity, _models.Recognizer, _models.Transcriber!, _models.TurnDetector! })
            await foreach (var _ in _client.SendAsync(id => new LoadModel(id, model), TimeSpan.FromMinutes(3))) { }
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    [TestMethod]
    [Timeout(3_600_000, CooperativeCancellation = true)]
    [DataRow("fr", "kokoro-82m", "voices/ff_siwis.bin")]
    [DataRow("de", "kokoro-82m-german", "voices/df_kerstin.bin")]
    public async Task RealReplies_AreUnderstoodWhenSpoken(string language, string voiceFolder, string voiceFile)
    {
        if (_client is null) Assert.Inconclusive("NEXAFLOW_TEST_SPEECH and NEXAFLOW_TEST_LEXICONS name no speech models and lexicons here.");
        using var kokoro = (KokoroSynthesizer)new KokoroSynthesizerRunner().Load(
            new ModelSpec($"speech/kokoro-{language}", "kokoro", Path.Combine(_root, voiceFolder), "onnx/model.onnx", null, 1L << 30, 0), ExecutionRuntime.Cpu);
        var voice = kokoro.Voice(voiceFile);

        PhonemeLanguage rules = language == "fr" ? new FrenchLanguage() : new GermanLanguage();
        var ours = new LexiconPhonemizer(PhonemeLexicon.Load(Path.Combine(_lexicons, $"{language}.tsv.gz")), rules, new EnglishPhonemizer());
        var replay = language == "fr" ? new EspeakG2P("fr-fr") : null;

        var rows = File.ReadLines(Path.Combine(_lexicons, $"context-{language}-sentences.jsonl")).Take(Sentences)
                       .Select(l => JsonDocument.Parse(l).RootElement)
                       .Select(r => (Sentence: r.GetProperty("sentence").GetString()!, Espeak: r.GetProperty("espeak").GetString()!))
                       .ToList();

        var errors = new Dictionary<string, (int Wrong, int Words)>();
        foreach (var (sentence, espeak) in rows)
        {
            var readings = new Dictionary<string, string> { ["ours"] = ours.Phonemize(sentence), ["espeak-ng"] = espeak };
            if (replay is not null) readings["misaki replay"] = replay.Phonemize(sentence);
            foreach (var (who, phonemes) in readings)
            {
                string heard = await HeardAsync(kokoro, voice, phonemes, language);
                var (wrong, words) = WordErrors(sentence, heard);
                errors[who] = errors.TryGetValue(who, out var sum) ? (sum.Wrong + wrong, sum.Words + words) : (wrong, words);
            }
        }

        foreach (var (who, (wrong, words)) in errors)
            Console.WriteLine($"{language} {who}: {100.0 * wrong / words:0.0}% words wrong ({wrong}/{words}) over {rows.Count} sentences");
        var (oursWrong, oursWords) = errors["ours"];
        var (espeakWrong, _) = errors["espeak-ng"];
        Assert.IsTrue(oursWrong <= espeakWrong * 1.1, $"ours {oursWrong} words wrong, espeak-ng {espeakWrong}");
        Assert.IsTrue(oursWrong <= oursWords * 0.15, $"{oursWrong}/{oursWords} words wrong");
    }

    private static async Task<string> HeardAsync(KokoroSynthesizer kokoro, float[] voice, string phonemes, string language)
    {
        var wave = kokoro.Tokenize(phonemes).SelectMany(piece => kokoro.Synthesize(piece, voice, 1f)).ToArray();
        byte[] pcm = new PcmResampler(KokoroSynthesizer.SampleRate, 16_000).Convert(Pcm16.FromSamples(wave));

        await using var stream = await _client!.OpenStreamAsync(id => new Listen(id, _models!, language, UtteranceEnd.Pause, RequestPriority.Voice), TimeSpan.FromMinutes(1));
        var reading = Task.Run(async () =>
        {
            var finals = new List<string>();
            await foreach (var reply in stream.ReadAsync()) if (reply is ListenFinal final) finals.Add(final.Text);
            return string.Join(' ', finals);
        });
        byte[] audio = [.. pcm, .. new byte[16_000 * 2 * 2]];
        const int piece = 1_600 * 2;
        for (int at = 0; at < audio.Length; at += piece)
            await stream.WriteAsync(new AudioInput(stream.RequestId, audio[at..Math.Min(audio.Length, at + piece)]));
        await stream.WriteAsync(new InputEnd(stream.RequestId));
        return await reading.WaitAsync(TimeSpan.FromMinutes(1));
    }

    /// <summary>The words Whisper got wrong — substituted, left out or added — against those written, ignoring case and
    /// punctuation.</summary>
    private static (int Wrong, int Words) WordErrors(string written, string heard)
    {
        static string[] Words(string text) => text.ToLowerInvariant()
            .Split([' ', '\'', '’', '-', ',', '.', '!', '?', ';', ':', '"', '«', '»', '(', ')', '“', '”'], StringSplitOptions.RemoveEmptyEntries);
        var a = Words(written);
        var b = Words(heard);
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return (d[a.Length, b.Length], a.Length);
    }
}

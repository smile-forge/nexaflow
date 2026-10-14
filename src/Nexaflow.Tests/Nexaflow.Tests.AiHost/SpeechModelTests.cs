using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The host listening with the real speech models — Silero, Nemotron, Whisper large-v3-turbo and Smart Turn — to
/// speech this machine's own voice says. <c>NEXAFLOW_TEST_SPEECH</c> names a folder holding them as their catalog lays
/// them out; <c>NEXAFLOW_TEST_RUNTIME_PACK</c> the CUDA pack, without which they run on the CPU. What is asserted is
/// where turns end and the words that decide it, not the transcripts' exact form.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("ai-host")]
public class SpeechModelTests
{
    private static AiHostClient? _client;
    private static ListenModels? _models;
    private static VoiceSpec? _voice;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
        if (Environment.GetEnvironmentVariable("NEXAFLOW_TEST_SPEECH") is not { Length: > 0 } root) return;

        string? pack = Environment.GetEnvironmentVariable("NEXAFLOW_TEST_RUNTIME_PACK") is { Length: > 0 } p ? p : null;
        var aiDir = Directory.CreateTempSubdirectory("nexaflow-aihost-").FullName;
        _client = new AiHostClient(new ProcessAiHostLauncher(Path.Combine(AppContext.BaseDirectory, "AiHost", "Nexaflow.AiHost.exe"), aiDir))
        {
            Environment = new AiHostEnvironment(pack),
        };

        // On the first GPU when the host runs from the pack; on the CPU without.
        int? gpu = pack is null ? null : 0;
        long Held(long bytes) => pack is null ? 0 : bytes;
        string nemotron = Path.Combine(root, "nemotron-3.5-asr-streaming");
        _models = new ListenModels(
            new ModelSpec("speech/voice-activity", "silero-vad", nemotron, "silero_vad.onnx", null, 0, 0),
            new ModelSpec("speech/recognizer", "genai-streaming-asr", nemotron, null, "genai-native", Held(2L << 30), 0, Device: gpu),
            pack is null
                ? new ModelSpec("speech/transcriber", "genai-whisper", Path.Combine(root, "whisper-large-v3-turbo-cpu"), null, "genai-native", 0, 0)
                : new ModelSpec("speech/transcriber", "genai-whisper", Path.Combine(root, "whisper-large-v3-turbo-cuda"), null, "genai-native", 4L << 30, 0,
                                new Dictionary<string, string> { ["sdpa_kernel"] = "1" }, Device: gpu),
            new ModelSpec("speech/turn", "smart-turn", Path.Combine(root, "smart-turn-v3"), "smart-turn-v3.2-cpu.onnx", null, 0, 0));

        _voice = new VoiceSpec("en", new ModelSpec("speech/kokoro", "kokoro", Path.Combine(root, "kokoro-82m"), "onnx/model.onnx", null, Held(1L << 30), 0,
                                                   Device: gpu),
                               "voices/af_heart.bin");

        foreach (var model in new[] { _models.VoiceActivity, _models.Recognizer, _models.Transcriber!, _models.TurnDetector!, _voice.Model })
            await foreach (var _ in _client.SendAsync(id => new LoadModel(id, model), TimeSpan.FromMinutes(3))) { }
    }

    [ClassCleanup]
    public static async Task Stop()
    {
        if (_client is not null) await _client.DisposeAsync();
    }

    [TestMethod]
    public async Task ASentence_IsHeardAsItIsSpoken_AndEndsAsOneTurn()
    {
        var heard = await ListenAsync(Speech(p => p.AppendText("What time is it in Paris right now?")), UtteranceEnd.Turn);

        Assert.IsTrue(heard.OfType<ListenActivity>().First().Speaking, "speech is noticed first");
        Assert.IsTrue(heard.OfType<ListenPartial>().Any(), "words show while it is spoken");
        var turn = heard.OfType<ListenFinal>().Single();
        StringAssert.Contains(turn.Text.ToLowerInvariant(), "paris");

    }

    [TestMethod]
    public async Task AThinkingPause_DoesNotEndTheTurn()
    {
        var heard = await ListenAsync(Speech(p =>
        {
            p.AppendText("I was thinking that, um,");
            p.AppendBreak(TimeSpan.FromMilliseconds(1200));
            p.AppendText("we could meet on Thursday instead.");
        }), UtteranceEnd.Turn);

        var turn = heard.OfType<ListenFinal>().Single();
        StringAssert.Contains(turn.Text.ToLowerInvariant(), "thursday");
        StringAssert.Contains(turn.Text.ToLowerInvariant(), "thinking");
    }

    [TestMethod]
    public async Task AOneWordAnswer_IsHeard()
    {
        var heard = await ListenAsync(Speech(p => p.AppendText("No.")), UtteranceEnd.Turn);

        StringAssert.StartsWith(heard.OfType<ListenFinal>().Single().Text.ToLowerInvariant(), "no");
    }

    [TestMethod]
    public async Task Dictation_EndsAPhraseAtEachPause()
    {
        var heard = await ListenAsync(Speech(p =>
        {
            p.AppendText("Buy some milk.");
            p.AppendBreak(TimeSpan.FromMilliseconds(1200));
            p.AppendText("Call the bank tomorrow.");
        }), UtteranceEnd.Pause);

        var phrases = heard.OfType<ListenFinal>().Select(f => f.Text.ToLowerInvariant()).ToList();
        Assert.AreEqual(2, phrases.Count, string.Join(" | ", phrases));
        StringAssert.Contains(phrases[0], "milk");
        StringAssert.Contains(phrases[1], "bank");
    }

    [TestMethod]
    public async Task Silence_IsNoTurn()
    {
        var heard = await ListenAsync(new byte[16_000 * 2 * 2], UtteranceEnd.Turn);

        Assert.IsFalse(heard.OfType<ListenFinal>().Any());
        Assert.IsFalse(heard.OfType<ListenActivity>().Any());
    }

    [TestMethod]
    public async Task Segments_AreSpokenInOrder_AndWhatIsSaidIsWhatWasWritten()
    {
        if (_client is null || _voice is null) Assert.Inconclusive("NEXAFLOW_TEST_SPEECH names no speech model folder on this machine.");

        await using var stream = await _client!.OpenStreamAsync(id => new Speak(id, [_voice!], 1f, RequestPriority.Voice), TimeSpan.FromMinutes(1));
        await stream.WriteAsync(new TextToSpeak(stream.RequestId, 0, "Good morning, Sam.", "en"));
        await stream.WriteAsync(new TextToSpeak(stream.RequestId, 1, "Your meeting has moved to Thursday at three.", "en"));
        await stream.WriteAsync(new InputEnd(stream.RequestId));
        var replies = new List<HostReply>();
        await foreach (var reply in stream.ReadAsync()) if (reply is not Progress) replies.Add(reply);

        CollectionAssert.AreEqual(new[] { 0, 1 }, replies.OfType<SpokenSegmentEnd>().Select(e => e.Segment).ToArray());
        Assert.IsTrue(replies.TakeWhile(r => r is not SpokenSegmentEnd).All(r => r is SpokenAudio { Segment: 0 }), "the first segment is heard whole before the second");
        byte[] speech = [.. replies.OfType<SpokenAudio>().SelectMany(a => a.Pcm)];
        double seconds = speech.Length / 2.0 / 24_000;
        Assert.IsTrue(seconds is > 2 and < 8, $"{seconds:0.0} s of speech");

        // Heard back through the host's own ears: the words are the ones written.
        var heard = await ListenAsync(new Nexaflow.IO.Common.Audio.PcmResampler(24_000, 16_000).Convert(speech), UtteranceEnd.Pause);
        string text = string.Join(" ", heard.OfType<ListenFinal>().Select(f => f.Text)).ToLowerInvariant();
        StringAssert.Contains(text, "thursday");
        StringAssert.Contains(text, "morning");
    }

    [TestMethod]
    [DataRow("fr", "kokoro-82m", "voices/ff_siwis.bin", "Il reste 3,5 Go sur le disque.", "disque")]
    [DataRow("de", "kokoro-82m-german", "voices/df_kerstin.bin", "Die Datei liegt im Ordner Dokumente.", "ordner")]
    public async Task FrenchAndGerman_AreSpokenFromTheirLexicons_AndHeardBack(string language, string folder, string voiceFile, string written, string heardWord)
    {
        if (_client is null || _voice is null) Assert.Inconclusive("NEXAFLOW_TEST_SPEECH names no speech model folder on this machine.");
        string root = System.IO.Path.GetDirectoryName(_voice!.Model.Folder)!;
        var voice = new VoiceSpec(language, _voice.Model with { Id = $"speech/kokoro-{language}", Folder = System.IO.Path.Combine(root, folder) },
                                  voiceFile, System.IO.Path.Combine(root, folder, "lexicons", $"{language}.tsv.gz"));

        await using var stream = await _client!.OpenStreamAsync(id => new Speak(id, [_voice!, voice], 1f, RequestPriority.Voice), TimeSpan.FromMinutes(3));
        await stream.WriteAsync(new TextToSpeak(stream.RequestId, 0, written, language));
        await stream.WriteAsync(new InputEnd(stream.RequestId));
        var replies = new List<HostReply>();
        await foreach (var reply in stream.ReadAsync()) if (reply is not Progress) replies.Add(reply);
        byte[] speech = [.. replies.OfType<SpokenAudio>().SelectMany(a => a.Pcm)];

        await using var listening = await _client.OpenStreamAsync(id => new Listen(id, _models!, language, UtteranceEnd.Pause, RequestPriority.Voice), TimeSpan.FromMinutes(1));
        var reading = Task.Run(async () =>
        {
            var finals = new List<string>();
            await foreach (var reply in listening.ReadAsync()) if (reply is ListenFinal final) finals.Add(final.Text);
            return string.Join(' ', finals);
        });
        byte[] audio = [.. new Nexaflow.IO.Common.Audio.PcmResampler(24_000, 16_000).Convert(speech), .. new byte[16_000 * 2 * 2]];
        for (int at = 0; at < audio.Length; at += 3_200)
            await listening.WriteAsync(new AudioInput(listening.RequestId, audio[at..Math.Min(audio.Length, at + 3_200)]));
        await listening.WriteAsync(new InputEnd(listening.RequestId));
        string heard = (await reading.WaitAsync(TimeSpan.FromMinutes(1))).ToLowerInvariant();

        StringAssert.Contains(heard, heardWord, $"heard: {heard}");
    }

    /// <summary>Streams the audio in 100 ms pieces, then 2 s of quiet, and returns everything the host replied.</summary>
    private static async Task<List<HostReply>> ListenAsync(byte[] pcm, UtteranceEnd end)
    {
        if (_client is null || _models is null) Assert.Inconclusive("NEXAFLOW_TEST_SPEECH names no speech model folder on this machine.");

        await using var stream = await _client!.OpenStreamAsync(id => new Listen(id, _models!, "en", end, RequestPriority.Voice), TimeSpan.FromMinutes(1));
        var reading = Task.Run(async () =>
        {
            var replies = new List<HostReply>();
            await foreach (var reply in stream.ReadAsync())
                if (reply is not Progress) replies.Add(reply);
            return replies;
        });

        byte[] audio = [.. pcm, .. new byte[16_000 * 2 * 2]];
        const int piece = 1_600 * 2;
        for (int at = 0; at < audio.Length; at += piece)
            await stream.WriteAsync(new AudioInput(stream.RequestId, audio[at..Math.Min(audio.Length, at + piece)]));
        await stream.WriteAsync(new InputEnd(stream.RequestId));
        return await reading.WaitAsync(TimeSpan.FromMinutes(1));
    }

    /// <summary>What Windows' own voice says, as 16 kHz 16-bit mono PCM.</summary>
    private static byte[] Speech(Action<PromptBuilder> say)
    {
        using var synthesizer = new SpeechSynthesizer();
        using var pcm = new MemoryStream();
        synthesizer.SetOutputToAudioStream(pcm, new SpeechAudioFormatInfo(16_000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
        var prompt = new PromptBuilder(new System.Globalization.CultureInfo("en-US"));
        say(prompt);
        synthesizer.Speak(prompt);
        return [.. new byte[16_000 / 2 * 2], .. pcm.ToArray()];   // half a second of room before it
    }
}

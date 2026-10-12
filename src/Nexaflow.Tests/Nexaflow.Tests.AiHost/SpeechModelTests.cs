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
    public async Task ASentence_IsHeardAsItIsSpoken_AndEndsAsOneTurn()
    {
        var heard = await ListenAsync(Speech(p => p.AppendText("What time is it in Paris right now?")), UtteranceEnd.Turn);

        Assert.IsTrue(heard.OfType<ListenActivity>().First().Speaking, "speech is noticed first");
        Assert.IsTrue(heard.OfType<ListenPartial>().Any(), "words show while it is spoken");
        var turn = heard.OfType<ListenFinal>().Single();
        StringAssert.Contains(turn.Text.ToLowerInvariant(), "paris");
        Assert.IsFalse(turn.Backchannel);
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

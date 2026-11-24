using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.Tests.Fixtures;
using Nexaflow.AiHost.Speech;
using Nexaflow.Media.Audio;
using Nexaflow.AiHost.Catalog;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// The host listening with the real speech models — Silero, Nemotron, Whisper large-v3-turbo and Smart Turn — to
/// speech this machine's own voice says, and Kokoro speaking: <c>nemotron-whisper-turbo</c> and <c>kokoro-82m</c> from the test
/// store (<see cref="TestStore"/>), held where the host places them — on the CUDA pack where the store holds one this machine runs, on
/// the CPU otherwise. What is asserted is
/// where turns end and the words that decide it, not the transcripts' exact form.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("OwnedModel")]
[CoversNode("ai-host")]
public class SpeechModelTests
{
    private static AiHostClient? _client;

    /// <summary>Both models' parts, held for the class's requests as a voice conversation holds them.</summary>
    private static HostHold? _hold;

    /// <summary>The speech-to-text model of the catalog that hears the turns.</summary>
    private const string Listener = "nemotron-whisper-turbo";

    /// <summary>The text-to-speech model of the catalog that speaks, in the voice the catalog gives each language.</summary>
    private const string Speaker = "kokoro-82m";

    /// <summary>Whether the Whisper hearing the turns is an English-only one: a vocabulary one token short of a multilingual model's.</summary>
    private static bool _englishOnly;

    [ClassInitialize]
    public static async Task Start(TestContext context)
    {
    _client = await TestStore.HostOnEitherAsync();
    if (_client is null) return;

    // The host on the CUDA pack runs the parts its GPU builds are for; on the CPU, their CPU builds.
    bool cuda = _client.Environment.Cuda;
    _englishOnly = WhisperExport.Read(TestStore.PartFolder(Listener, HostedRoles.Transcriber, cuda)).Vocabulary < WhisperTranscriber.MultilingualVocabulary;
    _hold = await TestStore.HoldPlacedAsync(_client, [.. TestStore.HeldPartsOf(Listener, cuda), .. TestStore.HeldPartsOf(Speaker, cuda)], RequestPriority.Voice);
    }

    [ClassCleanup]
    public static async Task Stop()
    {
    if (_hold is not null) await _hold.DisposeAsync();
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
        if (_client is null || _hold is null) Assert.Inconclusive(TestStore.Missing(gpu: false));

        await using var stream = await _client!.OpenStreamAsync(id => new Speak(id, Speaker, 1f, RequestPriority.Voice), TimeSpan.FromMinutes(1));
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
        var heard = await ListenAsync(new PcmResampler(24_000, 16_000).Convert(speech), UtteranceEnd.Pause);
        string text = string.Join(" ", heard.OfType<ListenFinal>().Select(f => f.Text)).ToLowerInvariant();
        StringAssert.Contains(text, "thursday");
        StringAssert.Contains(text, "morning");
    }

    [TestMethod]
    [DataRow("fr", "Il reste 3,5 Go sur le disque.", "disque")]
    [DataRow("de", "Die Datei liegt im Ordner Dokumente.", "ordner")]
    public async Task FrenchAndGerman_AreSpokenFromTheirLexicons_AndHeardBack(string language, string written, string heardWord)
    {
        if (_client is null || _hold is null) Assert.Inconclusive(TestStore.Missing(gpu: false));
        if (_englishOnly) Assert.Inconclusive($"The Whisper of {Listener} hears English only.");

        await using var stream = await _client!.OpenStreamAsync(id => new Speak(id, Speaker, 1f, RequestPriority.Voice), TimeSpan.FromMinutes(3));
        await stream.WriteAsync(new TextToSpeak(stream.RequestId, 0, written, language));
        await stream.WriteAsync(new InputEnd(stream.RequestId));
        var replies = new List<HostReply>();
        await foreach (var reply in stream.ReadAsync()) if (reply is not Progress) replies.Add(reply);
        byte[] speech = [.. replies.OfType<SpokenAudio>().SelectMany(a => a.Pcm)];

        await using var listening = await _client.OpenStreamAsync(id => new Listen(id, Listener, language, UtteranceEnd.Pause, RequestPriority.Voice), TimeSpan.FromMinutes(1));
        var reading = Task.Run(async () =>
        {
            var finals = new List<string>();
            await foreach (var reply in listening.ReadAsync()) if (reply is ListenFinal final) finals.Add(final.Text);
            return string.Join(' ', finals);
        });
        byte[] audio = [.. new PcmResampler(24_000, 16_000).Convert(speech), .. new byte[16_000 * 2 * 2]];
        for (int at = 0; at < audio.Length; at += 3_200)
            await listening.WriteAsync(new AudioInput(listening.RequestId, audio[at..Math.Min(audio.Length, at + 3_200)]));
        await listening.WriteAsync(new InputEnd(listening.RequestId));
        string heard = (await reading.WaitAsync(TimeSpan.FromMinutes(1))).ToLowerInvariant();

        StringAssert.Contains(heard, heardWord, $"heard: {heard}");
    }

    /// <summary>Streams the audio in 100 ms pieces, then 2 s of quiet, and returns everything the host replied.</summary>
    private static async Task<List<HostReply>> ListenAsync(byte[] pcm, UtteranceEnd end)
    {
        if (_client is null || _hold is null) Assert.Inconclusive(TestStore.Missing(gpu: false));

        await using var stream = await _client!.OpenStreamAsync(id => new Listen(id, Listener, "en", end, RequestPriority.Voice), TimeSpan.FromMinutes(1));
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

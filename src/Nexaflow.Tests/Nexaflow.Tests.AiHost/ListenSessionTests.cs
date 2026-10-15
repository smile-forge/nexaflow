using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Speech;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>Where utterances start and end, decided frame by frame — with the models stood in for.</summary>
[TestClass]
[CoversNode("ai-host")]
public class ListenSessionTests
{
    private const float Speech = 0.9f, Quiet = 0.05f;

    private sealed class Script
    {
        public readonly Queue<float> Activity = new();
        public readonly List<HostReply> Replies = [];
        public readonly List<int> Transcribed = [];
        public string Transcript = "hello there.";
        public Func<int, float> TurnComplete = _ => 1f;
        public int TurnAsks;
        public int RecognizerFrames;

        public void Add(float probability, int frames)
        {
            for (int i = 0; i < frames; i++) Activity.Enqueue(probability);
        }

        public ListenSession Session(UtteranceEnd end, bool transcribe = true, bool detectTurns = true) => new(
            7, "en", end, new Vad(this), () => new Recognizer(this),
            transcribe ? new Transcriber(this) : null, detectTurns ? new Turns(this) : null,
            reply => { lock (Replies) Replies.Add(reply); return Task.CompletedTask; });   // partials come from the recogniser's pump, beside the frames

        public async Task RunAsync(ListenSession session)
        {
            while (Activity.Count > 0) await session.FrameAsync(new float[ListenSession.FrameSamples]);
            await session.FinishAsync();
        }

        public List<ListenFinal> Finals => [.. Replies.OfType<ListenFinal>()];

        private sealed class Vad(Script s) : IVoiceActivityStream
        {
            public float Next(ReadOnlySpan<float> frame) => s.Activity.Dequeue();
        }

        private sealed class Recognizer(Script s) : IRecognizerStream
        {
            public ValueTask<string?> FeedAsync(float[] samples, CancellationToken ct)
                => ValueTask.FromResult<string?>(++s.RecognizerFrames % 10 == 0 ? $"words {s.RecognizerFrames / 10}" : null);

            public ValueTask<string?> FlushAsync(CancellationToken ct) => ValueTask.FromResult<string?>(null);

            public void Dispose() { }
        }

        private sealed class Transcriber(Script s) : IUtteranceTranscriber
        {
            public Task<string> TranscribeAsync(float[] audio, CancellationToken ct)
            {
                s.Transcribed.Add(audio.Length / ListenSession.FrameSamples);
                return Task.FromResult(s.Transcript);
            }
        }

        private sealed class Turns(Script s) : ITurnDetector
        {
            public float CompleteProbability(ReadOnlySpan<float> turn) => s.TurnComplete(++s.TurnAsks);
        }
    }

    [TestMethod]
    public async Task SpeechStarts_After96Milliseconds_NotAtAClick()
    {
        var s = new Script();
        s.Add(Quiet, 5); s.Add(Speech, 2); s.Add(Quiet, 30);   // a click
        await s.RunAsync(s.Session(UtteranceEnd.Turn));
        Assert.AreEqual(0, s.Replies.Count);

        s.Add(Quiet, 5); s.Add(Speech, ListenSession.OnsetFrames); s.Add(Quiet, 30);
        await s.RunAsync(s.Session(UtteranceEnd.Turn));
        Assert.IsTrue(((ListenActivity)s.Replies[0]).Speaking);
        Assert.AreEqual(1, s.Finals.Count);
    }

    [TestMethod]
    public async Task SpeechIsNoticed_WhileTheRecognizerWaitsForTheGpu()
    {
        var gpu = new TaskCompletionSource();
        var replies = new List<HostReply>();
        var session = new ListenSession(7, "en", UtteranceEnd.Turn, new Steady(Speech), () => new Stuck(gpu.Task), null, new Never(),
            r => { lock (replies) replies.Add(r); return Task.CompletedTask; });

        for (int i = 0; i < ListenSession.OnsetFrames + 20; i++)
            await session.FrameAsync(new float[ListenSession.FrameSamples]).WaitAsync(TimeSpan.FromSeconds(2));

        lock (replies) Assert.IsTrue(((ListenActivity)replies[0]).Speaking, "what pauses the assistant does not queue behind recognition");
        gpu.SetResult();
        await session.FinishAsync();
    }

    private sealed class Steady(float probability) : IVoiceActivityStream
    {
        public float Next(ReadOnlySpan<float> frame) => probability;
    }

    private sealed class Never : ITurnDetector
    {
        public float CompleteProbability(ReadOnlySpan<float> turn) => 0f;
    }

    /// <summary>A recogniser whose every chunk waits for a GPU that is busy elsewhere.</summary>
    private sealed class Stuck(Task gpu) : IRecognizerStream
    {
        public async ValueTask<string?> FeedAsync(float[] samples, CancellationToken ct) { await gpu; return null; }
        public ValueTask<string?> FlushAsync(CancellationToken ct) => ValueTask.FromResult<string?>(null);
        public void Dispose() { }
    }

    [TestMethod]
    public async Task AnUtterance_KeepsTheSoundBeforeItsOnset()
    {
        var s = new Script();
        s.Add(Quiet, 20); s.Add(Speech, 20); s.Add(Quiet, 40);
        await s.RunAsync(s.Session(UtteranceEnd.Turn));

        // 10 frames of lead-in (the onset's 3 among them), the 17 speech frames after it, and the pause it was asked at.
        Assert.AreEqual(ListenSession.PrerollFrames + 17 + ListenSession.PauseFrames, s.Transcribed.Single());
    }

    [TestMethod]
    public async Task Dictation_EndsAPhrase_After600MillisecondsOfSilence()
    {
        var s = new Script();
        s.Add(Speech, 20); s.Add(Quiet, ListenSession.DictationEndFrames - 1); s.Add(Speech, 20); s.Add(Quiet, ListenSession.DictationEndFrames);
        await s.RunAsync(s.Session(UtteranceEnd.Pause, detectTurns: false));
        Assert.AreEqual(1, s.Finals.Count, "a shorter pause does not end it");

        s.Add(Speech, 20); s.Add(Quiet, ListenSession.DictationEndFrames); s.Add(Speech, 20); s.Add(Quiet, ListenSession.DictationEndFrames);
        await s.RunAsync(s.Session(UtteranceEnd.Pause, detectTurns: false));
        Assert.AreEqual(3, s.Finals.Count);
    }

    [TestMethod]
    public async Task ATurn_EndsWhenTheDetectorJudgesItComplete_AskedFirstAfter200Milliseconds()
    {
        var s = new Script { TurnComplete = ask => ask >= 3 ? 0.9f : 0.1f };
        s.Add(Speech, 20); s.Add(Quiet, 60);
        await s.RunAsync(s.Session(UtteranceEnd.Turn));

        Assert.AreEqual(3, s.TurnAsks, "asked at 7 frames of silence, then every 9");
        var end = s.Replies.IndexOf(s.Finals.Single());
        Assert.IsFalse(s.Replies.Take(end).OfType<ListenActivity>().Last().Speaking, "the pause was reported before the turn ended");
    }

    [TestMethod]
    public async Task ATurnTheTranscriptLeavesOpen_IsWaitedFor_UntilTheCeiling()
    {
        var s = new Script { Transcript = "I was thinking that, um," };
        s.Add(Speech, 20); s.Add(Quiet, ListenSession.TurnCeilingFrames - 1);
        await s.RunAsync(s.Session(UtteranceEnd.Turn));
        Assert.AreEqual(1, s.Finals.Count, "only the end of input ended it");
        Assert.AreEqual(1, s.Transcribed.Count, "the same speech is transcribed once, however often the turn is asked about");

        var ceiling = new Script { Transcript = "I was thinking that, um," };
        ceiling.Add(Speech, 20); ceiling.Add(Quiet, ListenSession.TurnCeilingFrames); ceiling.Add(Speech, 20); ceiling.Add(Quiet, 5);
        await ceiling.RunAsync(ceiling.Session(UtteranceEnd.Turn));
        Assert.AreEqual(2, ceiling.Finals.Count, "the ceiling ended the first");
    }

    [TestMethod]
    public async Task ATurnLongerThanTheDetectorHears_EndsOnItsTranscript_FromTheSecondAsk()
    {
        var s = new Script { TurnComplete = _ => 0.1f };
        int frames = ListenSession.LongTurnSamples / ListenSession.FrameSamples + 5;
        s.Add(Speech, frames); s.Add(Quiet, 40);
        await s.RunAsync(s.Session(UtteranceEnd.Turn));

        var end = s.Replies.IndexOf(s.Finals.Single());
        Assert.AreEqual(2, s.TurnAsks, "the first ask, at a breath, does not end it; the second does");
        Assert.AreEqual(1, s.Transcribed.Count);
    }

    [TestMethod]
    public async Task SpeechAfterAPause_ContinuesTheSameTurn()
    {
        var s = new Script { TurnComplete = _ => 0.1f };
        s.Add(Speech, 20); s.Add(Quiet, 30); s.Add(Speech, 20); s.Add(Quiet, 5);
        await s.RunAsync(s.Session(UtteranceEnd.Turn));

        Assert.AreEqual(1, s.Finals.Count);
        CollectionAssert.AreEqual(new[] { true, false, true, false }, s.Replies.OfType<ListenActivity>().Select(a => a.Speaking).ToArray());
    }

    [TestMethod]
    public async Task WordsShowAsTheyAreRecognised_AndAnUnheardUtteranceHasNoFinal()
    {
        var s = new Script { Transcript = "" };
        s.Add(Speech, 30); s.Add(Quiet, 30);
        await s.RunAsync(s.Session(UtteranceEnd.Turn));

        Assert.IsTrue(s.Replies.OfType<ListenPartial>().Any());
        Assert.AreEqual(0, s.Finals.Count);
    }

    [TestMethod]
    public async Task WithoutATranscriber_TheRecognizersWordsAreTheFinal()
    {
        var s = new Script();
        s.Add(Speech, 30); s.Add(Quiet, 30);
        await s.RunAsync(s.Session(UtteranceEnd.Pause, transcribe: false, detectTurns: false));

        Assert.AreEqual(s.Replies.OfType<ListenPartial>().Last().Text, s.Finals.Single().Text);
    }
}

using System.Threading.Channels;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Speech;

namespace Nexaflow.AiHost.Speech;

/// <summary>Speech probability for each 32 ms frame of one stream; it keeps that stream's state.</summary>
internal interface IVoiceActivityStream
{
    float Next(ReadOnlySpan<float> frame);
}

/// <summary>Words recognised as an utterance is spoken.</summary>
internal interface IRecognizerStream : IDisposable
{
    /// <summary>Takes more of the utterance; returns its text so far when that changed, otherwise null.</summary>
    ValueTask<string?> FeedAsync(float[] samples, CancellationToken ct);

    /// <summary>Recognises what is still buffered; returns the text so far when that changed, otherwise null.</summary>
    ValueTask<string?> FlushAsync(CancellationToken ct);
}

/// <summary>A finished utterance, transcribed whole.</summary>
internal interface IUtteranceTranscriber
{
    Task<string> TranscribeAsync(float[] audio, CancellationToken ct);
}

/// <summary>Whether the speaker has finished their turn, judged from how it sounds.</summary>
internal interface ITurnDetector
{
    /// <summary>The probability the turn is complete, from its audio up to now.</summary>
    float CompleteProbability(ReadOnlySpan<float> turn);
}

/// <summary>
/// One stretch of listening, frame by frame: where speech starts (96 ms of it, so a click is not a turn), the words as
/// they are recognised, and where each utterance ends — at a pause when dictating; in a conversation, when the turn
/// detector judges the audio finished and the transcript does not end mid-phrase, or at a ceiling longer than any
/// thinking pause. Time is counted in frames, so the session decides the same way however fast it is fed.
/// <para>
/// <b>The frame path never waits on the GPU.</b> Detecting speech is what stops the assistant when the user talks over
/// it, so it must not queue behind a sentence being synthesised or an utterance being transcribed: each utterance's
/// recogniser is fed by a pump of its own, a transcript is asked for and read once it is ready, and an utterance's
/// final text is completed after the session has moved on — in order, one utterance after another.
/// </para>
/// </summary>
internal sealed class ListenSession(
    long requestId,
    string? language,
    UtteranceEnd end,
    IVoiceActivityStream voiceActivity,
    Func<IRecognizerStream> openRecognizer,
    IUtteranceTranscriber? transcriber,
    ITurnDetector? turnDetector,
    Func<HostReply, Task> reply,
    CancellationToken ct = default,
    Func<IDisposable>? engage = null) : IAsyncDisposable
{
    public const int SampleRate   = 16_000;
    public const int FrameSamples = 512;

    private const float SpeechThreshold  = 0.5f;
    private const float SilenceThreshold = 0.35f;
    private const float TurnComplete     = 0.5f;

    /// <summary>96 ms of speech starts an utterance; a click or a cough's onset does not.</summary>
    internal const int OnsetFrames = 3;
    /// <summary>320 ms kept from before the onset, so the utterance's first sound is not cut.</summary>
    internal const int PrerollFrames = 10;
    /// <summary>About 200 ms of silence is a pause — where the turn detector is first asked.</summary>
    internal const int PauseFrames = 7;
    /// <summary>About 300 ms between asks while the turn detector says the speaker is still going.</summary>
    internal const int TurnRecheckFrames = 9;
    /// <summary>About 600 ms of silence ends a dictated phrase.</summary>
    internal const int DictationEndFrames = 19;
    /// <summary>About 3 s of silence ends any turn — longer than the 2.4 s thinking pauses measured.</summary>
    internal const int TurnCeilingFrames = 94;
    /// <summary>The turn detector's window: a turn longer than this is judged by its transcript too.</summary>
    internal const int LongTurnSamples = 8 * SampleRate;
    /// <summary>About 300 ms of trailing silence kept for the transcriber; more only invites it to hear words in noise.</summary>
    private const int TranscribedTailFrames = 9;

    private readonly Queue<float[]> _preroll = new();
    private Utterance? _utterance;
    private bool _speaking;
    private int _onsetRun;
    private int _silentFrames;
    private int _nextTurnAsk = PauseFrames;
    private Task _finals = Task.CompletedTask;

    /// <summary>Hears one <see cref="FrameSamples"/>-sample frame.</summary>
    public async Task FrameAsync(float[] frame)
    {
        float p = voiceActivity.Next(frame);
        if (_utterance is not { } utterance)
        {
            _preroll.Enqueue(frame);
            if (_preroll.Count > PrerollFrames) _preroll.Dequeue();
            _onsetRun = p >= SpeechThreshold ? _onsetRun + 1 : 0;
            if (_onsetRun >= OnsetFrames) await BeginAsync();
            return;
        }

        utterance.Add(frame);

        // Between the thresholds, speech carries on until silence has begun: a word's quiet tail is not a pause.
        bool speech = p >= SpeechThreshold || (p >= SilenceThreshold && _silentFrames == 0);
        if (speech)
        {
            _silentFrames = 0;
            _nextTurnAsk  = PauseFrames;
            utterance.SpeechEnd = utterance.Length;
            if (!_speaking)
            {
                _speaking = true;
                await reply(new ListenActivity(requestId, true));
            }
            return;
        }

        _silentFrames++;
        if (_silentFrames == PauseFrames && _speaking)
        {
            _speaking = false;
            await reply(new ListenActivity(requestId, false));
        }

        if (end == UtteranceEnd.Pause)
        {
            if (_silentFrames >= DictationEndFrames) await EndAsync();
            return;
        }
        if (_silentFrames >= TurnCeilingFrames)
        {
            await EndAsync();
            return;
        }
        if (_silentFrames == _nextTurnAsk) Ask(utterance);
        if (utterance.Transcript is { IsCompleted: true } done && utterance.TranscriptOf == utterance.SpeechEnd && utterance.Finishing)
        {
            if (SpokenTurns.LooksFinished(await done, language)) await EndAsync();
            else utterance.Finishing = false;   // the words leave it open: the next ask decides again
        }
    }

    /// <summary>No more audio: an utterance under way ends where it is, and every final is sent.</summary>
    public async Task FinishAsync()
    {
        if (_utterance is not null) await EndAsync();
        await _finals;
    }

    private async Task BeginAsync()
    {
        _speaking = true;
        _onsetRun = 0;
        await reply(new ListenActivity(requestId, true));   // first: this is what pauses the assistant

        var utterance = new Utterance(openRecognizer(), this);
        _utterance = utterance;
        while (_preroll.TryDequeue(out var earlier)) utterance.Add(earlier);
        utterance.SpeechEnd = utterance.Length;
    }

    /// <summary>A pause long enough to ask whether the turn is over. The detector runs on the CPU, here; the
    /// transcript it may need is asked for and read on a later frame, once it is ready.</summary>
    private void Ask(Utterance utterance)
    {
        _nextTurnAsk = _silentFrames + TurnRecheckFrames;
        bool complete = turnDetector!.CompleteProbability(utterance.Audio) >= TurnComplete;

        // The detector hears only the last 8 s, so of a longer turn it hears no beginning, and it judged the ends of
        // measured long turns unfinished. There the transcript decides — from the second ask, so that a breath between
        // two sentences does not end the turn.
        bool longTurn = utterance.SpeechEnd > LongTurnSamples && _silentFrames > PauseFrames;
        if (!complete && !longTurn) return;
        utterance.Finishing = true;
        if (utterance.TranscriptOf != utterance.SpeechEnd) utterance.Transcribe();
    }

    /// <summary>The utterance is over: the session listens for the next at once, and this one's final text — the
    /// transcript, or the recogniser's last words — is sent once it is ready, after any before it.</summary>
    private async Task EndAsync()
    {
        var utterance = _utterance!;
        _utterance    = null;
        if (_speaking) await reply(new ListenActivity(requestId, false));
        _speaking     = false;
        _silentFrames = 0;
        _nextTurnAsk  = PauseFrames;
        _preroll.Clear();

        var before = _finals;
        _finals = SendFinalAsync(before, utterance);
        if (_finals.IsCompleted) await _finals;   // surfaces a failure now rather than at the end
    }

    private async Task SendFinalAsync(Task before, Utterance utterance)
    {
        await before;
        string text = (await utterance.FinalTextAsync()).Trim();
        if (text.Length > 0) await reply(new ListenFinal(requestId, text));
    }

    public async ValueTask DisposeAsync()
    {
        if (_utterance is { } utterance) await utterance.DisposeAsync();
    }

    private IUtteranceTranscriber? Transcriber => transcriber;
    private CancellationToken Token => ct;

    private IDisposable? Engaged() => engage?.Invoke();
    private Task SendPartialAsync(string text) => reply(new ListenPartial(requestId, text));

    /// <summary>One utterance: its audio, the recogniser fed from it on a pump of its own, and its transcript.</summary>
    private sealed class Utterance : IAsyncDisposable
    {
        private readonly ListenSession _session;
        private readonly IRecognizerStream _recognizer;
        private readonly List<float> _audio = [];
        private readonly Channel<float[]> _frames = Channel.CreateUnbounded<float[]>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        private readonly Task _pump;
        private readonly IDisposable? _engaged;
        private string _partial = "";

        /// <summary>Someone is speaking: from the first word until the text that counts is out, the models hearing them are
        /// attending to someone (<see cref="GpuScheduler.Engage"/>).</summary>
        public Utterance(IRecognizerStream recognizer, ListenSession session)
        {
            _recognizer = recognizer;
            _session    = session;
            _engaged    = session.Engaged();
            _pump       = Task.Run(PumpAsync);
        }

        public int Length => _audio.Count;
        public ReadOnlySpan<float> Audio => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_audio);

        /// <summary>Where the last speech ended, in samples.</summary>
        public int SpeechEnd { get; set; }

        /// <summary>The detector judged the turn over; its transcript decides, once it is ready.</summary>
        public bool Finishing { get; set; }

        public Task<string>? Transcript { get; private set; }

        /// <summary>The speech the transcript is of — the utterance up to where speech last ended.</summary>
        public int TranscriptOf { get; private set; } = -1;

        public void Add(float[] frame)
        {
            _audio.AddRange(frame);
            _frames.Writer.TryWrite(frame);
        }

        /// <summary>Asks for the transcript of the speech so far, and a little after it.</summary>
        public void Transcribe()
        {
            TranscriptOf = SpeechEnd;
            if (_session.Transcriber is null)
            {
                Transcript = Task.FromResult(Volatile.Read(ref _partial));
                return;
            }
            int length = Math.Min(_audio.Count, SpeechEnd + TranscribedTailFrames * FrameSamples);
            Transcript = _session.Transcriber.TranscribeAsync([.. _audio.GetRange(0, length)], _session.Token);
        }

        /// <summary>The text that counts: the transcript of all the speech, or without a transcriber the recogniser's
        /// words once it has read everything.</summary>
        public async Task<string> FinalTextAsync()
        {
            _frames.Writer.TryComplete();
            try
            {
                if (_session.Transcriber is null)
                {
                    await _pump;
                    if (await _recognizer.FlushAsync(_session.Token) is { } flushed) await PartialAsync(flushed);
                    return _partial;
                }
                if (TranscriptOf != SpeechEnd || Transcript is null) Transcribe();
                return await Transcript!;
            }
            finally { await DisposeAsync(); }
        }

        private async Task PumpAsync()
        {
            await foreach (var frame in _frames.Reader.ReadAllAsync(_session.Token))
                if (await _recognizer.FeedAsync(frame, _session.Token) is { } text) await PartialAsync(text);
        }

        private async Task PartialAsync(string text)
        {
            if (text.Length == 0 || text == _partial) return;
            Volatile.Write(ref _partial, text);
            await _session.SendPartialAsync(text);
        }

        private int _disposed;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _frames.Writer.TryComplete();
            try { await _pump; } catch (OperationCanceledException) { }
            _recognizer.Dispose();
            _engaged?.Dispose();
        }
    }
}

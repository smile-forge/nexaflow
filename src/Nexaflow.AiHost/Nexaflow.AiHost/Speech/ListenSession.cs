using Nexaflow.AiHost.Contracts;

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
/// </summary>
internal sealed class ListenSession(
    long requestId,
    string? language,
    UtteranceEnd end,
    IVoiceActivityStream voiceActivity,
    Func<IRecognizerStream> openRecognizer,
    IUtteranceTranscriber? transcriber,
    ITurnDetector? turnDetector,
    Func<HostReply, Task> reply) : IDisposable
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
    /// <summary>About 300 ms of trailing silence kept for the transcriber; more only invites it to hear words in noise.</summary>
    private const int TranscribedTailFrames = 9;

    private readonly Queue<float[]> _preroll = new();
    private readonly List<float> _utterance = [];
    private IRecognizerStream? _recognizer;
    private bool _inUtterance;
    private bool _speaking;
    private int _onsetRun;
    private int _silentFrames;
    private int _nextTurnAsk = PauseFrames;
    private int _speechEnd;
    private string _partial = "";
    private (int SpeechEnd, string Text)? _transcript;

    /// <summary>Hears one <see cref="FrameSamples"/>-sample frame.</summary>
    public async Task FrameAsync(float[] frame, CancellationToken ct)
    {
        float p = voiceActivity.Next(frame);
        if (!_inUtterance)
        {
            _preroll.Enqueue(frame);
            if (_preroll.Count > PrerollFrames) _preroll.Dequeue();
            _onsetRun = p >= SpeechThreshold ? _onsetRun + 1 : 0;
            if (_onsetRun >= OnsetFrames) await BeginAsync(ct);
            return;
        }

        _utterance.AddRange(frame);
        await FeedAsync(frame, ct);

        // Between the thresholds, speech carries on until silence has begun: a word's quiet tail is not a pause.
        bool speech = p >= SpeechThreshold || (p >= SilenceThreshold && _silentFrames == 0);
        if (speech)
        {
            _silentFrames = 0;
            _nextTurnAsk  = PauseFrames;
            _speechEnd    = _utterance.Count;
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
        if (await EndsHereAsync(ct)) await EndAsync(ct);
    }

    /// <summary>No more audio: an utterance under way ends where it is.</summary>
    public async Task FinishAsync(CancellationToken ct)
    {
        if (_inUtterance) await EndAsync(ct);
    }

    private async Task BeginAsync(CancellationToken ct)
    {
        _inUtterance = true;
        _speaking    = true;
        _onsetRun    = 0;
        _recognizer  = openRecognizer();
        await reply(new ListenActivity(requestId, true));
        while (_preroll.TryDequeue(out var earlier))
        {
            _utterance.AddRange(earlier);
            await FeedAsync(earlier, ct);
        }
        _speechEnd = _utterance.Count;
    }

    private async Task FeedAsync(float[] frame, CancellationToken ct)
    {
        if (await _recognizer!.FeedAsync(frame, ct) is { } text) await PartialAsync(text);
    }

    private async Task PartialAsync(string text)
    {
        if (text == _partial || text.Length == 0) return;
        _partial = text;
        await reply(new ListenPartial(requestId, text));
    }

    private async Task<bool> EndsHereAsync(CancellationToken ct)
    {
        if (end == UtteranceEnd.Pause) return _silentFrames >= DictationEndFrames;
        if (_silentFrames >= TurnCeilingFrames) return true;
        if (_silentFrames != _nextTurnAsk) return false;

        _nextTurnAsk = _silentFrames + TurnRecheckFrames;
        if (turnDetector!.CompleteProbability(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_utterance)) < TurnComplete) return false;
        return TurnText.LooksFinished(await TranscriptAsync(ct), language);
    }

    /// <summary>The utterance's text: the transcriber's, of the speech and a little after it — kept while no more
    /// speech comes, so asking again costs nothing — or else the recognizer's.</summary>
    private async Task<string> TranscriptAsync(CancellationToken ct)
    {
        if (transcriber is null)
        {
            if (await _recognizer!.FlushAsync(ct) is { } flushed) await PartialAsync(flushed);
            return _partial;
        }
        if (_transcript is { } kept && kept.SpeechEnd == _speechEnd) return kept.Text;

        int length = Math.Min(_utterance.Count, _speechEnd + TranscribedTailFrames * FrameSamples);
        string text = (await transcriber.TranscribeAsync([.. _utterance.GetRange(0, length)], ct)).Trim();
        _transcript = (_speechEnd, text);
        return text;
    }

    private async Task EndAsync(CancellationToken ct)
    {
        string text = await TranscriptAsync(ct);
        if (_speaking) await reply(new ListenActivity(requestId, false));
        if (text.Length > 0) await reply(new ListenFinal(requestId, text, TurnText.IsBackchannel(text, language)));

        _recognizer?.Dispose();
        _recognizer  = null;
        _inUtterance = false;
        _speaking    = false;
        _silentFrames = 0;
        _nextTurnAsk = PauseFrames;
        _speechEnd   = 0;
        _partial     = "";
        _transcript  = null;
        _utterance.Clear();
        _preroll.Clear();
    }

    public void Dispose() => _recognizer?.Dispose();
}

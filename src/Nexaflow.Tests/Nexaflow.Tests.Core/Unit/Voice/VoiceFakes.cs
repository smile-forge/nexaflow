using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Nexaflow.Core.Services.Voice;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common.Audio;
using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Core.Unit.Voice;

/// <summary>A clock the test moves by hand; timers fire when it passes them, a periodic one again each period.</summary>
internal sealed class ManualTime : TimeProvider
{
    private readonly List<Timer> _timers = [];
    private DateTimeOffset _now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        _now += by;
        List<Timer> due;
        lock (_timers) due = [.. _timers.Where(t => t.Due is { } at && at <= _now)];
        foreach (var timer in due) timer.Fire();
    }

    /// <summary>One-shot timers set and not yet fired — what a test waits on before moving the clock past them.</summary>
    public int Waiting { get { lock (_timers) return _timers.Count(t => t.Due is not null && !t.Periodic); } }

    private sealed class Timer(ManualTime time, TimerCallback callback, object? state) : ITimer
    {
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public DateTimeOffset? Due { get; private set; }

        public bool Periodic => _period != Timeout.InfiniteTimeSpan;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (time._timers)
            {
                _period = period;
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : time._now + dueTime;
                if (!time._timers.Contains(this)) time._timers.Add(this);
            }
            return true;
        }

        public void Fire()
        {
            lock (time._timers) Due = Periodic ? time._now + _period : null;
            callback(state);
        }

        public void Dispose() { lock (time._timers) time._timers.Remove(this); }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>A listening model the test speaks through.</summary>
internal sealed class FakeListener : ISpeechToTextProvider
{
    private readonly Channel<SpeechToTextEvent> _heard = Channel.CreateUnbounded<SpeechToTextEvent>();

    public string Name => "ears";
    public DataResidency Residency => DataResidency.StaysOnMachine;
    public SpeechToTextOptions? Options { get; private set; }

    public void Hear(SpeechToTextEvent e) => _heard.Writer.TryWrite(e);

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<ISpeechToTextSession> StartSessionAsync(SpeechToTextOptions options, CancellationToken ct = default)
    {
        Options = options;
        return Task.FromResult<ISpeechToTextSession>(new Session(_heard.Reader));
    }

    private sealed class Session(ChannelReader<SpeechToTextEvent> heard) : ISpeechToTextSession
    {
        public ValueTask AppendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct = default) => ValueTask.CompletedTask;
        public ValueTask CompleteAsync(CancellationToken ct = default) => ValueTask.CompletedTask;
        public IAsyncEnumerable<SpeechToTextEvent> ReadEventsAsync(CancellationToken ct = default) => heard.ReadAllAsync(ct);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>A voice that "speaks" 10 ms of audio per character, and keeps what it was given to say.</summary>
internal sealed class FakeSpeaker : ITextToSpeechProvider
{
    public const int SamplesPerChar = 240;

    public readonly List<(string Text, string? Language)> Said = [];

    public string Name => "voice";
    public DataResidency Residency => DataResidency.StaysOnMachine;
    public bool Speaks(string language) => true;

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);

    public Task<ITextToSpeechSession> StartSessionAsync(TextToSpeechOptions options, CancellationToken ct = default)
        => Task.FromResult<ITextToSpeechSession>(new Session(this));

    public string[] Texts { get { lock (Said) return [.. Said.Select(s => s.Text)]; } }

    private sealed class Session(FakeSpeaker speaker) : ITextToSpeechSession
    {
        private readonly Channel<TextToSpeechEvent> _audio = Channel.CreateUnbounded<TextToSpeechEvent>();
        private int _segments;

        public ValueTask<int> SpeakAsync(string text, string? language = null, CancellationToken ct = default)
        {
            lock (speaker.Said) speaker.Said.Add((text, language));
            int segment = _segments++;
            _audio.Writer.TryWrite(new SpeechAudioChunk(segment, new byte[text.Length * SamplesPerChar * 2]));
            _audio.Writer.TryWrite(new SpeechSegmentEnd(segment));
            return ValueTask.FromResult(segment);
        }

        public ValueTask CompleteAsync(CancellationToken ct = default)
        {
            _audio.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<TextToSpeechEvent> ReadAudioAsync(CancellationToken ct = default) => _audio.Reader.ReadAllAsync(ct);

        public ValueTask DisposeAsync()
        {
            _audio.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class FakeCapture : ISpeechCapture
{
    private readonly Channel<byte[]> _audio = Channel.CreateUnbounded<byte[]>();

    public bool Disposed { get; private set; }
    public ChannelReader<byte[]> Audio => _audio.Reader;
    public bool CancelsEcho => true;
    public Task StartAsync() => Task.CompletedTask;

    public void Dispose()
    {
        Disposed = true;
        _audio.Writer.TryComplete();
    }
}

/// <summary>A speaker the test plays by hand.</summary>
internal sealed class FakePlayback : ISpeechPlayback
{
    private long _queued, _played;

    public string EndpointId => "speakers";
    public long QueuedSamples => Interlocked.Read(ref _queued);
    public long PlayedSamples => Interlocked.Read(ref _played);
    public bool IsPaused { get; private set; }
    public int Stops { get; private set; }

    public event EventHandler? Drained;

    public void Enqueue(byte[] pcm) => Interlocked.Add(ref _queued, pcm.Length / 2);
    public void Pause() => IsPaused = true;
    public void Resume() => IsPaused = false;

    public void Stop()
    {
        Stops++;
        IsPaused = false;
        Interlocked.Exchange(ref _queued, PlayedSamples);
    }

    /// <summary>Plays this much more, or everything queued.</summary>
    public void Play(long samples = long.MaxValue)
    {
        long played = Math.Min(QueuedSamples, PlayedSamples + Math.Min(samples, long.MaxValue - PlayedSamples));
        Interlocked.Exchange(ref _played, played);
        if (played == QueuedSamples) Drained?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() { }
}

/// <summary>The agent, scripted: each turn runs the script against the handler it is given.</summary>
internal sealed class FakeAgent(Func<string, IReadOnlyList<IClientTool>, IAIResponseHandler, CancellationToken, Task> script) : IVoiceAgent
{
    public readonly List<(string Text, IReadOnlyList<LlmMessage> History)> Turns = [];

    public async Task RunAsync(string userText, IReadOnlyList<LlmMessage> history, IReadOnlyList<IClientTool> sessionTools,
                               IAIResponseHandler handler, Action<IReadOnlyList<LlmMessage>> ran, CancellationToken ct)
    {
        lock (Turns) Turns.Add((userText, history));
        try { await script(userText, sessionTools, handler, ct); }
        finally { if (Runs?.Invoke(userText) is { } run) ran(run); }
    }

    /// <summary>The messages a turn's run adds to the conversation, as the agent loop reports them; none when unset.</summary>
    public Func<string, IReadOnlyList<LlmMessage>>? Runs { get; set; }

    public int Count { get { lock (Turns) return Turns.Count; } }

    /// <summary>What the Disambiguation model makes of words over an announcement; what it was asked.</summary>
    public Func<string, AnswerIntent> Judge { get; set; } = _ => AnswerIntent.Unclear;
    public readonly List<string> Judged = [];

    public Task<AnswerIntent> JudgeAnswerAsync(string announcement, string said, CancellationToken ct)
    {
        lock (Judged) Judged.Add(said);
        return Task.FromResult(Judge(said));
    }
}

/// <summary>What the conversation shows. A voice reply announces its actions itself, so approvals never reach it.</summary>
internal sealed class ShownConversation : IAIResponseHandler
{
    public readonly List<string> Finals = [];

    public void BeginResponse(string userText) { }
    public void ReportProgress(string message) { }
    public void ShowFinal(string finalMarkdown) { lock (Finals) Finals.Add(finalMarkdown); }
    public void Abort() { }

    public Task<bool> RequestToolBatchApprovalAsync(string explanationMarkdown, IReadOnlyList<ToolCall> batch, CancellationToken ct)
        => throw new InvalidOperationException("a voice reply announces its actions; it does not pass them on for approval");

    public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
        => throw new InvalidOperationException("a voice reply announces its plans; it does not pass them on for approval");
}

/// <summary>The bar's Stop and go-ahead: shown until the test presses one, or until answered some other way.</summary>
internal sealed class FakeConfirmations : IVoiceConfirmations
{
    private TaskCompletionSource<bool>? _shown;

    public readonly List<VoiceConfirmation> Shown = [];
    public int Withdrawn;

    public bool Showing => Volatile.Read(ref _shown) is { Task.IsCompleted: false };

    public async Task<bool> ShowAsync(VoiceConfirmation confirmation, CancellationToken ct)
    {
        var shown = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Shown) Shown.Add(confirmation);
        Volatile.Write(ref _shown, shown);
        await using var _ = ct.Register(() => { if (shown.TrySetCanceled(ct)) Interlocked.Increment(ref Withdrawn); });
        return await shown.Task;
    }

    public void Press(bool proceed) => Volatile.Read(ref _shown)?.TrySetResult(proceed);
}

using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Nexaflow.Core.Services.Voice;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common.Audio;
using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Core.Unit.Voice;

/// <summary>A clock the test moves by hand; timers fire when it passes them.</summary>
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

    private sealed class Timer(ManualTime time, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset? Due { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (time._timers)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : time._now + dueTime;
                if (!time._timers.Contains(this)) time._timers.Add(this);
            }
            return true;
        }

        public void Fire()
        {
            Due = null;
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

    public Task RunAsync(string userText, IReadOnlyList<LlmMessage> history, IReadOnlyList<IClientTool> sessionTools,
                         IAIResponseHandler handler, CancellationToken ct)
    {
        lock (Turns) Turns.Add((userText, history));
        return script(userText, sessionTools, handler, ct);
    }

    public int Count { get { lock (Turns) return Turns.Count; } }
}

/// <summary>What the conversation shows: approvals wait for a click that never comes, until cancelled.</summary>
internal sealed class ShownConversation : IAIResponseHandler
{
    public readonly List<string> Finals = [];
    public int ApprovalsCancelled;

    public void BeginResponse(string userText) { }
    public void ReportProgress(string message) { }
    public void ShowFinal(string finalMarkdown) { lock (Finals) Finals.Add(finalMarkdown); }
    public void Abort() { }

    public async Task<bool> RequestToolBatchApprovalAsync(string explanationMarkdown, IReadOnlyList<ToolCall> batch, CancellationToken ct)
    {
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { Interlocked.Increment(ref ApprovalsCancelled); throw; }
        return false;
    }

    public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct) => RequestToolBatchApprovalAsync(plan.Title, [], ct);
}

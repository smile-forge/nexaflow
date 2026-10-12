using System.Threading.Channels;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common.Audio;
using Nexaflow.IO.Common.Speech;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Voice;

/// <summary>Where a voice conversation is.</summary>
public enum VoiceState
{
    /// <summary>Opening the microphone and the models.</summary>
    Starting,
    /// <summary>Waiting for the user.</summary>
    Listening,
    /// <summary>The user has finished; the assistant is working on its reply.</summary>
    Thinking,
    /// <summary>The assistant is talking.</summary>
    Speaking,
    /// <summary>Over: stopped by the user, by the assistant, or by a failure.</summary>
    Ended,
}

/// <summary>Runs one turn of the agent for a voice session: the user's words, what was said before, and the handler
/// the reply is written to.</summary>
public interface IVoiceAgent
{
    Task RunAsync(string userText, IReadOnlyList<LlmMessage> history, IReadOnlyList<IClientTool> sessionTools,
                  IAIResponseHandler handler, CancellationToken ct);
}

/// <summary>What a voice session is made of: the models it listens and speaks with, the microphone and speaker, the
/// agent it talks to, the handler that shows the conversation, and the clock it times barge-in by.</summary>
public sealed record VoiceSessionParts(
    ISpeechToTextProvider Listener,
    ITextToSpeechProvider Speaker,
    ISpeechCapture Capture,
    ISpeechPlayback Playback,
    IVoiceAgent Agent,
    IAIResponseHandler Shown,
    TimeProvider Time);

/// <summary>
/// A spoken conversation, from the first press of the microphone until it is stopped or the assistant ends it. The
/// microphone streams into the Speech In model listening for turns; each finished turn runs the agent, whose reply is
/// spoken as it is written — first clause, then sentences — while anything speech cannot carry is shown.
/// <para>
/// <b>Barge-in.</b> Speech while the assistant talks pauses it at once; words within half a second stop the reply, and
/// what was actually heard of it — not what was written — is what the conversation remembers. No words — a cough, an
/// "mm-hm" — and it carries on from where it paused. A turn that ends just after an interruption is held a second for
/// the rest of the sentence ("Wait, stop —" is rarely all).
/// </para>
/// <para>Everything happens on one loop, so the state is never seen half-changed.</para>
/// </summary>
public sealed class VoiceSession : IAsyncDisposable
{
    internal static readonly TimeSpan BargeInDecision = TimeSpan.FromMilliseconds(500);
    internal static readonly TimeSpan HoldAfterBargeIn = TimeSpan.FromSeconds(1);

    private readonly VoiceSessionParts _parts;
    private readonly string? _language;
    private readonly Channel<Func<Task>> _loop = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _life = new();
    private readonly List<LlmMessage> _history = [];
    private readonly IClientTool _endTool;
    private ISpeechToTextSession? _listening;
    private Task _running = Task.CompletedTask;
    private VoiceReply? _reply;
    private ITimer? _bargeInTimer, _holdTimer;
    private bool _bargeInPending, _interrupted, _endRequested;
    private string _held = "";

    public VoiceSession(VoiceSessionParts parts, string? language)
    {
        _parts    = parts;
        _language = language;
        _endTool  = new DelegateClientTool("end_voice_session",
            "End the voice conversation, after your reply is spoken. Use when the user says goodbye or that they are done.",
            [], ToolSafety.SafeOperation, (_, _) =>
            {
                Post(() => { _endRequested = true; return Task.CompletedTask; });
                return Task.FromResult(ToolResult.Ok("The conversation ends once your reply has been spoken."));
            });
        _parts.Playback.Drained += (_, _) => Post(OnDrainedAsync);
    }

    public VoiceState State { get; private set; } = VoiceState.Starting;

    /// <summary>Raised from the session's loop; a listener marshals.</summary>
    public event EventHandler<VoiceState>? StateChanged;

    /// <summary>What the user is saying, as it is heard.</summary>
    public event EventHandler<string>? Heard;

    /// <summary>A diagram, table or code the assistant's reply holds, to show over the page.</summary>
    public event EventHandler<string>? Shown;

    /// <summary>Why it ended — null when the user stopped it or the assistant said goodbye.</summary>
    public event EventHandler<string?>? Ended;

    /// <summary>What the conversation has been, as the assistant will read it next turn.</summary>
    public IReadOnlyList<LlmMessage> History => _history;

    /// <summary>A turn that ended just after an interruption is being held for the rest of the sentence.</summary>
    internal bool IsHoldingTurn => _holdTimer is not null;

    /// <summary>Opens the microphone and the listening model, and starts listening.</summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _parts.Capture.StartAsync();
        _listening = await _parts.Listener.StartSessionAsync(new SpeechToTextOptions(_language, SpeechSegmentation.Turns), ct);
        _running = Task.WhenAll(Task.Run(LoopAsync), Task.Run(PumpAsync), Task.Run(ListenAsync));
        Post(() => { SetState(VoiceState.Listening); return Task.CompletedTask; });
    }

    /// <summary>Ends the conversation: whatever is being said stops, and the microphone closes.</summary>
    public Task StopAsync() => EndAsync(reason: null);

    private void Post(Func<Task> action) => _loop.Writer.TryWrite(action);

    private async Task LoopAsync()
    {
        await foreach (var action in _loop.Reader.ReadAllAsync())
        {
            try { await action(); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await EndCoreAsync(ex.Message);
            }
        }
    }

    /// <summary>The microphone into the listening model, as it is captured.</summary>
    private async Task PumpAsync()
    {
        try
        {
            await foreach (var pcm in _parts.Capture.Audio.ReadAllAsync(_life.Token))
                await _listening!.AppendAudioAsync(pcm, _life.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Post(() => EndCoreAsync(ex.Message)); }
    }

    private async Task ListenAsync()
    {
        try
        {
            await foreach (var e in _listening!.ReadEventsAsync(_life.Token))
                Post(() => OnHeardAsync(e));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Post(() => EndCoreAsync(ex.Message)); }
    }

    private Task OnHeardAsync(SpeechToTextEvent e)
    {
        switch (e)
        {
            case SpeechActivity { Speaking: true } when Audible && !_parts.Playback.IsPaused:
                // The user started talking over the reply: pause it now, and let the words decide.
                _parts.Playback.Pause();
                _bargeInPending = true;
                _bargeInTimer?.Dispose();
                _bargeInTimer = _parts.Time.CreateTimer(_ => Post(OnBargeInUndecidedAsync), null, BargeInDecision, Timeout.InfiniteTimeSpan);
                break;

            case SpeechActivity { Speaking: true } when _holdTimer is not null:
                // More is coming after an interruption: the turn waits for it.
                _holdTimer.Dispose();
                _holdTimer = null;
                break;

            case TranscriptPartial partial:
                Heard?.Invoke(this, (_held + " " + partial.Text).Trim());
                if (_bargeInPending && !SpokenTurns.IsBackchannel(partial.Text, _language)) return InterruptAsync();
                break;

            case TranscriptFinal final:
                return OnTurnAsync(final.Text);
        }
        return Task.CompletedTask;
    }

    /// <summary>Some of the reply is still to be heard.</summary>
    private bool Audible => _reply is not null && _parts.Playback.QueuedSamples > _parts.Playback.PlayedSamples;

    private async Task OnTurnAsync(string text)
    {
        if (PendingAnswer is { } answer)
        {
            // The reply asked a question and waits on it: this is the answer, not a new turn.
            PendingAnswer = null;
            answer.TrySetResult(text);
            return;
        }

        if (Audible && SpokenTurns.IsBackchannel(text, _language))
        {
            // "Mm-hm" while it talks: an acknowledgement, not a turn.
            if (_bargeInPending) await OnBargeInUndecidedAsync();
            return;
        }

        if (_reply is not null) await InterruptAsync();
        _held = (_held + " " + text).Trim();
        Heard?.Invoke(this, _held);

        if (_interrupted)
        {
            // Just after an interruption, the first fragment ("Wait, stop —") is rarely all of it.
            _holdTimer?.Dispose();
            _holdTimer = _parts.Time.CreateTimer(_ => Post(TakeTurnAsync), null, HoldAfterBargeIn, Timeout.InfiniteTimeSpan);
            return;
        }
        await TakeTurnAsync();
    }

    private Task OnBargeInUndecidedAsync()
    {
        if (!_bargeInPending) return Task.CompletedTask;
        _bargeInPending = false;
        _parts.Playback.Resume();
        return Task.CompletedTask;
    }

    /// <summary>The user's turn is complete: the agent answers it.</summary>
    private Task TakeTurnAsync()
    {
        _holdTimer?.Dispose();
        _holdTimer = null;
        _interrupted = false;
        string text = _held;
        _held = "";
        if (text.Length == 0) return Task.CompletedTask;

        var reply = new VoiceReply(this, text);
        _reply = reply;
        SetState(VoiceState.Thinking);
        return reply.StartAsync();
    }

    /// <summary>Stops the reply: its agent, its synthesis and its sound. The conversation remembers what was heard.</summary>
    private async Task InterruptAsync()
    {
        _bargeInPending = false;
        _bargeInTimer?.Dispose();
        if (_reply is not { } reply) return;
        _reply = null;
        _interrupted = true;
        long heard = _parts.Playback.PlayedSamples;
        _parts.Playback.Stop();
        await reply.StopAsync();
        PendingAnswer?.TrySetCanceled();
        PendingAnswer = null;
        if (reply.HeardText(heard) is { Length: > 0 } said) Remember(reply.UserText, said + " —");
        else _held = (reply.UserText + " " + _held).Trim();   // nothing of the reply was heard: the user is still mid-turn
        SetState(VoiceState.Listening);
    }

    /// <summary>Raised by the playback thread when everything queued has been heard.</summary>
    private async Task OnDrainedAsync()
    {
        if (_reply is not { } reply || !reply.Complete) return;
        _reply = null;
        Remember(reply.UserText, reply.Written);
        await reply.StopAsync();
        if (_endRequested) await EndCoreAsync(reason: null);
        else SetState(VoiceState.Listening);
    }

    /// <summary>Called on the loop when a reply has finished writing and synthesising: if nothing is left to hear, it is over.</summary>
    internal Task OnReplySynthesisedAsync(VoiceReply reply)
    {
        if (_reply != reply) return Task.CompletedTask;
        return _parts.Playback.PlayedSamples >= _parts.Playback.QueuedSamples ? OnDrainedAsync() : Task.CompletedTask;
    }

    private void Remember(string user, string assistant)
    {
        _history.Add(new LlmMessage(LlmRole.User, user));
        if (assistant.Length > 0) _history.Add(new LlmMessage(LlmRole.Assistant, assistant));
    }

    internal void SetStateFromReply(VoiceReply reply, VoiceState state)
    {
        if (_reply == reply) SetState(state);
    }

    private void SetState(VoiceState state)
    {
        if (State == state || State == VoiceState.Ended) return;
        State = state;
        StateChanged?.Invoke(this, state);
    }

    internal void ShowBlock(string markdown) => Shown?.Invoke(this, markdown);

    internal void PostFromReply(Func<Task> action) => Post(action);

    internal VoiceSessionParts Parts => _parts;

    internal string? Language => _language;

    internal IReadOnlyList<IClientTool> SessionTools => [_endTool];

    /// <summary>The user's next turn, for a spoken approval: resolved by the loop when it is heard.</summary>
    internal TaskCompletionSource<string>? PendingAnswer { get; set; }

    private Task EndAsync(string? reason)
    {
        Post(() => EndCoreAsync(reason));
        return _running;
    }

    private async Task EndCoreAsync(string? reason)
    {
        if (State == VoiceState.Ended) return;
        _bargeInTimer?.Dispose();
        _holdTimer?.Dispose();
        if (_reply is { } reply)
        {
            _reply = null;
            _parts.Playback.Stop();
            await reply.StopAsync();
        }
        SetState(VoiceState.Ended);
        _life.Cancel();
        _parts.Capture.Dispose();
        if (_listening is not null) await _listening.DisposeAsync();
        _loop.Writer.TryComplete();
        Ended?.Invoke(this, reason);
    }

    public async ValueTask DisposeAsync()
    {
        await EndAsync(reason: null);
        _life.Dispose();
    }
}

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
    /// <summary>The user is adding instructions while the assistant works: it carries on without a word, and sums up
    /// once everything is done.</summary>
    Working,
    /// <summary>The assistant is talking.</summary>
    Speaking,
    /// <summary>Over: stopped by the user, by the assistant, or by a failure.</summary>
    Ended,
}

/// <summary>What the user meant by words said over an announced action.</summary>
public enum AnswerIntent
{
    /// <summary>Neither here nor there: the action is not done, and the user is told it was not understood.</summary>
    Unclear,
    /// <summary>Do it now.</summary>
    Proceed,
    /// <summary>Do not do it.</summary>
    Cancel,
    /// <summary>Something more to do: added to the work, and the action waits on.</summary>
    Instruction,
}

/// <summary>Runs one turn of the agent for a voice session: the user's words, what was said before, and the handler
/// the reply is written to.</summary>
public interface IVoiceAgent
{
    /// <summary>Runs one turn. <paramref name="ran"/> is told, once the run ends however it ends, the messages it added
    /// to the conversation — what the conversation remembers of the turn.</summary>
    Task RunAsync(string userText, IReadOnlyList<LlmMessage> history, IReadOnlyList<IClientTool> sessionTools,
                  IAIResponseHandler handler, Action<IReadOnlyList<LlmMessage>> ran, CancellationToken ct);

    /// <summary>What words said over an announced action mean for it, when they are not a plain yes or no.</summary>
    Task<AnswerIntent> JudgeAnswerAsync(string announcement, string said, CancellationToken ct);
}

/// <summary>What a voice session is made of: the models it listens and speaks with, the microphone and speaker, the
/// agent it talks to, the handler that shows the conversation, where announced actions are shown with their Stop, and
/// the clock it times barge-in and announcements by.</summary>
public sealed record VoiceSessionParts(
    ISpeechToTextProvider Listener,
    ITextToSpeechProvider Speaker,
    ISpeechCapture Capture,
    ISpeechPlayback Playback,
    IVoiceAgent Agent,
    IAIResponseHandler Shown,
    IVoiceConfirmations Confirmations,
    TimeProvider Time);

/// <summary>
/// A spoken conversation, from the first press of the microphone until it is stopped or the assistant ends it. The
/// microphone streams into the Speech In model listening for turns; each finished turn runs the agent, whose reply is
/// spoken as it is written — first clause, then sentences — while anything speech cannot carry is shown.
/// <para>
/// <b>Barge-in.</b> Speech while the assistant talks pauses it at once; words within half a second stop the reply, and
/// what was actually heard of it — not what was written — is what the conversation remembers. No words — a cough, an
/// "mm-hm" — and it carries on from where it paused. A turn that ends just after an interruption is held a second for
/// the rest of the sentence ("Wait, stop —" is rarely all). Over a question the reply waits on — an announced action,
/// a request to send a page away — the words are its answer instead: the question stays paused until they are
/// finished, and is cut there.
/// </para>
/// <para>
/// <b>Instruction after instruction.</b> Words over a reply still being worked on that are not a stop are added to it
/// (<see cref="VoiceReply.Add"/>): the agent carries on with them, quietly, and sums up at the end.
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
    private ITimer? _bargeInTimer, _holdTimer, _voicedTimer;
    private string _voiced = "";
    private bool _bargeInPending, _interrupted, _endRequested;
    private string _held = "";
    private readonly object _speechGate = new();
    private bool _userSpeaking;
    private TaskCompletionSource _speechTurned = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

    /// <summary>What the user is saying, as it is heard — speech only; a typed turn is not heard.</summary>
    public event EventHandler<string>? Heard;

    /// <summary>The user's turn, said or typed, as the assistant is given it.</summary>
    public event EventHandler<string>? TurnTaken;

    /// <summary>A diagram, table or code the assistant's reply holds, to show over the page.</summary>
    public event EventHandler<string>? Shown;

    /// <summary>What the assistant has said of its reply so far, revealed word by word as the speaker plays it — the
    /// words given to the voice, as they are heard.</summary>
    public event EventHandler<string>? Voiced;

    /// <summary>How often what has been heard of a reply is brought up to date.</summary>
    internal static readonly TimeSpan VoicedEvery = TimeSpan.FromMilliseconds(100);

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
            {
                // Speech over the reply pauses it here, as it is heard, not when the loop gets to it: the loop may be
                // queueing a sentence for the voice.
                if (e is SpeechActivity { Speaking: true } && Audible) _parts.Playback.Pause();
                if (e is SpeechActivity activity) OnUserSpeech(activity.Speaking);
                Post(() => OnHeardAsync(e));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Post(() => EndCoreAsync(ex.Message)); }
    }

    /// <summary>Whether the user is talking, as the listening model hears it — what an action waiting to be stopped waits
    /// on. Set from the listening thread.</summary>
    private void OnUserSpeech(bool speaking)
    {
        TaskCompletionSource turned;
        lock (_speechGate)
        {
            if (_userSpeaking == speaking) return;
            _userSpeaking = speaking;
            turned = _speechTurned;
            _speechTurned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        turned.TrySetResult();
    }

    /// <summary>Completes once the user has said nothing for <paramref name="span"/> — starting over whenever they speak.</summary>
    internal async Task QuietForAsync(TimeSpan span, CancellationToken ct)
    {
        while (true)
        {
            Task turned;
            bool speaking;
            lock (_speechGate)
            {
                turned   = _speechTurned.Task;
                speaking = _userSpeaking;
            }
            if (speaking)
            {
                await turned.WaitAsync(ct);
                continue;
            }

            using var waiting = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var quiet = Task.Delay(span, _parts.Time, waiting.Token);
            if (await Task.WhenAny(quiet, turned) == quiet)
            {
                await quiet;
                return;
            }
            waiting.Cancel();
            try { await quiet; }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        }
    }

    private Task OnHeardAsync(SpeechToTextEvent e)
    {
        switch (e)
        {
            case SpeechActivity { Speaking: true } when Audible && !_bargeInPending:
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
                if (_bargeInPending && !SpokenTurns.IsBackchannel(partial.Text, _language))
                {
                    // Words over a reply still being worked on — or over a question — are something for it, decided when
                    // they are finished: an answer, a stop, or more to do. What it was saying stops now. Over a reply
                    // already written, they are the next turn.
                    if (PendingAnswer is null && _reply is not { Writing: true }) return InterruptAsync();
                    EndBargeIn();
                    _reply?.Silence();
                }
                break;

            case TranscriptFinal final:
                return OnTurnAsync(final.Text);
        }
        return Task.CompletedTask;
    }

    /// <summary>Some of the reply is still to be heard.</summary>
    private bool Audible => _reply is not null && _parts.Playback.QueuedSamples > _parts.Playback.PlayedSamples;

    private async Task OnTurnAsync(string text, bool typed = false)
    {
        if (PendingAnswer is { } answer)
        {
            // The reply asked a question and waits on it: this is the answer, not a new turn — the reply judges what it
            // means — and what was still to be said of the question is not needed.
            PendingAnswer = null;
            EndBargeIn();
            _reply?.Silence();
            answer.TrySetResult(text);
            TurnTaken?.Invoke(this, text);
            return;
        }

        if (Audible && SpokenTurns.IsBackchannel(text, _language))
        {
            // "Mm-hm" while it talks: an acknowledgement, not a turn.
            if (_bargeInPending) await OnBargeInUndecidedAsync();
            return;
        }

        if (_reply is { Writing: true } working && SpokenAnswers.Classify(text, _language) != false
            && working.HasBegun(_parts.Playback.PlayedSamples))
        {
            // More to do while the reply is worked on: added to it, not a turn of its own.
            EndBargeIn();
            working.Add(text);
            TurnTaken?.Invoke(this, text);
            return;
        }

        if (_reply is not null) await InterruptAsync();
        _held = (_held + " " + text).Trim();
        if (!typed) Heard?.Invoke(this, _held);

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

    /// <summary>The words have decided what the speech over the reply was: the half-second wait is over.</summary>
    private void EndBargeIn()
    {
        _bargeInPending = false;
        _bargeInTimer?.Dispose();
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
        TurnTaken?.Invoke(this, text);

        var reply = new VoiceReply(this, text);
        _reply = reply;
        FollowVoice(reply);
        SetState(VoiceState.Thinking);
        return reply.StartAsync();
    }

    /// <summary>Stops the reply: its agent, its synthesis and its sound. The conversation remembers what was heard.</summary>
    private async Task InterruptAsync()
    {
        EndBargeIn();
        if (_reply is not { } reply) return;
        FollowVoice(null);
        _reply = null;
        _interrupted = true;
        long heard = _parts.Playback.PlayedSamples;
        _parts.Playback.Stop();
        await reply.StopAsync();
        PendingAnswer?.TrySetCanceled();
        PendingAnswer = null;
        if (reply.HeardText(heard) is { Length: > 0 } said) Remember(reply, said);
        else _held = (reply.UserTurn + " " + _held).Trim();   // nothing of the reply was heard: the user is still mid-turn
        SetState(VoiceState.Listening);
    }

    /// <summary>Raised by the playback thread when everything queued has been heard.</summary>
    private async Task OnDrainedAsync()
    {
        _reply?.OnDrained();
        if (_reply is not { } reply || !reply.Complete) return;
        await UpdateVoicedAsync();   // all of it has been heard
        FollowVoice(null);
        _reply = null;
        Remember(reply);
        await reply.StopAsync();
        if (_endRequested) await EndCoreAsync(reason: null);
        else if (reply.TakeLeftover() is { Count: > 0 } leftover)
        {
            // Added after the agent's last step: the next turn.
            _held = string.Join(' ', leftover);
            await TakeTurnAsync();
        }
        else SetState(VoiceState.Listening);
    }

    /// <summary>Called on the loop when a reply has finished writing and synthesising: if nothing is left to hear, it is over.</summary>
    internal Task OnReplySynthesisedAsync(VoiceReply reply)
    {
        if (_reply != reply) return Task.CompletedTask;
        return _parts.Playback.PlayedSamples >= _parts.Playback.QueuedSamples ? OnDrainedAsync() : Task.CompletedTask;
    }

    /// <summary>
    /// What the conversation keeps of a turn: the run as it happened — the request, the calls made and what came of each,
    /// the reply — so a later turn knows what was done, not only what was said. A reply the user cut into is kept as far
    /// as it was heard (<paramref name="heard"/>). A run that kept nothing is kept as what was asked and said.
    /// </summary>
    private void Remember(VoiceReply reply, string? heard = null)
    {
        var said = heard is null ? reply.Written : heard + " —";
        if (reply.Ran is not { Count: > 0 } ran)
        {
            _history.Add(new LlmMessage(LlmRole.User, reply.UserTurn));
            if (said.Length > 0) _history.Add(new LlmMessage(LlmRole.Assistant, said));
            return;
        }

        var kept = ran.ToList();
        if (heard is not null)
        {
            if (kept[^1] is { Role: LlmRole.Assistant, ToolCalls: null or { Count: 0 } } final) kept[^1] = final with { Text = said };
            else if (kept[^1].Role == LlmRole.User) kept.Add(new LlmMessage(LlmRole.Assistant, said));
        }
        _history.AddRange(kept);
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

    /// <summary>Follows a reply's voice as it plays — or stops following, given none.</summary>
    private void FollowVoice(VoiceReply? reply)
    {
        _voicedTimer?.Dispose();
        _voicedTimer = null;
        _voiced = "";
        if (reply is not null)
            _voicedTimer = _parts.Time.CreateTimer(_ => Post(UpdateVoicedAsync), null, VoicedEvery, VoicedEvery);
    }

    /// <summary>What has been heard of the reply now, raised when it has grown.</summary>
    private Task UpdateVoicedAsync()
    {
        if (_reply is not { } reply) return Task.CompletedTask;
        var text = reply.HeardText(_parts.Playback.PlayedSamples);
        if (text.Length > 0 && text != _voiced)
        {
            _voiced = text;
            Voiced?.Invoke(this, text);
        }
        return Task.CompletedTask;
    }

    /// <summary>A turn typed rather than said — in a meeting, a noisy room — answered aloud like any other.</summary>
    public void Submit(string text)
    {
        if (!string.IsNullOrWhiteSpace(text)) Post(() => OnTurnAsync(text.Trim(), typed: true));
    }

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
        FollowVoice(null);
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

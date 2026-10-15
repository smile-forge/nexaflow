using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common.Speech;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services.Voice;

/// <summary>
/// One spoken reply: the agent writing it, the voice reading it as it is written, and the speaker playing it. It is
/// the handler the agent writes to — passing everything on to the handler that shows the conversation, and speaking
/// the prose of each draft as it grows.
/// <para>
/// <b>Actions are announced, not asked about.</b> Asked to delete a file, the assistant does not ask whether to delete
/// it: it says what it will do, in fixed words — "Ok. I will delete file test.zip." — shows Stop beside it, and once
/// that has been heard and three seconds have passed with nothing said, does it and says "File deleted.", which ends
/// the turn. A clear yes goes ahead at once; "stop" — said or clicked — stops it and says so. Sending a page's
/// sensitive contents away is the exception: that waits for a yes.
/// </para>
/// <para>
/// <b>Instruction after instruction.</b> More to do, said while the reply is still being worked on — over an
/// announcement, as the Disambiguation model judges it — is added to the work: the agent is given it at its next step, as a message typed during a chat reply is. What was
/// being said stops, and the acknowledgements that would have followed are held — the reply goes quiet and shows
/// what it is doing — until everything is done, when the model's own short summary is all that is said. An action
/// announced meanwhile is shown, not said, in red with Abort, and goes ahead once the user has been quiet a while.
/// </para>
/// <para>The segmenter and the session's state are touched only on the session's loop; the agent's callbacks post there.</para>
/// </summary>
internal sealed class VoiceReply : IAIResponseHandler
{
    /// <summary>How long an announced action waits, once it has been heard and the user is quiet, for someone to stop it.</summary>
    internal static readonly TimeSpan StopWindow = TimeSpan.FromSeconds(3);

    private readonly VoiceSession _session;
    private readonly IAIResponseHandler _shown;
    private readonly CancellationTokenSource _stop = new();
    private readonly CancellationTokenSource _writingStop;
    private readonly ReplySegmenter _segmenter;
    private readonly List<string> _segmentText = [];
    private readonly List<long> _segmentSamples = [];
    private readonly HashSet<int> _segmentsSynthesised = [];
    private readonly object _audioGate = new();
    private readonly List<string> _added = [];
    private readonly List<string> _pending = [];
    private readonly TaskCompletionSource _workingQuietly = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ITextToSpeechSession? _voice;
    private Task _writing = Task.CompletedTask, _sounding = Task.CompletedTask;
    private string _draft = "";
    private long _firstSample = -1, _droppedSamples;
    private int _lastSegment = -1, _silencedThrough = -1;
    private bool _written, _synthesised, _acted;
    private volatile bool _quiet;
    private AnnouncedActions? _running;
    private (int Segment, TaskCompletionSource Heard)? _awaitingHeard;

    public VoiceReply(VoiceSession session, string userText)
    {
        _session     = session;
        _shown       = session.Parts.Shown;
        UserText     = userText;
        _segmenter   = new ReplySegmenter(session.Language);
        _writingStop = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
    }

    public string UserText { get; }

    /// <summary>What the user asked of this reply: the turn that began it, and every instruction added while it was
    /// worked on — what the conversation remembers they said.</summary>
    public string UserTurn => string.Join(' ', [UserText, .. _added]);

    /// <summary>The reply as written — what the conversation remembers when all of it was heard.</summary>
    public string Written { get; private set; } = "";

    private IReadOnlyList<LlmMessage>? _ran;

    /// <summary>The run as it happened — the request, each call and what came of it, the reply — once it has ended; null
    /// before then, or for a run that kept nothing.</summary>
    public IReadOnlyList<LlmMessage>? Ran => Volatile.Read(ref _ran);

    private IReadOnlyList<LlmMessage>? _condensed;

    /// <summary>The conversation this reply continued, its older part condensed to make room; null when it fitted as it was.</summary>
    public IReadOnlyList<LlmMessage>? Condensed => Volatile.Read(ref _condensed);

    /// <summary>Written, and every word of it synthesised and queued.</summary>
    public bool Complete => _written && _synthesised;

    /// <summary>The agent is still at work on it: what the user adds now can still be given to it.</summary>
    public bool Writing => !_written;

    public async Task StartAsync()
    {
        _shown.BeginResponse(UserText);
        _voice    = await _session.Parts.Speaker.StartSessionAsync(new TextToSpeechOptions(), _stop.Token);
        _sounding = Task.Run(SoundAsync);
        var history = _session.History.ToList();
        _writing  = Task.Run(async () =>
        {
            try
            {
                // What needs thought is said to at once — "Let me think about that." — and the model reasons before it
                // answers; anything else is answered straight away, without reasoning, as quickly as it was asked.
                var agent = _session.Parts.Agent;
                bool think = await agent.NeedsThoughtAsync(UserText, history, _writingStop.Token);
                if (think) _session.PostFromReply(async () => await SayAsync(Str.Get("Shell.Voice.LetMeThink")));
                await agent.RunAsync(UserText, history, _session.SessionTools, this, ran => Volatile.Write(ref _ran, ran),
                                     condensed => Volatile.Write(ref _condensed, condensed),
                                     think ? LlmThinking.Allowed : LlmThinking.Off, _writingStop.Token);
            }
            catch (OperationCanceledException) when (_writingStop.IsCancellationRequested) { }
            finally { _session.PostFromReply(OnWrittenAsync); }
        });
    }

    /// <summary>The turn is over though the agent would go on: what has been said is the reply.</summary>
    private void EndWriting() => _writingStop.Cancel();

    private async Task OnWrittenAsync()
    {
        _written = true;
        if (_voice is not null && !_stop.IsCancellationRequested) await _voice.CompleteAsync();
    }

    /// <summary>Something of it has been done or heard — the user's words are no longer the rest of the turn it began
    /// with, but something to add to it.</summary>
    public bool HasBegun(long playedSamples) => _acted || HeardText(playedSamples).Length > 0;

    /// <summary>
    /// An instruction the user gives while the reply is worked on, on the loop: what is being said stops, the agent is
    /// given the instruction at its next step, and the reply goes quiet — holding its acknowledgements — until
    /// everything is done.
    /// </summary>
    internal void Add(string instruction)
    {
        Silence();
        _added.Add(instruction);
        lock (_pending) _pending.Add(instruction);
        if (_quiet) return;
        _quiet = true;
        _workingQuietly.TrySetResult();
        _session.SetStateFromReply(this, VoiceState.Working);
    }

    /// <summary>Instructions added after the agent's last step — the next turn's, once this one is over.</summary>
    internal IReadOnlyList<string> TakeLeftover()
    {
        lock (_pending)
        {
            var left = _pending.ToList();
            _pending.Clear();
            return left;
        }
    }

    /// <summary>The voice's audio onto the speaker, as it comes; each segment's length kept, to map what was heard back
    /// to the words.</summary>
    private async Task SoundAsync()
    {
        try
        {
            await foreach (var e in _voice!.ReadAudioAsync(_stop.Token))
            {
                switch (e)
                {
                    case SpeechAudioChunk chunk:
                        if (!Enqueue(chunk)) break;
                        _session.PostFromReply(() => { _session.SetStateFromReply(this, VoiceState.Speaking); return Task.CompletedTask; });
                        break;
                    case SpeechSegmentEnd segmentEnd:
                        _session.PostFromReply(() =>
                        {
                            _segmentsSynthesised.Add(segmentEnd.Segment);
                            CheckHeard();
                            return Task.CompletedTask;
                        });
                        break;
                }
            }
            _session.PostFromReply(() =>
            {
                _synthesised = true;
                return _session.OnReplySynthesisedAsync(this);
            });
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    /// <summary>A chunk onto the speaker, its length kept — unless what it belongs to was cut short.</summary>
    private bool Enqueue(SpeechAudioChunk chunk)
    {
        var playback = _session.Parts.Playback;
        lock (_audioGate)
        {
            if (chunk.Segment <= _silencedThrough) return false;
            if (_firstSample < 0) _firstSample = playback.QueuedSamples;
            while (_segmentSamples.Count <= chunk.Segment) _segmentSamples.Add(0);
            _segmentSamples[chunk.Segment] += chunk.Pcm.Length / 2;
            playback.Enqueue(chunk.Pcm);
            return true;
        }
    }

    /// <summary>Cuts short everything said so far, on the loop — a question has its answer, or the user has more to
    /// ask: what is playing stops, and what is still being synthesised of it is dropped. What is said next is heard. What
    /// was dropped is counted, so what comes after still maps back to its words.</summary>
    internal void Silence()
    {
        var playback = _session.Parts.Playback;
        lock (_audioGate)
        {
            _silencedThrough = _lastSegment;
            long unplayed = playback.QueuedSamples - playback.PlayedSamples;
            if (unplayed <= 0) return;
            _droppedSamples += unplayed;
            playback.Stop();
        }
    }

    private void PostSilence() => _session.PostFromReply(() =>
    {
        Silence();
        return Task.CompletedTask;
    });

    /// <summary>Raised on the loop whenever the speaker has played everything queued.</summary>
    internal void OnDrained() => CheckHeard();

    /// <summary>An announcement waited on has been heard once it is synthesised and everything queued has played.</summary>
    private void CheckHeard()
    {
        if (_awaitingHeard is not { } awaited || !_segmentsSynthesised.Contains(awaited.Segment)) return;
        var playback = _session.Parts.Playback;
        if (playback.PlayedSamples < playback.QueuedSamples) return;
        _awaitingHeard = null;
        awaited.Heard.TrySetResult();
    }

    /// <summary>What of the reply was heard, given the speaker's count: every segment wholly played, and of the one cut
    /// short its words in proportion.</summary>
    public string HeardText(long playedSamples)
    {
        lock (_audioGate)
        {
            if (_firstSample < 0) return "";
            // A segment cut short was let go of whole: what was dropped of it stands as heard, so the segments after
            // it line up with what the speaker played.
            long heard = playedSamples - _firstSample + _droppedSamples;
            var said = new List<string>();
            for (int i = 0; i < _segmentText.Count && i < _segmentSamples.Count && heard > 0; i++)
            {
                long length = _segmentSamples[i];
                if (heard >= length)
                {
                    said.Add(_segmentText[i]);
                    heard -= length;
                    continue;
                }
                var words = _segmentText[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int count = (int)(words.Length * heard / Math.Max(1, length));
                if (count > 0) said.Add(string.Join(' ', words.Take(count)));
                break;
            }
            return string.Join(' ', said);
        }
    }

    /// <summary>Speaks the parts in order; returns the number of the last segment queued, or -1. Nothing is said while
    /// the reply works quietly: its acknowledgements wait for the summary.</summary>
    private async Task<int> SpeakAsync(IReadOnlyList<SpokenPart> parts)
    {
        int last = -1;
        if (_voice is null || _stop.IsCancellationRequested || _quiet) return last;
        foreach (var part in parts)
        {
            var segment = part switch
            {
                ShownBlock block => Show(block),
                SpokenSegment s  => s,
                _ => null,
            };
            if (segment is null) continue;
            lock (_audioGate) _segmentText.Add(segment.Text);
            last = await _voice.SpeakAsync(segment.Text, segment.Language, _stop.Token);
            lock (_audioGate) _lastSegment = last;
        }
        return last;

        SpokenSegment Show(ShownBlock block)
        {
            _session.ShowBlock(block.Markdown);
            return block.Reference;
        }
    }

    private Task<int> SayAsync(string line) => SpeakAsync([new SpokenSegment(line, _session.Language)]);

    public async Task StopAsync()
    {
        _stop.Cancel();
        if (_voice is not null) await _voice.DisposeAsync();
        try { await Task.WhenAll(_writing, _sounding); }
        catch (Exception) { /* a reply cut short has nothing more to say */ }
        _awaitingHeard?.Heard.TrySetCanceled();
        _writingStop.Dispose();
        _stop.Dispose();
    }

    // ── IAIResponseHandler: everything shown as before, the prose spoken as well ───────────────────────────────

    public void BeginResponse(string userText) { }

    public void ReportProgress(string message) => _shown.ReportProgress(message);

    public void ReportContextUse(int tokens, int window) => _shown.ReportContextUse(tokens, window);

    public void ShowReplyDraft(string draftMarkdown)
    {
        _shown.ShowReplyDraft(draftMarkdown);
        _session.PostFromReply(() =>
        {
            _draft = draftMarkdown;
            return _quiet ? Task.CompletedTask : SpeakAsync(_segmenter.Update(draftMarkdown));
        });
    }

    public void EndReplyDraft(bool keep)
    {
        _shown.EndReplyDraft(keep);
        _session.PostFromReply(async () =>
        {
            // A draft kept stands as the account of what is being done — said in full; one taken away is replaced,
            // and what was not yet said of it never will be.
            if (keep && !_quiet) await SpeakAsync(_segmenter.Finish(_draft));
            _segmenter.Restart();
            _draft = "";
        });
    }

    /// <summary>What the batch is doing, shown as it starts — "Deleting file test.zip" — so a reply working quietly
    /// still says what it is at.</summary>
    public void OnToolBatchStarting(IReadOnlyList<ToolCall> batch)
    {
        _shown.OnToolBatchStarting(batch);
        _shown.ReportProgress(AnnouncedActions.Of(batch).Doing);
        _session.PostFromReply(() =>
        {
            _acted = true;
            return Task.CompletedTask;
        });
    }

    /// <summary>An announced batch has run: what came of it is said in fixed words, and the turn ends there — the
    /// model is not asked to say it again. Working quietly, it is held for the summary, and the agent goes on to what
    /// was added.</summary>
    public void OnToolBatchFinished(IReadOnlyList<ToolRunResult> results)
    {
        _shown.OnToolBatchFinished(results);
        if (_running is not { } actions) return;
        _running = null;
        if (_quiet) return;
        string after = actions.After(results);
        _session.PostFromReply(async () =>
        {
            Written = actions.Announcement + " " + after;
            await SayAsync(after);
        });
        EndWriting();
    }

    public Task<bool> RequestToolBatchApprovalAsync(string explanationMarkdown, IReadOnlyList<ToolCall> batch, CancellationToken ct)
        => AnnounceAsync(AnnouncedActions.Of(batch), endsTurn: true, ct);

    public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
        => AnnounceAsync(AnnouncedActions.OfPlan(plan), endsTurn: false, ct);

    /// <summary>
    /// Says what will be done and shows it with Go ahead and Stop; goes ahead on a click, a yes, or three seconds of quiet
    /// once it has been heard; stops on Stop or a no — and then says so, and ends the turn. Words that are not a plain yes
    /// or no are judged by the Disambiguation model: more to do is added to the work, and the announcement is then shown
    /// held, in red with Abort, waiting for the user to be quiet; anything it cannot make out stops it, saying so.
    /// </summary>
    private async Task<bool> AnnounceAsync(AnnouncedActions actions, bool endsTurn, CancellationToken ct)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool first = !_quiet;   // announced before the user began adding instructions
        var spoken = AwaitAnswer();
        _session.PostFromReply(async () =>
        {
            _segmenter.Restart();   // what the model wrote before the action is not said: the announcement is
            _draft = "";
            if (_quiet) return;     // the user is giving instructions: shown, not said
            int segment = await SayAsync(actions.Announcement);
            _awaitingHeard = (segment, heard);
            CheckHeard();
        });

        var counting  = CancellationTokenSource.CreateLinkedTokenSource(race.Token);
        var countdown = CountDownAsync(heard.Task, counting.Token);
        bool held = _quiet;
        var shown   = CancellationTokenSource.CreateLinkedTokenSource(race.Token);
        var clicked = Confirm(actions, held, first, shown.Token);
        var intent  = AnswerIntent.Proceed;
        Task decided;
        try
        {
            while (true)
            {
                decided = await Task.WhenAny(held ? [clicked, spoken.Task, countdown] : [clicked, spoken.Task, countdown, _workingQuietly.Task])
                                    .WaitAsync(ct);
                if (decided == _workingQuietly.Task)
                {
                    // Instructions began over it: no longer said, but shown held until the user has been quiet a while.
                    held = true;
                    shown.Cancel();
                    await Settled(clicked);
                    shown.Dispose();
                    shown   = CancellationTokenSource.CreateLinkedTokenSource(race.Token);
                    clicked = Confirm(actions, held, first, shown.Token);
                    continue;
                }
                if (decided != spoken.Task) break;

                string said = spoken.Task.Result;
                intent = await JudgeAsync(actions, said, ct);
                if (intent != AnswerIntent.Instruction) break;

                // More to do: added to the work, and the action waits on for the user to be quiet again.
                _session.PostFromReply(() =>
                {
                    Add(said);
                    return Task.CompletedTask;
                });
                spoken = AwaitAnswer();
                counting.Cancel();
                await Settled(countdown);
                counting.Dispose();
                counting  = CancellationTokenSource.CreateLinkedTokenSource(race.Token);
                countdown = CountDownAsync(heard.Task, counting.Token);
            }
        }
        finally
        {
            race.Cancel();
            await Settled(clicked);
            await Settled(countdown);
            shown.Dispose();
            counting.Dispose();
            StopAwaiting(spoken);
        }

        bool proceed;
        string stopped = actions.Stopped;
        if (decided == clicked)
        {
            PostSilence();   // a click answers it as words would: the rest of the announcement is not said
            proceed = await clicked;
        }
        else if (decided == spoken.Task)
        {
            proceed = intent == AnswerIntent.Proceed;
            if (intent == AnswerIntent.Unclear) stopped = actions.NotUnderstood;
        }
        else proceed = true;   // nobody stopped it

        if (proceed)
        {
            if (endsTurn) _running = actions;
            return true;
        }

        // Working quietly, the summary says what was not done; the agent goes on with what was added.
        if (_quiet) return false;
        _session.PostFromReply(async () =>
        {
            Written = actions.Announcement + " " + stopped;
            await SayAsync(stopped);
        });
        EndWriting();
        return false;

        static async Task Settled(Task task)
        {
            try { await task; }
            catch (OperationCanceledException) { }
        }
    }

    /// <summary>A yes or a no is told by its words; anything else is put to the Disambiguation model, and a model that
    /// fails leaves it unclear.</summary>
    private async Task<AnswerIntent> JudgeAsync(AnnouncedActions actions, string said, CancellationToken ct)
    {
        switch (SpokenAnswers.Classify(said, _session.Language))
        {
            case true:  return AnswerIntent.Proceed;
            case false: return AnswerIntent.Cancel;
        }
        try { return await _session.Parts.Agent.JudgeAnswerAsync(actions.Announcement, said, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return AnswerIntent.Unclear; }
    }

    /// <summary>The user's next words, as the answer to what the reply asks: the session hands them here rather than
    /// taking them as a turn.</summary>
    private TaskCompletionSource<string> AwaitAnswer()
    {
        var answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _session.PostFromReply(() =>
        {
            _session.PendingAnswer = answer;
            return Task.CompletedTask;
        });
        return answer;
    }

    private void StopAwaiting(TaskCompletionSource<string> answer) => _session.PostFromReply(() =>
    {
        if (_session.PendingAnswer == answer) _session.PendingAnswer = null;
        return Task.CompletedTask;
    });

    private Task<bool> Confirm(AnnouncedActions actions, bool held, bool first, CancellationToken ct)
        => _session.Parts.Confirmations.ShowAsync(held
            ? new VoiceConfirmation(actions.Held(first), actions.ProceedLabel, Str.Get("Shell.Voice.Abort"), IsHeld: true)
            : new VoiceConfirmation(actions.Announcement, actions.ProceedLabel, Str.Get("Shell.Voice.Stop")), ct);

    /// <summary>Waits for the announcement to be heard — or for it not to be said, the user giving instructions — then
    /// for the stop window to pass with the user quiet.</summary>
    private async Task CountDownAsync(Task heard, CancellationToken ct)
    {
        await Task.WhenAny(heard, _workingQuietly.Task).WaitAsync(ct);
        await _session.QuietForAsync(StopWindow, ct);
    }

    /// <summary>Sending a page's sensitive contents away is not done on silence: it waits for a yes, said or clicked.</summary>
    public async Task<bool> RequestContextReleaseAsync(IReadOnlyList<string> destinations, CancellationToken ct)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        string question = Str.Format("Shell.Voice.ContextReleaseFormat", string.Join(", ", destinations));
        var spoken = AwaitAnswer();
        _session.PostFromReply(async () => await SayAsync(question));
        var clicked = _session.Parts.Confirmations.ShowAsync(
            new VoiceConfirmation(question, Str.Get("Shell.Voice.Send"), Str.Get("Shell.Voice.DontSend")), race.Token);

        var first = await Task.WhenAny(clicked, spoken.Task).WaitAsync(ct);
        if (first == clicked) PostSilence();
        bool send = first == clicked ? await clicked : SpokenAnswers.Classify(spoken.Task.Result, _session.Language) == true;
        race.Cancel();
        try { await clicked; } catch (OperationCanceledException) { }
        StopAwaiting(spoken);
        return send;
    }

    /// <summary>The final answer, spoken. After working quietly it is the model's summary of everything done — said
    /// whole, since nothing of it was said along the way.</summary>
    public void ShowFinal(string finalMarkdown)
    {
        _shown.ShowFinal(finalMarkdown);
        _session.PostFromReply(() =>
        {
            Written = finalMarkdown;
            if (_quiet)
            {
                _quiet = false;
                _segmenter.Restart();
            }
            return SpeakAsync(_segmenter.Finish(finalMarkdown));
        });
    }

    public void Abort() => _shown.Abort();

    public void Enqueue(string text) => _shown.Enqueue(text);

    /// <summary>What the user added since the agent's last step, said or typed, and anything queued on the page.</summary>
    public IReadOnlyList<string> TakeInterjections() => [.. TakeLeftover(), .. _shown.TakeInterjections()];

    public IReadOnlyList<string> DrainQueue() => [.. TakeLeftover(), .. _shown.DrainQueue()];
}

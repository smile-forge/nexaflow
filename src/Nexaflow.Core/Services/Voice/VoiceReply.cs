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
/// the turn. A clear yes goes ahead at once; "stop", or anything it cannot take for a yes, stops it and says so. Sending
/// a page's sensitive contents away is the exception: that waits for a yes.
/// </para>
/// <para>The segmenter and the session's state are touched only on the session's loop; the agent's callbacks post there.</para>
/// </summary>
internal sealed class VoiceReply : IAIResponseHandler
{
    /// <summary>How long an announced action waits, once it has been heard, for someone to stop it.</summary>
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
    private ITextToSpeechSession? _voice;
    private Task _writing = Task.CompletedTask, _sounding = Task.CompletedTask;
    private string _draft = "";
    private long _firstSample = -1;
    private int _lastSegment = -1, _silencedThrough = -1;
    private bool _written, _synthesised;
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

    /// <summary>The reply as written — what the conversation remembers when all of it was heard.</summary>
    public string Written { get; private set; } = "";

    /// <summary>Written, and every word of it synthesised and queued.</summary>
    public bool Complete => _written && _synthesised;

    public async Task StartAsync()
    {
        _shown.BeginResponse(UserText);
        _voice    = await _session.Parts.Speaker.StartSessionAsync(new TextToSpeechOptions(), _stop.Token);
        _sounding = Task.Run(SoundAsync);
        var history = _session.History.ToList();
        _writing  = Task.Run(async () =>
        {
            try { await _session.Parts.Agent.RunAsync(UserText, history, _session.SessionTools, this, _writingStop.Token); }
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

    /// <summary>Cuts short everything said so far, on the loop — a question has its answer, so the rest of it is not
    /// needed: what is playing stops, and what is still being synthesised of it is dropped. What is said next is heard.</summary>
    internal void Silence()
    {
        lock (_audioGate) _silencedThrough = _lastSegment;
        var playback = _session.Parts.Playback;
        if (playback.QueuedSamples > playback.PlayedSamples) playback.Stop();
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
            long heard = playedSamples - _firstSample;
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

    /// <summary>Speaks the parts in order; returns the number of the last segment queued, or -1.</summary>
    private async Task<int> SpeakAsync(IReadOnlyList<SpokenPart> parts)
    {
        int last = -1;
        if (_voice is null || _stop.IsCancellationRequested) return last;
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
            _session.OnSaid(segment.Text);
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

    public void ShowReplyDraft(string draftMarkdown)
    {
        _shown.ShowReplyDraft(draftMarkdown);
        _session.PostFromReply(() =>
        {
            _draft = draftMarkdown;
            return SpeakAsync(_segmenter.Update(draftMarkdown));
        });
    }

    public void EndReplyDraft(bool keep)
    {
        _shown.EndReplyDraft(keep);
        _session.PostFromReply(async () =>
        {
            // A draft kept stands as the account of what is being done — said in full; one taken away is replaced,
            // and what was not yet said of it never will be.
            if (keep) await SpeakAsync(_segmenter.Finish(_draft));
            _segmenter.Restart();
            _draft = "";
        });
    }

    public void OnToolBatchStarting(IReadOnlyList<ToolCall> batch) => _shown.OnToolBatchStarting(batch);

    /// <summary>An announced batch has run: what came of it is said in fixed words, and the turn ends there — the
    /// model is not asked to say it again.</summary>
    public void OnToolBatchFinished(IReadOnlyList<ToolRunResult> results)
    {
        _shown.OnToolBatchFinished(results);
        if (_running is not { } actions) return;
        _running = null;
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
    /// Says what will be done and shows it with Go ahead and Stop; goes ahead on a click, a spoken yes, or three seconds
    /// of nothing once it has been heard; stops on Stop or on anything else said — and then says so, and ends the turn.
    /// </summary>
    private async Task<bool> AnnounceAsync(AnnouncedActions actions, bool endsTurn, CancellationToken ct)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var spoken = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var heard  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _session.PostFromReply(async () =>
        {
            _segmenter.Restart();   // what the model wrote before the action is not said: the announcement is
            _draft = "";
            int segment = await SayAsync(actions.Announcement);
            _awaitingHeard = (segment, heard);
            CheckHeard();
            _session.PendingAnswer = spoken;
        });

        var clicked   = _session.Parts.Confirmations.ShowAsync(
            new VoiceConfirmation(actions.Announcement, actions.ProceedLabel, Str.Get("Shell.Voice.Stop")), race.Token);
        var countdown = CountDownAsync(heard.Task, race.Token);

        var first = await Task.WhenAny(clicked, spoken.Task, countdown).WaitAsync(ct);
        bool proceed;
        string? stopped = null;
        if (first == clicked)
        {
            PostSilence();   // a click answers it as words would: the rest of the announcement is not said
            proceed = await clicked;
            stopped = actions.Stopped;
        }
        else if (first == spoken.Task)
        {
            var answer = SpokenAnswers.Classify(spoken.Task.Result, _session.Language);
            proceed = answer == true;
            stopped = answer == false ? actions.Stopped : actions.NotUnderstood;
        }
        else proceed = true;

        race.Cancel();
        try { await Task.WhenAll(clicked, countdown); } catch (OperationCanceledException) { }
        _session.PostFromReply(() =>
        {
            if (_session.PendingAnswer == spoken) _session.PendingAnswer = null;
            return Task.CompletedTask;
        });

        if (proceed)
        {
            if (endsTurn) _running = actions;
            return true;
        }

        _session.PostFromReply(async () =>
        {
            Written = actions.Announcement + " " + stopped;
            await SayAsync(stopped!);
        });
        EndWriting();
        return false;
    }

    /// <summary>Waits for the announcement to be heard, then for the stop window.</summary>
    private async Task CountDownAsync(Task heard, CancellationToken ct)
    {
        await heard.WaitAsync(ct);
        await Task.Delay(StopWindow, _session.Parts.Time, ct);
    }

    /// <summary>Sending a page's sensitive contents away is not done on silence: it waits for a yes, said or clicked.</summary>
    public async Task<bool> RequestContextReleaseAsync(IReadOnlyList<string> destinations, CancellationToken ct)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        string question = Str.Format("Shell.Voice.ContextReleaseFormat", string.Join(", ", destinations));
        var spoken = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _session.PostFromReply(async () =>
        {
            await SayAsync(question);
            _session.PendingAnswer = spoken;
        });
        var clicked = _session.Parts.Confirmations.ShowAsync(
            new VoiceConfirmation(question, Str.Get("Shell.Voice.Send"), Str.Get("Shell.Voice.DontSend")), race.Token);

        var first = await Task.WhenAny(clicked, spoken.Task).WaitAsync(ct);
        if (first == clicked) PostSilence();
        bool send = first == clicked ? await clicked : SpokenAnswers.Classify(spoken.Task.Result, _session.Language) == true;
        race.Cancel();
        try { await clicked; } catch (OperationCanceledException) { }
        _session.PostFromReply(() =>
        {
            if (_session.PendingAnswer == spoken) _session.PendingAnswer = null;
            return Task.CompletedTask;
        });
        return send;
    }

    public void ShowFinal(string finalMarkdown)
    {
        _shown.ShowFinal(finalMarkdown);
        _session.PostFromReply(() =>
        {
            Written = finalMarkdown;
            return SpeakAsync(_segmenter.Finish(finalMarkdown));
        });
    }

    public void Abort() => _shown.Abort();

    public void Enqueue(string text) => _shown.Enqueue(text);

    public IReadOnlyList<string> TakeInterjections() => _shown.TakeInterjections();

    public IReadOnlyList<string> DrainQueue() => _shown.DrainQueue();
}

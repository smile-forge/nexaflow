using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common.Speech;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Voice;

/// <summary>
/// One spoken reply: the agent writing it, the voice reading it as it is written, and the speaker playing it. It is
/// the handler the agent writes to — passing everything on to the handler that shows the conversation, and speaking
/// the prose of each draft as it grows. Approvals are asked aloud as well as shown, and answered by whichever comes
/// first: a click, or a "yes" or "no".
/// <para>The segmenter and the session's state are touched only on the session's loop; the agent's callbacks post there.</para>
/// </summary>
internal sealed class VoiceReply : IAIResponseHandler
{
    private readonly VoiceSession _session;
    private readonly IAIResponseHandler _shown;
    private readonly CancellationTokenSource _stop = new();
    private readonly ReplySegmenter _segmenter;
    private readonly List<string> _segmentText = [];
    private readonly List<long> _segmentSamples = [];
    private readonly object _audioGate = new();
    private ITextToSpeechSession? _voice;
    private Task _writing = Task.CompletedTask, _sounding = Task.CompletedTask;
    private string _draft = "";
    private long _firstSample = -1;
    private bool _written, _synthesised;

    public VoiceReply(VoiceSession session, string userText)
    {
        _session   = session;
        _shown     = session.Parts.Shown;
        UserText   = userText;
        _segmenter = new ReplySegmenter(session.Language);
    }

    public string UserText { get; }

    /// <summary>The reply as written — what the conversation remembers when all of it was heard.</summary>
    public string Written { get; private set; } = "";

    /// <summary>Written, and every word of it synthesised and queued.</summary>
    public bool Complete => _written && _synthesised;

    public async Task StartAsync()
    {
        _voice    = await _session.Parts.Speaker.StartSessionAsync(new TextToSpeechOptions(), _stop.Token);
        _sounding = Task.Run(SoundAsync);
        var history = _session.History.ToList();
        _writing  = Task.Run(async () =>
        {
            try { await _session.Parts.Agent.RunAsync(UserText, history, _session.SessionTools, this, _stop.Token); }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            finally { _session.PostFromReply(OnWrittenAsync); }
        });
    }

    private async Task OnWrittenAsync()
    {
        _written = true;
        if (_voice is not null && !_stop.IsCancellationRequested) await _voice.CompleteAsync();
    }

    /// <summary>The voice's audio onto the speaker, as it comes; each segment's length kept, to map what was heard back
    /// to the words.</summary>
    private async Task SoundAsync()
    {
        var playback = _session.Parts.Playback;
        try
        {
            await foreach (var e in _voice!.ReadAudioAsync(_stop.Token))
            {
                if (e is not SpeechAudioChunk chunk) continue;
                lock (_audioGate)
                {
                    if (_firstSample < 0) _firstSample = playback.QueuedSamples;
                    while (_segmentSamples.Count <= chunk.Segment) _segmentSamples.Add(0);
                    _segmentSamples[chunk.Segment] += chunk.Pcm.Length / 2;
                    playback.Enqueue(chunk.Pcm);
                }
                _session.PostFromReply(() => { _session.SetStateFromReply(this, VoiceState.Speaking); return Task.CompletedTask; });
            }
            _session.PostFromReply(() =>
            {
                _synthesised = true;
                return _session.OnReplySynthesisedAsync(this);
            });
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
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

    private async Task SpeakAsync(IReadOnlyList<SpokenPart> parts)
    {
        if (_voice is null || _stop.IsCancellationRequested) return;
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
            await _voice.SpeakAsync(segment.Text, segment.Language, _stop.Token);
        }

        SpokenSegment Show(ShownBlock block)
        {
            _session.ShowBlock(block.Markdown);
            return block.Reference;
        }
    }

    public async Task StopAsync()
    {
        _stop.Cancel();
        if (_voice is not null) await _voice.DisposeAsync();
        try { await Task.WhenAll(_writing, _sounding); }
        catch (Exception) { /* a reply cut short has nothing more to say */ }
        _stop.Dispose();
    }

    // ── IAIResponseHandler: everything shown as before, the prose spoken as well ───────────────────────────────

    public void BeginResponse(string userText) => _shown.BeginResponse(userText);

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

    public void OnToolBatchFinished(IReadOnlyList<ToolRunResult> results) => _shown.OnToolBatchFinished(results);

    public Task<bool> RequestToolBatchApprovalAsync(string explanationMarkdown, IReadOnlyList<ToolCall> batch, CancellationToken ct)
        => AskAsync(explanationMarkdown, token => _shown.RequestToolBatchApprovalAsync(explanationMarkdown, batch, token), ct);

    public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
        => AskAsync(plan.Title, token => _shown.RequestPlanApprovalAsync(plan, token), ct);

    public Task<bool> RequestContextReleaseAsync(IReadOnlyList<string> destinations, CancellationToken ct)
        => AskAsync(SpokenQuestions.ContextRelease(destinations, _session.Language), token => _shown.RequestContextReleaseAsync(destinations, token), ct);

    /// <summary>
    /// Says the question — the rest of what the model wrote, then "shall I go ahead?" — and takes the first answer: a
    /// click on what is shown, or a spoken yes or no. Anything else said is not an answer, and the question stands.
    /// </summary>
    private async Task<bool> AskAsync(string explanation, Func<CancellationToken, Task<bool>> shown, CancellationToken ct)
    {
        using var race = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var clicked = shown(race.Token);
        var spoken = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _session.PostFromReply(async () =>
        {
            var parts = new List<SpokenPart>(_segmenter.Finish(explanation));
            string? language = parts.OfType<SpokenSegment>().LastOrDefault()?.Language ?? _session.Language;
            parts.Add(new SpokenSegment(SpokenQuestions.GoAhead(language), language));
            await SpeakAsync(parts);
            _segmenter.Restart();
            _session.PendingAnswer = spoken;
        });

        while (true)
        {
            var first = await Task.WhenAny(clicked, spoken.Task).WaitAsync(ct);
            if (first == clicked)
            {
                _session.PostFromReply(() => { if (_session.PendingAnswer == spoken) _session.PendingAnswer = null; return Task.CompletedTask; });
                return await clicked;
            }
            if (SpokenAnswers.Classify(spoken.Task.Result, _session.Language) is { } yes)
            {
                race.Cancel();
                try { await clicked; } catch (OperationCanceledException) { }
                return yes;
            }
            var next = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _session.PostFromReply(() => { _session.PendingAnswer = next; return Task.CompletedTask; });
            spoken = next;
        }
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

/// <summary>What the assistant asks aloud, in the conversation's language.</summary>
internal static class SpokenQuestions
{
    public static string GoAhead(string? language) => language switch
    {
        "fr" => "Je continue ?",
        "de" => "Soll ich das machen?",
        _    => "Shall I go ahead?",
    };

    public static string ContextRelease(IReadOnlyList<string> destinations, string? language) => language switch
    {
        "fr" => $"Cette page contient des informations sensibles. Je l'envoie à {string.Join(", ", destinations)} ?",
        "de" => $"Diese Seite enthält vertrauliche Informationen. Soll ich sie an {string.Join(", ", destinations)} senden?",
        _    => $"This page holds sensitive information. Shall I send it to {string.Join(", ", destinations)}?",
    };
}

using Nexaflow.Providers.Common;
using OpenAI;
using OpenAI.Realtime;

namespace Nexaflow.Providers.OpenAI;

#pragma warning disable OPENAI002 // the realtime API is marked experimental in the SDK

/// <summary>
/// OpenAI's speech-to-text models (gpt-4o-transcribe, gpt-4o-mini-transcribe, whisper-1) through a realtime
/// transcription session: audio streams up as it is captured, the server finds where each utterance ends, and its
/// text comes back as it is recognised.
/// </summary>
public sealed class OpenAISpeechToTextProvider : ISpeechToTextProvider
{
    public string Name => OpenAILlmProvider.ProviderName;

    private readonly OpenAIConfig               _config;
    private readonly IBackgroundActivityManager _activityManager;
    private readonly string                     _model;
    private OpenAIClient?                       _client;

    public string? LastModelListError { get; private set; }

    public OpenAISpeechToTextProvider(IBackgroundActivityManager activityManager, OpenAIConfig config, ProviderModel model)
    {
        _activityManager = activityManager;
        _config          = config;
        _model           = model.Model;
    }

    private OpenAIClient Client => _client ??= OpenAIClients.Create(_config);

    /// <summary>OpenAI's API, unless the base URL points at a compatible server on this machine.</summary>
    public DataResidency Residency => EndpointResidency.Of(_config.BaseUrl, DataResidency.LeavesMachine);

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_config.ApiKey);

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await Client.GetOpenAIModelClient().GetModelsAsync(ct);
            LastModelListError = null;
            return result.Value.Select(m => m.Id).Where(IsSpeechToText).Order().ToList();
        }
        catch (Exception ex)
        {
            LastModelListError = ex.Message;
            return [];
        }
    }

    /// <summary>The models the transcription endpoint takes: the transcribe family, and Whisper.</summary>
    internal static bool IsSpeechToText(string model)
        => model.Contains("transcribe", StringComparison.OrdinalIgnoreCase)
           || model.StartsWith("whisper", StringComparison.OrdinalIgnoreCase);

    public async Task<ISpeechToTextSession> StartSessionAsync(SpeechToTextOptions options, CancellationToken ct = default)
    {
        var activity = _activityManager.StartActivity($"OpenAI transcription ({_model})…");
        try
        {
            var session = await Client.GetRealtimeClient().StartTranscriptionSessionAsync(cancellationToken: ct);
            await session.ConfigureTranscriptionSessionAsync(new RealtimeTranscriptionSessionOptions
            {
                AudioOptions = new RealtimeTranscriptionSessionAudioOptions
                {
                    InputAudioOptions = new RealtimeTranscriptionSessionInputAudioOptions
                    {
                        AudioFormat               = new RealtimePcmAudioFormat(),
                        AudioTranscriptionOptions = new RealtimeAudioTranscriptionOptions { Model = _model, Language = options.Language },
                        TurnDetection             = new RealtimeServerVadTurnDetection(),
                    },
                },
            }, ct);
            return new OpenAITranscriptionSession(session, activity);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            activity.Fail("canceled");
            throw;
        }
        catch (Exception ex)
        {
            activity.Fail(ex.Message);
            throw new LlmProviderException($"OpenAI transcription could not start: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// One realtime transcription session. Audio is converted to the 24 kHz the API takes as it is sent; the server's
/// updates are read through a <see cref="TranscriptionTracker"/>, which decides what each means and when the session,
/// once completed, has nothing left to say.
/// </summary>
internal sealed class OpenAITranscriptionSession(RealtimeSessionClient session, IActivityHandle activity) : ISpeechToTextSession
{
    /// <summary>The realtime API's PCM rate.</summary>
    private const int ApiRate = 24_000;

    /// <summary>How long the last utterance may take to arrive once the audio has ended, before the session gives up
    /// on it rather than hanging the caller.</summary>
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(15);

    private readonly PcmResampler            _resampler = new(ISpeechToTextSession.SampleRate, ApiRate);
    private readonly TranscriptionTracker    _tracker   = new();
    private readonly CancellationTokenSource _ending    = new();
    private int _settled;

    public async ValueTask AppendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct = default)
    {
        var converted = _resampler.Convert(pcm.Span);
        if (converted.Length > 0) await session.SendInputAudioAsync(BinaryData.FromBytes(converted), ct);
    }

    public async ValueTask CompleteAsync(CancellationToken ct = default)
    {
        _tracker.CompletionRequested();
        await session.CommitPendingAudioAsync(ct);
        _ending.CancelAfter(CompletionTimeout);
    }

    public async IAsyncEnumerable<SpeechToTextEvent> ReadEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _ending.Token);
        await using var updates = session.ReceiveUpdatesAsync(linked.Token).GetAsyncEnumerator(linked.Token);

        while (!_tracker.IsFinished)
        {
            RealtimeServerUpdate update;
            try
            {
                if (!await updates.MoveNextAsync()) break;
                update = updates.Current;
            }
            catch (OperationCanceledException) when (_ending.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                break;   // the last utterance never came — end rather than hang
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Settle(ex.Message);
                throw new LlmProviderException($"OpenAI transcription failed: {ex.Message}", ex);
            }

            var events = update switch
            {
                RealtimeServerUpdateInputAudioBufferSpeechStarted                       => _tracker.SpeechStarted(),
                RealtimeServerUpdateInputAudioBufferSpeechStopped                       => _tracker.SpeechStopped(),
                RealtimeServerUpdateInputAudioBufferCommitted c                         => _tracker.Committed(c.ItemId),
                RealtimeServerUpdateConversationItemInputAudioTranscriptionDelta d      => _tracker.Delta(d.ItemId, d.Delta),
                RealtimeServerUpdateConversationItemInputAudioTranscriptionCompleted f => _tracker.Completed(f.ItemId, f.Transcript),
                RealtimeServerUpdateConversationItemInputAudioTranscriptionFailed f    => Fail(f.Error),
                RealtimeServerUpdateError e when IsEmptyCommit(e.Error)                 => _tracker.NothingToCommit(),
                RealtimeServerUpdateError e                                             => Fail(e.Error),
                _                                                                      => [],
            };
            foreach (var e in events) yield return e;
        }
        Settle(null);
    }

    /// <summary>Committing an empty buffer is how a session that already sent its last utterance ends — the server
    /// says so as an error, and it is not one.</summary>
    private static bool IsEmptyCommit(RealtimeError? error)
        => error?.Code?.Contains("commit_empty", StringComparison.OrdinalIgnoreCase) == true;

    private IReadOnlyList<SpeechToTextEvent> Fail(RealtimeError? error)
    {
        var message = error?.Message ?? "unknown error";
        Settle(message);
        throw new LlmProviderException($"OpenAI transcription failed: {message}");
    }

    private void Settle(string? failure)
    {
        if (Interlocked.Exchange(ref _settled, 1) == 1) return;
        if (failure is null) activity.Complete();
        else                 activity.Fail(failure);
    }

    public ValueTask DisposeAsync()
    {
        Settle(null);
        _ending.Dispose();
        session.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// What a transcription session's updates add up to: the text of each utterance as it grows, its final text, and —
/// once the caller has said no more audio is coming — whether every utterance the server took has been transcribed.
/// Free of the SDK, so the bookkeeping can be tested without a connection.
/// </summary>
internal sealed class TranscriptionTracker
{
    private readonly Dictionary<string, string> _growing  = [];
    private readonly HashSet<string>            _awaiting = [];
    private bool _completionRequested;
    private bool _finalCommitAnswered;

    /// <summary>The caller has completed the session, its last commit has been answered, and no utterance the
    /// server took is still waiting for its text.</summary>
    public bool IsFinished => _completionRequested && _finalCommitAnswered && _awaiting.Count == 0;

    public void CompletionRequested() => _completionRequested = true;

    public IReadOnlyList<SpeechToTextEvent> SpeechStarted() => [new SpeechActivity(true)];

    public IReadOnlyList<SpeechToTextEvent> SpeechStopped() => [new SpeechActivity(false)];

    public IReadOnlyList<SpeechToTextEvent> Committed(string itemId)
    {
        _awaiting.Add(itemId);
        if (_completionRequested) _finalCommitAnswered = true;
        return [];
    }

    /// <summary>The final commit found nothing left — the server had already taken every utterance.</summary>
    public IReadOnlyList<SpeechToTextEvent> NothingToCommit()
    {
        if (_completionRequested) _finalCommitAnswered = true;
        return [];
    }

    public IReadOnlyList<SpeechToTextEvent> Delta(string itemId, string? delta)
    {
        if (string.IsNullOrEmpty(delta)) return [];
        var text = _growing.GetValueOrDefault(itemId, string.Empty) + delta;
        _growing[itemId] = text;
        return [new TranscriptPartial(text.Trim())];
    }

    public IReadOnlyList<SpeechToTextEvent> Completed(string itemId, string? transcript)
    {
        _growing.Remove(itemId);
        _awaiting.Remove(itemId);
        var text = transcript?.Trim() ?? string.Empty;
        return text.Length == 0 ? [] : [new TranscriptFinal(text)];
    }
}

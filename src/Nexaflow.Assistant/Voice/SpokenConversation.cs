using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common.Audio;
using Nexaflow.Providers.Common;
using Nexaflow.Session.Services;

namespace Nexaflow.Assistant.Voice;

/// <summary>
/// One spoken conversation with the workspace's assistant, from the moment the microphone opens to the moment it closes: the
/// microphone and speakers it is held through, the agent that answers each turn on the page in front of the user, and the name its
/// turns are taken under while it is held in the bar, which the workspace's models let go of once it ends. A page that keeps the
/// conversation itself, the conversation page, takes its turns under its own name.
/// <para>Its events are raised on whichever thread the conversation is on, except <see cref="Ended"/>: raised once, on the UI thread.</para>
/// </summary>
public sealed class SpokenConversation : IAsyncDisposable
{
    private readonly AIService _service;
    private readonly IUiThread _ui;
    private readonly ISpeechCapture _capture;
    private readonly ISpeechPlayback _playback;
    private readonly VoiceSession _session;
    private int _released;

    /// <param name="service">The workspace's assistant: its models listen, answer and speak, and its platform's devices carry the sound.</param>
    /// <param name="ui">The UI thread, where each turn runs and the conversation ends.</param>
    /// <param name="currentPage">The page in front of the user as each turn is taken.</param>
    /// <param name="includeContext">Whether the page's context goes with each turn, read as each is taken.</param>
    /// <param name="shown">What shows the replies: the page's own surface where it keeps the conversation, the bar's otherwise.</param>
    /// <param name="confirmations">What shows an announced action, or a question, until it is answered.</param>
    /// <param name="heldByPage">The page in front of the user keeps the conversation itself, under its own name.</param>
    public SpokenConversation(AIService service, IUiThread ui, Func<IPageViewModel?> currentPage, Func<bool> includeContext,
                              IAIResponseHandler shown, IVoiceConfirmations confirmations, bool heldByPage)
    {
        _service = service;
        _ui      = ui;
        ConversationName = heldByPage ? null : "voice-" + Guid.NewGuid().ToString("N");

        (_capture, _playback) = service.SpeechDevices.Open();
        try
        {
            var parts = new VoiceSessionParts(service.SpeechToText!, service.TextToSpeech!, _capture, _playback,
                                              new PageVoiceAgent(service, ui, currentPage, includeContext, pageKeepsConversation: heldByPage),
                                              shown, confirmations, TimeProvider.System);
            _session = new VoiceSession(parts, service.ConversationLanguage);
        }
        catch
        {
            _capture.Dispose();
            _playback.Dispose();
            throw;
        }

        _session.StateChanged += (_, state) => StateChanged?.Invoke(this, state);
        _session.Heard        += (_, text)  => Heard?.Invoke(this, text);
        _session.TurnTaken    += (_, text)  => TurnTaken?.Invoke(this, text);
        _session.Voiced       += (_, text)  => Voiced?.Invoke(this, text);
        _session.Shown        += (_, md)    => Shown?.Invoke(this, md);
        // Ended from inside the session, which cannot be let go of from within itself: it ends on the UI thread.
        _session.Ended        += (_, why)   => _ui.Post(() => _ = EndAsync(why));
    }

    /// <summary>The name the conversation's turns are taken under while it is held in the bar; null where the page keeps it.</summary>
    public string? ConversationName { get; }

    /// <summary>What has been said, oldest first: both sides.</summary>
    public IReadOnlyList<LlmMessage> History => _session.History;

    /// <inheritdoc cref="VoiceSession.StateChanged"/>
    public event EventHandler<VoiceState>? StateChanged;

    /// <inheritdoc cref="VoiceSession.Heard"/>
    public event EventHandler<string>? Heard;

    /// <inheritdoc cref="VoiceSession.TurnTaken"/>
    public event EventHandler<string>? TurnTaken;

    /// <inheritdoc cref="VoiceSession.Voiced"/>
    public event EventHandler<string>? Voiced;

    /// <inheritdoc cref="VoiceSession.Shown"/>
    public event EventHandler<string>? Shown;

    /// <summary>The conversation is over — stopped, ended by the assistant, or failed (the message) — and its microphone and speakers
    /// are let go of. Raised once, on the UI thread.</summary>
    public event EventHandler<string?>? Ended;

    /// <summary>Opens the microphone and speakers and starts listening. Where it fails, let the conversation go with
    /// <see cref="DisposeAsync"/>.</summary>
    public Task StartAsync() => _session.StartAsync();

    /// <summary>A turn typed while talking, answered aloud.</summary>
    public void Submit(string text) => _session.Submit(text);

    /// <summary>Ends the conversation: whatever is being said stops, and the microphone closes.</summary>
    public async Task StopAsync()
    {
        await _session.StopAsync();
        await EndAsync(reason: null);
    }

    /// <summary>Lets the conversation go without saying it ended: one that never started.</summary>
    public async ValueTask DisposeAsync() => await ReleaseAsync();

    private async Task EndAsync(string? reason)
    {
        if (await ReleaseAsync()) Ended?.Invoke(this, reason);
    }

    /// <summary>Lets go of the session and the devices, and has the workspace's models forget a conversation held in the bar: once,
    /// whichever way it ends. True for the call that did.</summary>
    private async Task<bool> ReleaseAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) == 1) return false;
        await _session.DisposeAsync();
        if (ConversationName is { } ended) _ = _service.ForgetConversationAsync(ended);
        _capture.Dispose();
        _playback.Dispose();
        return true;
    }
}

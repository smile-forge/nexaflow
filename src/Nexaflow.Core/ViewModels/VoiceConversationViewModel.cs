using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Services;
using Nexaflow.Core.Services.Voice;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common.Audio;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.ViewModels;

/// <summary>A diagram, table or code the spoken reply holds, shown over the page until it is put away.</summary>
public sealed record ShownBlockViewModel(string Markdown);

/// <summary>
/// The input bar in voice mode: one window's spoken conversation. The assistant has one line above the box — what it
/// is saying now, exactly as it is read aloud, or what it is doing — and an action about to be done is shown there with
/// Stop beside it. The user's last turn shows above the box a while, and what they say fills the box as it is heard. Anything the reply holds that speech
/// cannot carry is shown over the page until Escape or the end of the conversation puts it away. On a page that shows
/// AI replies itself, the conversation page, the replies land there instead.
/// </summary>
public sealed partial class VoiceConversationViewModel(Dispatcher ui) : ObservableObject, IAIResponseHandler, IVoiceConfirmations
{
    private VoiceSession? _session;
    private ISpeechCapture? _capture;
    private ISpeechPlayback? _playback;
    private TaskCompletionSource<bool>? _confirmed;
    private bool _inline;

    /// <summary>A conversation is under way.</summary>
    [ObservableProperty] private bool _isActive;

    /// <summary>How long the user's last turn stays above the box before it fades.</summary>
    internal static readonly TimeSpan TurnShownFor = TimeSpan.FromSeconds(10);

    private DispatcherTimer? _turnFade;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Line))]
    private VoiceState _state = VoiceState.Ended;

    /// <summary>The user's last turn, as the assistant was given it — the box is free for the next one.</summary>
    [ObservableProperty] private string _turnText = "";

    /// <summary>The last turn is still shown; it fades once <see cref="TurnShownFor"/> has passed.</summary>
    [ObservableProperty] private bool _isTurnFresh;

    /// <summary>What the assistant has said of its reply so far, word by word as it is heard — the words given to the
    /// voice, revealed as they are played.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Line))]
    private string _saying = "";

    /// <summary>What the assistant is doing between words: the tool it runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Line))]
    private string? _progress;

    /// <summary>The assistant's one line: its reply as it is said, and between words what it is doing — "Thinking…",
    /// "Working… Deleting file test.zip". The reply stays until the next turn begins.</summary>
    public string Line => State switch
    {
        VoiceState.Working                          => $"{Str.Get("Shell.AiBar.Voice.Working")} {Progress}".TrimEnd(),
        // Speaking, but not a word heard yet: the voice is still on its way.
        VoiceState.Thinking or VoiceState.Speaking when Saying.Length == 0 => Str.Get("Shell.AiBar.Voice.Thinking"),
        _ when Saying.Length > 0                    => Saying,
        _                                           => Str.Get("Shell.AiBar.Voice.Listening"),
    };

    /// <summary>An action announced and waiting to be stopped — or a question waiting for a yes; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirming))]
    private VoiceConfirmation? _confirmation;

    public bool IsConfirming => Confirmation is not null;

    public ObservableCollection<ShownBlockViewModel> Shown { get; } = [];

    /// <summary>What the user is saying, as it is heard. Raised on the UI thread.</summary>
    public event Action<string>? Heard;

    /// <summary>The user's turn has gone to the assistant, so what fills the box is no longer it. Raised on the UI thread.</summary>
    public event Action<string>? TurnTaken;

    /// <summary>The conversation is over — stopped, ended by the assistant, or failed (the message). Raised on the UI thread.</summary>
    public event Action<string?>? Ended;

    /// <summary>The user asked to carry the conversation on in a conversation page: what was said, oldest first.</summary>
    public event Action<IReadOnlyList<LlmMessage>>? OpenInConversationRequested;

    /// <summary>Opens the microphone and speakers and starts listening.</summary>
    /// <param name="inline">The page's own handler, for a page that shows AI replies itself; null for the bar.</param>
    public async Task StartAsync(AIService service, Func<IPageViewModel?> currentPage, Func<bool> includeContext,
                                 IAIResponseHandler? inline, string language)
    {
        if (IsActive) return;
        _inline   = inline is not null;
        Saying    = "";
        _playback = new SpeakerPlayback();
        _capture  = new CommunicationsCapture(_playback.EndpointId);
        var parts = new VoiceSessionParts(service.SpeechToText!, service.TextToSpeech!, _capture, _playback,
                                          new PageVoiceAgent(service, ui, currentPage, includeContext, pageKeepsConversation: _inline),
                                          inline ?? this, this, TimeProvider.System);
        var session = new VoiceSession(parts, language);
        session.StateChanged += (_, state) => OnUi(() => State = state);
        session.Heard        += (_, text)  => OnUi(() => Heard?.Invoke(text));
        session.TurnTaken    += (_, text)  => OnUi(() => { ShowTurn(text); TurnTaken?.Invoke(text); });
        session.Voiced       += (_, text)  => OnUi(() => Saying = text);
        session.Shown        += (_, md)    => OnUi(() => { if (!_inline) Shown.Add(new ShownBlockViewModel(md)); });
        session.Ended        += (_, why)   => OnUi(() => _ = FinishAsync(why));
        _session = session;
        try
        {
            await session.StartAsync();
            IsActive = true;
        }
        catch
        {
            await TearDownAsync();
            throw;
        }
    }

    /// <summary>Shows the user's turn above the box for <see cref="TurnShownFor"/>, then lets it fade.</summary>
    private void ShowTurn(string text)
    {
        TurnText    = text;
        IsTurnFresh = true;
        _turnFade ??= new DispatcherTimer(TurnShownFor, DispatcherPriority.Normal, (_, _) =>
        {
            _turnFade!.Stop();
            IsTurnFresh = false;
        }, ui);
        _turnFade.Stop();
        _turnFade.Start();
    }

    /// <summary>Ends the conversation: whatever is being said stops, and the microphone closes. A conversation started
    /// again starts afresh.</summary>
    public async Task StopAsync()
    {
        if (!IsActive && _session is null) return;
        if (_session is { } session) await session.StopAsync();
        await FinishAsync(reason: null);
    }

    /// <summary>A turn typed into the box while talking, answered aloud.</summary>
    public void Submit(string text) => _session?.Submit(text);

    private async Task FinishAsync(string? reason)
    {
        if (_session is null && !IsActive) return;
        await TearDownAsync();
        Ended?.Invoke(reason);
    }

    private async Task TearDownAsync()
    {
        var session = _session;
        _session = null;
        if (session is not null) await session.DisposeAsync();
        _capture?.Dispose();
        _playback?.Dispose();
        _capture  = null;
        _playback = null;
        _confirmed?.TrySetCanceled();
        Confirmation = null;
        Shown.Clear();
        _turnFade?.Stop();
        Saying      = "";
        TurnText    = "";
        IsTurnFresh = false;
        Progress  = null;
        IsActive  = false;
        State     = VoiceState.Ended;
    }

    /// <summary>Puts away what the spoken replies showed over the page.</summary>
    public void PutAwayShown() => Shown.Clear();

    /// <summary>Carries the conversation on in a conversation page — voice mode ends, and the page holds what was said.</summary>
    [RelayCommand]
    private async Task OpenInConversation()
    {
        var said = _session?.History.ToList() ?? [];
        await StopAsync();
        OpenInConversationRequested?.Invoke(said);
    }

    [RelayCommand]
    private void Proceed() => Confirm(true);

    [RelayCommand]
    private void StopAction() => Confirm(false);

    private void Confirm(bool proceed)
    {
        Confirmation = null;
        _confirmed?.TrySetResult(proceed);
    }

    /// <summary>Shows the announced action — or the question — in the bar until a button answers it or it is answered aloud.</summary>
    public async Task<bool> ShowAsync(VoiceConfirmation confirmation, CancellationToken ct)
    {
        var confirmed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        OnUi(() =>
        {
            _confirmed?.TrySetCanceled();
            _confirmed   = confirmed;
            Confirmation = confirmation;
        });
        await using var _ = ct.Register(() => OnUi(() =>
        {
            if (_confirmed == confirmed) Confirmation = null;
            confirmed.TrySetCanceled(ct);
        }));
        return await confirmed.Task;
    }

    private void OnUi(Action action)
    {
        if (ui.CheckAccess()) action();
        else ui.BeginInvoke(action);
    }

    // ── The bar as the reply's handler: what is said is shown from what was said, not from the draft ──────────

    public void BeginResponse(string userText) => OnUi(() => { Saying = ""; Progress = null; });

    public void ReportProgress(string message) => OnUi(() => Progress = message);

    public void ShowFinal(string finalMarkdown) => OnUi(() => Progress = null);

    public void Abort() => OnUi(() => Progress = null);

    /// <summary>A voice reply announces its actions itself; were the bar asked, it would show the question with its
    /// buttons and wait for one.</summary>
    public Task<bool> RequestToolBatchApprovalAsync(string explanationMarkdown, IReadOnlyList<ToolCall> batch, CancellationToken ct)
        => ShowAsync(new VoiceConfirmation(SpeechText.Of(explanationMarkdown), Str.Get("Shell.Voice.Proceed"), Str.Get("Shell.Voice.Stop")), ct);

    /// <inheritdoc cref="RequestToolBatchApprovalAsync"/>
    public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
        => ShowAsync(new VoiceConfirmation(plan.Title, Str.Get("Shell.Voice.Proceed"), Str.Get("Shell.Voice.Stop")), ct);
}

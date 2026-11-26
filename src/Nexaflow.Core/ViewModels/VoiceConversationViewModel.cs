using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Services;
using Nexaflow.Assistant.Voice;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common.Audio;
using Nexaflow.Providers.Common;
using Nexaflow.Localization;

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
    private readonly IUiThread _thread = new WpfUiThread(ui);

    /// <summary>The conversation under way; null when none is.</summary>
    private SpokenConversation? _conversation;

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

    /// <summary>How much of the model's context the conversation fills, as its last step said; null until one has.</summary>
    [ObservableProperty] private ContextUse? _contextUse;

    /// <summary>The assistant's one line: its reply as it is said, and between words what it is doing — "Thinking…",
    /// "Working… Deleting file test.zip". The reply stays until the next turn begins.</summary>
    public string Line => State switch
    {
        VoiceState.Working                          => $"{Str.Get("Shell.AiBar.Voice.Working")} {Progress}".TrimEnd(),
        // The model that answers made way for a picture: nothing said now could be answered till it is back.
        VoiceState.Paused                           => Str.Get("Shell.AiBar.Voice.Working"),
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

    /// <summary>The name the spoken conversation's turns are taken under while it is held here, in the bar; null on a page that
    /// holds the conversation itself, and once it has ended.</summary>
    public string? ConversationName => _conversation?.ConversationName;

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
    public async Task StartAsync(AIService service, Func<IPageViewModel?> currentPage, Func<bool> includeContext, IAIResponseHandler? inline)
    {
        if (IsActive) return;
        _inline    = inline is not null;
        Saying     = "";
        ContextUse = null;
        var conversation = new SpokenConversation(service, _thread, currentPage, includeContext, inline ?? this, this, heldByPage: _inline);
        conversation.StateChanged += (_, state) => OnUi(() => State = state);
        conversation.Heard        += (_, text)  => OnUi(() => Heard?.Invoke(text));
        conversation.TurnTaken    += (_, text)  => OnUi(() => { ShowTurn(text); TurnTaken?.Invoke(text); });
        conversation.Voiced       += (_, text)  => OnUi(() => Saying = text);
        conversation.Shown        += (_, md)    => OnUi(() => { if (!_inline) Shown.Add(new ShownBlockViewModel(md)); });
        conversation.Ended        += (_, why)   => OnUi(() => Finish(conversation, why));
        _conversation = conversation;
        try
        {
            await conversation.StartAsync();
            IsActive = true;
        }
        catch
        {
            _conversation = null;
            await conversation.DisposeAsync();
            Reset();
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
        if (_conversation is { } conversation) await conversation.StopAsync();
    }

    /// <summary>A turn typed into the box while talking, answered aloud.</summary>
    public void Submit(string text) => _conversation?.Submit(text);

    /// <summary><paramref name="conversation"/> has ended: the bar leaves voice mode, unless a newer conversation already holds it.</summary>
    private void Finish(SpokenConversation conversation, string? reason)
    {
        if (!ReferenceEquals(_conversation, conversation)) return;
        _conversation = null;
        Reset();
        Ended?.Invoke(reason);
    }

    /// <summary>Clears what the bar shows of a conversation.</summary>
    private void Reset()
    {
        _confirmed?.TrySetCanceled();
        Confirmation = null;
        Shown.Clear();
        _turnFade?.Stop();
        Saying      = "";
        TurnText    = "";
        IsTurnFresh = false;
        Progress    = null;
        IsActive    = false;
        State       = VoiceState.Ended;
    }

    /// <summary>Puts away what the spoken replies showed over the page.</summary>
    public void PutAwayShown() => Shown.Clear();

    /// <summary>Carries the conversation on in a conversation page — voice mode ends, and the page holds what was said.</summary>
    [RelayCommand]
    private async Task OpenInConversation()
    {
        var said = _conversation?.History.ToList() ?? [];
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

    public void ReportContextUse(int tokens, int window) => OnUi(() => ContextUse = new(tokens, window));

    public void Abort() => OnUi(() => Progress = null);

    /// <summary>A voice reply announces its actions itself; were the bar asked, it would show the question with its
    /// buttons and wait for one.</summary>
    public Task<bool> RequestToolBatchApprovalAsync(string explanationMarkdown, IReadOnlyList<ToolCall> batch, CancellationToken ct)
        => ShowAsync(VoiceConfirmation.Asking(SpeechText.Of(explanationMarkdown)), ct);

    /// <inheritdoc cref="RequestToolBatchApprovalAsync"/>
    public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
        => ShowAsync(VoiceConfirmation.Asking(plan.Title), ct);
}

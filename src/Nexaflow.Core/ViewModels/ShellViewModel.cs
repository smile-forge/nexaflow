using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.AIChat;
using Nexaflow.Features.AIChat.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Core.ViewModels;

/// <summary>Which surface the AI response overlay is currently showing.</summary>
public enum AiOverlayState { Message, Approval, Running }

public partial class ShellViewModel : ObservableObject, IWindowHost, IToolApprovalCoordinator
{
    // ── IWindowHost ───────────────────────────────────────────────────────

    public Window Window { get; init; } = null!;

    bool IWindowHost.IsFocused
    {
        get => _isFocused;
        set
        {
            _isFocused = value;
            // When this window gains focus, surface any messages no window has toasted yet —
            // covers the daemon's first window opening after a windowless post (update/voice).
            if (value) DrainPendingToasts();
        }
    }
    private bool _isFocused;

    IReadOnlyList<Page> IWindowHost.Tabs => RootPane.Pages;

    void IWindowHost.AddTab(Page tab)        => RootPane.Add(tab);
    void IWindowHost.RemoveTab(Page tab)     => RootPane.Remove(tab);
    void IWindowHost.BringToFront(Page tab)  => RootPane.BringToFront(tab);
    void IWindowHost.SetActiveTab(Page tab)
    {
        RootPane.BringToFront(tab);
        RootPane.ActivePage = tab;
    }

    void IWindowHost.ShowError(string message) => ShowError("Error", message);
    void IWindowHost.ShowNotification(string message)
        => MessageCenter.Instance.Post(new NotificationItem { Title = "Info", Body = message });
    void IWindowHost.ShowConfirmation(string title, string prompt, Action onConfirm, Action? onCancel)
        => ShowConfirmation(title, prompt, onConfirm, onCancel);

    void IWindowHost.ShowPrompt(string title, string label, string initialValue,
                                Action<string> onConfirm, Action? onCancel)
        => ShowPrompt(title, label, initialValue, onConfirm, onCancel);

    void IWindowHost.AddRibbonPin(RibbonPinRequest request)
        => Ribbon?.PinFromHandlerCommand.Execute(request);

    // ── Pane (the strip of pages + active page) ───────────────────────────
    // The shell hosts a single root pane today. In future this becomes a
    // tree (SplitPaneNode = left + right Panes) — bind UI to RootPaneNode.

    public Pane RootPane { get; } = new();
    public IPaneNode RootPaneNode => RootPane;

    // Legacy facades — kept so existing call sites and bindings keep working.
    public ObservableCollection<Page> Tabs => RootPane.Pages;

    public Page? ActiveTab
    {
        get => RootPane.ActivePage;
        set => RootPane.ActivePage = value;
    }

    public UserControl? CurrentPage => RootPane.ActivePage?.GetOrCreateContent();

    private void WireRootPane()
    {
        // The breadcrumb bar binds Pane.ActivePage.Breadcrumbs directly (see PaneView.xaml),
        // so the shell only has to re-notify its computed ActiveTab/CurrentPage facades here.
        RootPane.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Pane.ActivePage))
            {
                OnPropertyChanged(nameof(ActiveTab));
                OnPropertyChanged(nameof(CurrentPage));
            }
        };
    }

    // ── Notifications ─────────────────────────────────────────────────────
    // The inbox is a single app-wide store shared by every window (see MessageCenter).
    public ObservableCollection<NotificationItem> Notifications => MessageCenter.Instance.Messages;

    [ObservableProperty] private int  _unreadCount;
    [ObservableProperty] private bool _notificationsOpen;

    // ── Background activity ───────────────────────────────────────────────
    private readonly BackgroundActivityManager _activityManager;

    public ObservableCollection<BackgroundTask> BackgroundTasks => _activityManager.Tasks;

    // ── AI interaction ────────────────────────────────────────────────────
    [ObservableProperty] private string  _aiInputText      = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleVoiceCommand))]
    private bool _aiIsBusy;
    [ObservableProperty] private bool    _voiceActive;
    [ObservableProperty] private string? _aiHandlerSymbol;
    [ObservableProperty] private bool    _aiIsListening;
    [ObservableProperty] private bool    _aiInputIsAiTyping;

    /// <summary>True while the mic is actively capturing — disables the input box.</summary>
    [ObservableProperty] private bool    _isRecording;

    /// <summary>Mic button enabled only when host capabilities pass and the model is downloaded.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleVoiceCommand))]
    private bool _voiceAvailable;

    /// <summary>When on, the active page's context is attached to conversational AI calls.</summary>
    [ObservableProperty] private bool    _includeContext = true;

    /// <summary>Inline completion proposed by the clear-winner query handler; null = none.</summary>
    [ObservableProperty] private string? _aiCompletionSuggestion;

    private CancellationTokenSource? _handlerEvalCts;

    // Created when a send begins; CancelAi cancels it. Token plumbing into the
    // providers is deferred — this only backs the Submit↔Cancel UI for now.
    private CancellationTokenSource? _aiSendCts;

    // ── Toast (transient presentation of a message) ────────────────────────
    // One toast shows at a time; further posts queue. ActiveToast is the message currently displayed.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToastVisible))]
    private NotificationItem? _activeToast;

    public bool ToastVisible => ActiveToast is not null;

    private readonly Queue<NotificationItem> _toastQueue = new();
    private CancellationTokenSource? _toastDismissCts;

    /// <summary>Default time a toast stays up, by severity (overridden by NotificationItem.ToastDuration).</summary>
    private static TimeSpan DefaultToastDuration(NotificationItem m) => m.Severity switch
    {
        // Messages with actions (Update, Retry) stay up long enough to act on.
        _ when m.HasActions          => TimeSpan.FromMinutes(5),
        MessageSeverity.Update       => TimeSpan.FromMinutes(5),
        MessageSeverity.Error        => TimeSpan.FromSeconds(8),
        MessageSeverity.Warning      => TimeSpan.FromSeconds(8),
        _                            => TimeSpan.FromSeconds(6),
    };

    /// <summary>Queues a message as a toast; shows it immediately if nothing is currently displayed.</summary>
    private void ShowToast(NotificationItem message)
    {
        _toastQueue.Enqueue(message);
        if (ActiveToast is null) DisplayNextToast();
    }

    private void DisplayNextToast()
    {
        _toastDismissCts?.Cancel();
        if (_toastQueue.Count == 0) { ActiveToast = null; return; }

        var message = _toastQueue.Dequeue();
        ActiveToast = message;

        _toastDismissCts = new CancellationTokenSource();
        var token = _toastDismissCts.Token;
        var duration = message.ToastDuration ?? DefaultToastDuration(message);
        _ = Task.Delay(duration, token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                Application.Current.Dispatcher.Invoke(() => { if (ActiveToast == message) DisplayNextToast(); });
        }, TaskScheduler.Default);
    }

    private void ShowError(string title, string message)
        => MessageCenter.Instance.Post(new NotificationItem
        {
            Title = title, Body = message, Severity = MessageSeverity.Error, ShowToast = true,
        });

    public void ShowErrorToast(string message) => ShowError("Settings", message);

    // ── Shell-level confirmation overlay ──────────────────────────────────
    // A modal yes/no overlay that lives at the window level (not inside any page).
    // Used by the ribbon's right-click Delete, and any other shell-side action that
    // needs to ask the user.

    [ObservableProperty] private bool   _confirmationVisible;
    [ObservableProperty] private string _confirmationTitle  = string.Empty;
    [ObservableProperty] private string _confirmationPrompt = string.Empty;

    private Action? _confirmationOnConfirm;
    private Action? _confirmationOnCancel;

    public void ShowConfirmation(string title, string prompt, Action onConfirm, Action? onCancel = null)
    {
        ConfirmationTitle      = title;
        ConfirmationPrompt     = prompt;
        _confirmationOnConfirm = onConfirm;
        _confirmationOnCancel  = onCancel;
        ConfirmationVisible    = true;
    }

    [RelayCommand]
    private void ConfirmShellConfirmation()
    {
        ConfirmationVisible = false;
        var cb = _confirmationOnConfirm;
        _confirmationOnConfirm = _confirmationOnCancel = null;
        cb?.Invoke();
    }

    [RelayCommand]
    private void CancelShellConfirmation()
    {
        ConfirmationVisible = false;
        var cb = _confirmationOnCancel;
        _confirmationOnConfirm = _confirmationOnCancel = null;
        cb?.Invoke();
    }

    // ── Shell-level input prompt overlay ──────────────────────────────────
    // Window-level text-input prompt. Mirrors the confirmation overlay above
    // and is the destination for IShellServices.ShowPrompt.

    [ObservableProperty] private bool   _promptVisible;
    [ObservableProperty] private string _promptTitle = string.Empty;
    [ObservableProperty] private string _promptLabel = string.Empty;
    [ObservableProperty] private string _promptValue = string.Empty;

    private Action<string>? _promptOnConfirm;
    private Action?         _promptOnCancel;

    public void ShowPrompt(string title, string label, string initialValue,
                           Action<string> onConfirm, Action? onCancel = null)
    {
        PromptTitle      = title;
        PromptLabel      = label;
        PromptValue      = initialValue;
        _promptOnConfirm = onConfirm;
        _promptOnCancel  = onCancel;
        PromptVisible    = true;
    }

    [RelayCommand]
    private void ConfirmShellPrompt()
    {
        PromptVisible = false;
        var value = PromptValue;
        var cb    = _promptOnConfirm;
        _promptOnConfirm = null;
        _promptOnCancel  = null;
        cb?.Invoke(value);
    }

    [RelayCommand]
    private void CancelShellPrompt()
    {
        PromptVisible = false;
        var cb = _promptOnCancel;
        _promptOnConfirm = null;
        _promptOnCancel  = null;
        cb?.Invoke();
    }

    /// <summary>
    /// Closes matching tabs (by PageKind) and immediately reopens them so they pick
    /// up updated config.  Called by the Options panel after a settings save.
    /// </summary>
    public void RefreshTabs(IEnumerable<string> pageKinds)
    {
        var kinds = pageKinds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toRefresh = Tabs.Where(t => kinds.Contains(t.PageKind ?? string.Empty)).ToList();
        foreach (var tab in toRefresh)
        {
            var pageKind   = tab.PageKind;
            var pageParams = tab.PageParams;
            _shellServices.CloseTab(tab);
            if (!string.IsNullOrEmpty(pageKind))
                _shellServices.OpenTab(pageKind, pageParams);
        }
    }

    // ── Work contexts ─────────────────────────────────────────────────────
    [ObservableProperty] private WorkContext _currentWorkContext = null!;

    public ObservableCollection<WorkContext> WorkContexts => WorkContextManager.Instance.Contexts;

    [RelayCommand]
    private void SelectWorkContext(WorkContext ctx)
    {
        if (ReferenceEquals(ctx, CurrentWorkContext)) return;

        // Transfer this window's registration (+ tracked tabs + factory/callbacks)
        // from the old context's ShellServices to the new one so actions instantiated
        // for the new context can still find a window to open tabs in.
        var newSvc = (ShellServices)ctx.ShellServices!;
        if (!ReferenceEquals(_shellServices, newSvc))
        {
            _shellServices.TransferWindowTo(this, newSvc);
            _shellServices = newSvc;
            OnPropertyChanged(nameof(ShellServices));
        }

        CurrentWorkContext = ctx;
        // The RibbonControl observes CurrentWorkContext via its WorkContext DP
        // and handles save/load of items per-context. The shell no longer touches the ribbon.
    }

    // ── Options overlay ───────────────────────────────────────────────────
    [ObservableProperty] private bool _optionsOpen;

    // ── Manage AI overlay ─────────────────────────────────────────────────
    [ObservableProperty] private bool _manageAiOpen;

    // ── AI response overlay ───────────────────────────────────────────────
    // Shown in-shell when the conversational AI returns a plain message, and as the
    // approval / progress surface while the agent harness runs client tools.
    [ObservableProperty] private bool   _aiResponseOverlayOpen;
    [ObservableProperty] private string _aiResponseAiName  = "Aria";
    [ObservableProperty] private string _aiResponseText    = string.Empty;
    [ObservableProperty] private string _aiResponsePrompt  = string.Empty;

    [ObservableProperty] private string _aiToolApprovalSummary = string.Empty;
    [ObservableProperty] private string _aiProgressText        = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiOverlayIsMessage))]
    [NotifyPropertyChangedFor(nameof(AiOverlayIsApproval))]
    [NotifyPropertyChangedFor(nameof(AiOverlayIsRunning))]
    [NotifyPropertyChangedFor(nameof(AiOverlayShowsMarkdown))]
    private AiOverlayState _aiOverlayState = AiOverlayState.Message;

    public bool AiOverlayIsMessage  => AiOverlayState == AiOverlayState.Message;
    public bool AiOverlayIsApproval => AiOverlayState == AiOverlayState.Approval;
    public bool AiOverlayIsRunning  => AiOverlayState == AiOverlayState.Running;
    /// <summary>True in Message/Approval states (markdown body + footer); false while Running.</summary>
    public bool AiOverlayShowsMarkdown => AiOverlayState != AiOverlayState.Running;

    // True once an agent run has taken over the overlay (approval / progress shown), so the
    // final message is rendered into the overlay rather than routed as a fresh reply.
    private bool _agentOverlayActive;

    // Resolves the pending tool-batch / plan approval (Accept = true, Deny / cancel = false).
    private TaskCompletionSource<bool>? _toolApprovalTcs;

    // Captured per send for "Continue as Conversation": the tab the agent ran against, and the
    // files its tools read/created.
    private Page? _agentOriginTab;
    private IReadOnlyList<string> _agentContextPaths = [];

    private ShellServices _shellServices;

    /// <summary>
    /// Set by MainWindow after the RibbonControl is initialised so the shell can
    /// remove ribbon items from handler execution (e.g. "files no longer exist").
    /// </summary>
    public RibbonViewModel? Ribbon { get; set; }

    public ShellViewModel(BackgroundActivityManager activityManager,
                          WorkContext workContext)
    {
        _activityManager = activityManager;
        _activityManager.IsActiveChanged += (_, active) =>
            Application.Current.Dispatcher.Invoke(() => AiIsBusy = active);
        _currentWorkContext = workContext;
        _shellServices = workContext.ShellServices!;

        WireRootPane();

        // When the Options panel rebuilds the context list, re-anchor to the same-named context
        WorkContextManager.Instance.ContextsRefreshed += (_, _) =>
        {
            var refreshed = WorkContextManager.Instance.Contexts
                .FirstOrDefault(c => c.Name == CurrentWorkContext?.Name)
                ?? WorkContextManager.Instance.Contexts.FirstOrDefault();
            if (refreshed is not null)
                Application.Current.Dispatcher.Invoke(() => CurrentWorkContext = refreshed);
        };

        // ── Voice input availability + transcription wiring ──────────────────
        HostCapabilityService.Instance.CapabilitiesReady += (_, _) => RecomputeVoiceAvailable();
        WhisperModelManager.Instance.ModelReadyChanged   += (_, _) => RecomputeVoiceAvailable();

        VoiceManager.Instance.RecordingChanged += (_, rec) =>
            Application.Current.Dispatcher.Invoke(() => { IsRecording = rec; VoiceActive = rec; });
        // Voice owns the input display while listening — set, don't append; it
        // self-corrects each pass and a final pass lands when listening stops.
        VoiceManager.Instance.TranscriptionUpdated += (_, text) =>
            Application.Current.Dispatcher.Invoke(() => AiInputText = text);
        VoiceManager.Instance.Error += (_, msg) =>
            Application.Current.Dispatcher.Invoke(() => ShowError("Voice", msg));

        RecomputeVoiceAvailable();

        // ── Shared inbox: keep this window's unread badge + toasts in sync ──
        // The store is shared across windows, but UnreadCount is derived, so recompute on both
        // collection changes AND per-item IsRead changes (marking read in one window is a property
        // mutation that ObservableCollection doesn't surface) — otherwise other windows' badges go stale.
        Notifications.CollectionChanged += OnInboxChanged;
        foreach (var m in Notifications) m.PropertyChanged += OnMessageItemChanged;
        MessageCenter.Instance.MessagePosted += OnMessagePosted;
        RecomputeUnread();
    }

    /// <summary>Detaches shared-store handlers so a closed window's view-model can be collected.</summary>
    public void Detach()
    {
        MessageCenter.Instance.MessagePosted -= OnMessagePosted;
        Notifications.CollectionChanged -= OnInboxChanged;
        foreach (var m in Notifications) m.PropertyChanged -= OnMessageItemChanged;
    }

    private void OnInboxChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (NotificationItem m in e.OldItems) m.PropertyChanged -= OnMessageItemChanged;
        if (e.NewItems is not null)
            foreach (NotificationItem m in e.NewItems) m.PropertyChanged += OnMessageItemChanged;
        RecomputeUnread();
    }

    private void OnMessageItemChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationItem.IsRead)) RecomputeUnread();
    }

    private void OnMessagePosted(object? sender, NotificationItem message)
    {
        // Only the focused window pops the toast; unfocused windows leave it pending until they
        // gain focus (DrainPendingToasts). The shared ShownAsToast flag prevents double-toasting.
        if (_isFocused && message.ShowToast && !message.ShownAsToast)
        {
            message.ShownAsToast = true;
            ShowToast(message);
        }
    }

    private void DrainPendingToasts()
    {
        foreach (var m in MessageCenter.Instance.PendingToasts.ToList())
        {
            m.ShownAsToast = true;
            ShowToast(m);
        }
    }

    private void RecomputeUnread() => UnreadCount = Notifications.Count(n => !n.IsRead);

    private void RecomputeVoiceAvailable()
    {
        var cfg = ConfigManager.Instance.GetAll().OfType<VoiceConfig>().FirstOrDefault();
        VoiceAvailable = cfg is not null
            && HostCapabilityService.Instance.Report?.CanRunWhisper == true
            && WhisperModelManager.Instance.IsModelReady(cfg);
    }

    // ── Tab commands ──────────────────────────────────────────────────────

    [RelayCommand]
    private void ActivateTab(Page tab)
    {
        if (tab.IsActive && tab == ActiveTab)
        {
            (CurrentPage as IPageView)?.Reinitialize(tab.PageParams ?? []);
            return;
        }

        ((IWindowHost)this).BringToFront(tab);
        ((IWindowHost)this).SetActiveTab(tab);
        (CurrentPage as IPageView)?.Reinitialize(tab.PageParams ?? []);
    }

    [RelayCommand]
    private void CloseTab(Page tab) => _shellServices.CloseTab(tab);

    [RelayCommand]
    private void TearOffTab(Page tab) => _shellServices.TearOffTab(tab);

    /// <summary>Cross-window drop target: receive a tab moved from another window.</summary>
    [RelayCommand]
    private void ReceiveTab(Page tab) => _shellServices.MoveTab(tab, this);

    /// <summary>
    /// Generic "open a page" entry point used by the breadcrumb bar's
    /// follow-link buttons (and any other shell-level page opener).
    /// </summary>
    [RelayCommand]
    private void OpenPage(OpenPageRequest req)
        => _shellServices.OpenTab(req.PageKind, req.PageParams, CurrentPage as IPageView);

    // ── Ribbon (shell-side: just "open a page from a ribbon item") ────────

    [RelayCommand]
    private void OpenRibbonItem(RibbonItem item)
    {
        if (item.PageKind is not null)
        {
            var handler = FeatureManager.Instance.GetRibbonPinHandler(item.PageKind, CurrentWorkContext);
            if (handler is not null)
            {
                var paths = (CurrentPage as ISelectionProvider)?.SelectedFilePaths
                            ?? (IReadOnlyList<string>)[];
                var ctx = new RibbonExecutionContext(
                    paths,
                    msg           => ShowError("Ribbon Action", msg),
                    (t, m, ok, c) => ShowConfirmation(t, m, ok, c),
                    ()            => Ribbon?.Items.Remove(item));
                handler.Execute(item.PageParams, ctx);
                return;
            }
            _shellServices.OpenTab(item.PageKind, item.PageParams);
        }
        else
        {
            item.Command?.Execute(null);
        }
    }

    [RelayCommand]
    private void OpenRibbonItemInNewWindow(RibbonItem item)
    {
        if (item.PageKind is null) return;

        var handler = FeatureManager.Instance.GetRibbonPinHandler(item.PageKind, CurrentWorkContext);
        if (handler is not null)
        {
            // Create the new window first so any OpenTab calls inside the handler land there.
            var newHost = _shellServices.CreateAndShowNewWindow();
            if (newHost is null) return;

            var paths = (CurrentPage as ISelectionProvider)?.SelectedFilePaths
                        ?? (IReadOnlyList<string>)[];
            var ctx = new RibbonExecutionContext(
                paths,
                msg           => ShowError("Ribbon Action", msg),
                (t, m, ok, c) => ShowConfirmation(t, m, ok, c),
                ()            => Ribbon?.Items.Remove(item));
            handler.Execute(item.PageParams, ctx);

            // If the action didn't open a tab, the new window serves no purpose.
            if (newHost.Tabs.Count == 0)
                newHost.Window.Close();
            return;
        }

        _shellServices.OpenPageInNewWindow(item.PageKind, item.PageParams);
    }

    /// <summary>Exposed so the RibbonControl can route its Delete confirmation through the shell.</summary>
    public IShellServices ShellServices => _shellServices;

    // ── Notifications ─────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleNotifications()
    {
        NotificationsOpen = !NotificationsOpen;
        if (NotificationsOpen)
        {
            foreach (var n in Notifications) n.IsRead = true;
            UnreadCount = 0;
        }
    }

    [RelayCommand]
    private void DismissNotification(NotificationItem item) => MessageCenter.Instance.Remove(item);

    // ── Toast ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void DismissActiveToast() => DisplayNextToast();

    /// <summary>Runs a message action from the toast, then dismisses the toast.</summary>
    [RelayCommand]
    private void RunToastAction(MessageAction action)
    {
        if (action.Command.CanExecute(null)) action.Command.Execute(null);
        DisplayNextToast();
    }

    // ── Options ───────────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleOptions() => OptionsOpen = !OptionsOpen;

    [RelayCommand]
    private void CloseOptions() => OptionsOpen = false;

    // ── Manage AI ─────────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleManageAi() => ManageAiOpen = !ManageAiOpen;

    [RelayCommand]
    private void CloseManageAi() => ManageAiOpen = false;

    // ── AI ────────────────────────────────────────────────────────────────

    partial void OnAiInputTextChanged(string value)
    {
        _handlerEvalCts?.Cancel();

        // While listening, voice owns the display — don't score handlers or
        // propose completions against the live transcription.
        if (IsRecording)
        {
            AiHandlerSymbol        = null;
            AiCompletionSuggestion = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            AiHandlerSymbol        = null;
            AiIsListening          = false;
            AiCompletionSuggestion = null;
            return;
        }

        // A stale suggestion no longer matches the edited text — drop it until
        // the clear-winner handler proposes a fresh one.
        AiCompletionSuggestion = null;

        _handlerEvalCts = new CancellationTokenSource();
        var cts = _handlerEvalCts;
        _ = Task.Delay(150, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Application.Current.Dispatcher.Invoke(() => EvaluateHandlers(value, cts.Token));
        }, TaskScheduler.Default);
    }

    private void EvaluateHandlers(string text, CancellationToken token)
    {
        var pageVm               = (CurrentPage as IPageView)?.ViewModel;
        var (_, clearWinner, _)  = CurrentWorkContext.AiService.ScoreHandlers(text, pageVm);

        if (clearWinner?.Symbol is not null)
        {
            AiHandlerSymbol = clearWinner.Symbol;
            AiIsListening   = false;
        }
        else
        {
            AiHandlerSymbol = null;
            AiIsListening   = true;
        }

        // Inline completion is only offered by the clear-winner handler.
        if (clearWinner is not null)
            _ = UpdateCompletionAsync(clearWinner, text, pageVm, token);
        else
            AiCompletionSuggestion = null;
    }

    private async Task UpdateCompletionAsync(IQueryHandler handler, string text,
                                             IPageViewModel? pageVm, CancellationToken token)
    {
        string? suggestion;
        try { suggestion = await handler.CompleteAsync(text, pageVm); }
        catch { return; }

        // Discard if cancelled or the input moved on while we were computing.
        if (token.IsCancellationRequested) return;
        if (!string.Equals(AiInputText, text, StringComparison.Ordinal)) return;

        AiCompletionSuggestion = string.IsNullOrEmpty(suggestion) ? null : suggestion;
    }

    [RelayCommand]
    private async Task SendAiMessage()
    {
        var text = AiInputText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        AiInputText            = string.Empty;
        AiHandlerSymbol        = null;
        AiIsListening          = false;
        AiCompletionSuggestion = null;
        // A new prompt always replaces the previous overlay response.
        AiResponseOverlayOpen  = false;
        _agentOverlayActive    = false;
        AiResponsePrompt       = text;   // original prompt, for "Continue as Conversation"
        _agentOriginTab        = ActiveTab;
        _agentContextPaths     = [];

        // A new send cancels any in-flight agent run; its pending approval (if any) resolves
        // to "deny" through the token registration below.
        _aiSendCts?.Cancel();
        _aiSendCts = new CancellationTokenSource();
        var sendToken = _aiSendCts.Token;

        var pageVm                                   = (CurrentPage as IPageView)?.ViewModel;
        var svc                                      = CurrentWorkContext.AiService;
        var (scored, clearWinner, effectiveText)     = svc.ScoreHandlers(text, pageVm);
        var selected                                 = clearWinner;
        text                                         = effectiveText;

        // Show an immediate placeholder so there's visible activity while we think (during the
        // possible disambiguation call and the agent run). Conversation pages show the exchange
        // inline, so they skip the overlay.
        var showOverlay = pageVm is not ConversationViewModel;
        if (showOverlay)
        {
            SyncAiName();
            AiProgressText        = "Considering…";
            AiOverlayState        = AiOverlayState.Running;
            AiResponseOverlayOpen = true;
        }

        if (selected is null && scored.Count > 0)
        {
            try   { selected = await svc.DisambiguateToolSelection(pageVm, text, scored); }
            catch (Exception ex) { AiResponseOverlayOpen = false; ShowError("AI error", ex.Message); return; }
        }

        // 3. A handler was identified — run it (results go to the AIChat tab, not the overlay)
        if (selected is not null)
        {
            AiResponseOverlayOpen = false;
            string? result;
            try
            {
                result = await selected.ProcessAsync(text, pageVm);
            }
            catch (Exception ex)
            {
                ShowError("AI error", ex.Message);
                return;
            }

            if (result is not null)
                ShowAiResponseOverlay(text, result);
            return;
        }

        // 4. No handler → client-side agent run (LLM may call this page's tools, see results, loop).
        AiResponse? response;
        try
        {
            response = await svc.RunAgentAsync(pageVm, text, IncludeContext, this, sendToken);
        }
        catch (OperationCanceledException) { AiResponseOverlayOpen = false; return; }
        catch (Exception ex)
        {
            AiResponseOverlayOpen = false;
            ShowError("AI error", ex.Message);
            return;
        }

        if (response is null) { AiResponseOverlayOpen = false; return; }

        _agentContextPaths = response.Attachments;

        switch (response.Kind)
        {
            case AiResponseKind.Prefill:
                AiResponseOverlayOpen = false;
                await AnimatePrefillAsync(response.Text!);
                break;

            case AiResponseKind.Message:
                // When the agent used the overlay (approval / progress), the final answer was
                // already rendered there by ShowFinal. Otherwise route the reply: append to an
                // active Conversation, else open a fresh overlay.
                if (_agentOverlayActive) break;
                if (pageVm is ConversationViewModel convVm)
                    await convVm.AppendExchangeAsync(text, response.Text!);
                else
                    ShowAiResponseOverlay(text, response.Text!);
                break;
        }
    }

    private void ShowAiResponseOverlay(string input, string response)
    {
        SyncAiName();
        AiResponsePrompt      = input;
        AiResponseText        = response;
        AiOverlayState        = AiOverlayState.Message;
        AiResponseOverlayOpen = true;
    }

    private void SyncAiName()
    {
        var persona = ConfigManager.Instance.GetAll().OfType<AiPersonaConfig>().FirstOrDefault();
        AiResponseAiName = string.IsNullOrWhiteSpace(persona?.Name) ? "Aria" : persona!.Name;
    }

    [RelayCommand]
    private void CloseAiResponseOverlay()
    {
        // Closing while an approval is pending counts as a denial.
        _toolApprovalTcs?.TrySetResult(false);
        AiResponseOverlayOpen = false;
    }

    // ── IToolApprovalCoordinator (agent harness → overlay) ─────────────────

    public Task<bool> RequestToolBatchApprovalAsync(
        string explanationMarkdown, IReadOnlyList<ToolCall> batch, CancellationToken ct)
    {
        var d = Application.Current?.Dispatcher;
        if (d is not null && !d.CheckAccess())
            return d.Invoke(() => RequestToolBatchApprovalAsync(explanationMarkdown, batch, ct));

        SyncAiName();
        AiResponseText        = string.IsNullOrWhiteSpace(explanationMarkdown)
            ? $"{AiResponseAiName} would like to run the following:"
            : explanationMarkdown;
        AiToolApprovalSummary = BuildBatchSummary(batch);
        return ShowApprovalAndWait(ct);
    }

    public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
    {
        var d = Application.Current?.Dispatcher;
        if (d is not null && !d.CheckAccess())
            return d.Invoke(() => RequestPlanApprovalAsync(plan, ct));

        SyncAiName();
        AiResponseText        = BuildPlanMarkdown(plan);
        AiToolApprovalSummary = $"Approve plan: {plan.Title}";
        return ShowApprovalAndWait(ct);
    }

    private Task<bool> ShowApprovalAndWait(CancellationToken ct)
    {
        _agentOverlayActive   = true;
        AiOverlayState        = AiOverlayState.Approval;
        AiResponseOverlayOpen = true;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _toolApprovalTcs = tcs;
        ct.Register(() => tcs.TrySetResult(false));
        return tcs.Task;
    }

    public void ReportProgress(string message)
    {
        var d = Application.Current?.Dispatcher;
        if (d is not null && !d.CheckAccess()) { d.Invoke(() => ReportProgress(message)); return; }

        _agentOverlayActive   = true;
        AiProgressText        = message;
        AiOverlayState        = AiOverlayState.Running;
        AiResponseOverlayOpen = true;
    }

    public void ShowFinal(string finalMarkdown)
    {
        var d = Application.Current?.Dispatcher;
        if (d is not null && !d.CheckAccess()) { d.Invoke(() => ShowFinal(finalMarkdown)); return; }

        // Only take over the overlay when the agent actually used it this run; a plain
        // conversational reply is routed by SendAiMessage instead.
        if (!_agentOverlayActive) return;
        AiResponseText        = finalMarkdown;
        AiOverlayState        = AiOverlayState.Message;
        AiResponseOverlayOpen = true;
    }

    [RelayCommand]
    private void AcceptToolBatch()
    {
        AiProgressText = "Working…";
        AiOverlayState = AiOverlayState.Running;
        var tcs = _toolApprovalTcs;
        _toolApprovalTcs = null;
        tcs?.TrySetResult(true);
    }

    [RelayCommand]
    private void DenyToolBatch()
    {
        AiProgressText = $"Declined — asking {AiResponseAiName}…";
        AiOverlayState = AiOverlayState.Running;
        var tcs = _toolApprovalTcs;
        _toolApprovalTcs = null;
        tcs?.TrySetResult(false);
    }

    private static string BuildBatchSummary(IReadOnlyList<ToolCall> batch)
    {
        if (batch.Count == 1)
            return $"Run {batch[0].Tool}?";
        var grouped = batch
            .GroupBy(c => c.Tool)
            .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key);
        return "Run " + string.Join(", ", grouped) + "?";
    }

    private static string BuildPlanMarkdown(ClientPlan plan)
    {
        var sb = new StringBuilder();
        sb.Append("### ").Append(plan.Title).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(plan.Mermaid))
            sb.Append("```mermaid\n").Append(plan.Mermaid!.Trim()).Append("\n```\n\n");
        if (plan.Steps.Count > 0)
        {
            sb.Append("**Steps**\n\n");
            var n = 1;
            foreach (var s in plan.Steps)
            {
                sb.Append(n++).Append(". ").Append(s.Title);
                if (s.IsDecisionPoint)               sb.Append("  _(decision)_");
                else if (!string.IsNullOrEmpty(s.Tool)) sb.Append("  `").Append(s.Tool).Append('`');
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    [RelayCommand]
    private async Task ContinueAsConversation()
    {
        var prompt   = AiResponsePrompt;
        var response = AiResponseText;
        AiResponseOverlayOpen = false;

        if (string.IsNullOrEmpty(prompt)) return;

        var record = new ConversationRecord
        {
            Id        = Guid.NewGuid().ToString(),
            StartedAt = DateTime.Now,
            Title     = ConversationTitleGenerator.Generate(prompt),
            Messages  =
            [
                new ConversationMessage { Text = prompt,   IsUser = true,  Timestamp = DateTime.Now },
                new ConversationMessage { Text = response, IsUser = false, Timestamp = DateTime.Now },
            ],
            // Carry the files the agent's tools read/created so the conversation can keep using them.
            Attachments = [.. _agentContextPaths]
        };

        try { await CurrentWorkContext.AiService.SaveAsync(record); }
        catch { /* persistence failures shouldn't block opening the tab */ }

        _shellServices.OpenTab("Conversation", new Dictionary<string, string>
        {
            ["conversationId"] = record.Id
        });

        // Add the tab the agent ran against as a live context item (not persistable — it's a live page).
        if (_agentOriginTab is not null &&
            (CurrentPage as IPageView)?.ViewModel is ConversationViewModel convVm)
            convVm.AddContextItem(_agentOriginTab);
    }

    private async Task AnimatePrefillAsync(string prefill)
    {
        AiInputIsAiTyping = true;
        AiInputText       = string.Empty;

        foreach (char c in prefill)
        {
            AiInputText += c;
            await Task.Delay(15);
        }

        await Task.Delay(400);
        AiInputIsAiTyping = false;
    }

    [RelayCommand]
    private void ClearAiInput() => AiInputText = string.Empty;

    // Mic is usable when the host can run Whisper and no AI request is processing.
    // While recording, AiIsBusy stays false so this remains true (to allow stop).
    private bool CanToggleVoice => VoiceAvailable && !AiIsBusy;

    [RelayCommand(CanExecute = nameof(CanToggleVoice))]
    private async Task ToggleVoice()
    {
        if (!IsRecording)
            VoiceManager.Instance.StartListening();
        else
            await VoiceManager.Instance.StopListeningAsync();   // manual early stop; also auto-stops
    }

    /// <summary>
    /// Cancels the in-flight AI request. Today this only trips the local
    /// <see cref="_aiSendCts"/>; the token is not yet observed by AIService or
    /// the providers, so the request may still complete. UI scaffolding for a
    /// later cancellation-plumbing pass.
    /// </summary>
    [RelayCommand]
    private void CancelAi() => _aiSendCts?.Cancel();

    // ── Background tasks ──────────────────────────────────────────────────

    public void AddBackgroundTask(BackgroundTask task) => _activityManager.AddTask(task);

    // ── IRibbonExecutionContext implementation ────────────────────────────

    private sealed class RibbonExecutionContext(
        IReadOnlyList<string>                               paths,
        Action<string>                                      showError,
        Action<string, string, Action, Action?>             showConfirmation,
        Action                                              removeItem)
        : IRibbonExecutionContext
    {
        public IReadOnlyList<string> SelectedFilePaths => paths;
        public void ShowError(string message)                         => showError(message);
        public void ShowConfirmation(string title, string message, Action onConfirm, Action? onCancel)
            => showConfirmation(title, message, onConfirm, onCancel);
        public void RemoveCurrentRibbonItem()                         => removeItem();
    }
}

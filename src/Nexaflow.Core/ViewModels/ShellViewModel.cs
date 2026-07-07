using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.ViewModels.Overlays;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Nexaflow.Core.ViewModels;

public partial class ShellViewModel : ObservableObject, IWindowHost
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

    // This window's UI dispatcher (captured on its constructing thread); marshal background work through it.
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    IReadOnlyList<Page> IWindowHost.Tabs => LeafPanes.SelectMany(p => p.Pages).ToList();

    // A new tab follows focus (it lands in the pane the user is working in); operations on an existing
    // tab follow the pane that owns it. This keeps ShellServices pane-agnostic — it deals in windows,
    // and the window resolves which of its panes the tab lives in.
    void IWindowHost.AddTab(Page tab) => FocusedPane.Add(tab);

    // Split off (or focus the existing) right pane so the next AddTab lands there — backs "Open in
    // right pane". SplitEmpty already creates the pane and focuses it; when already split we just refocus.
    void IWindowHost.FocusSecondPane()
    {
        if (RootPaneNode is SplitPaneNode split) FocusedPane = split.Second;
        else                                     SplitEmpty();
    }

    void IWindowHost.RemoveTab(Page tab)
    {
        (OwningPane(tab) ?? FocusedPane).Remove(tab);
        CollapseEmptyPane();
    }

    void IWindowHost.BringToFront(Page tab) => (OwningPane(tab) ?? FocusedPane).BringToFront(tab);

    void IWindowHost.SetActiveTab(Page tab)
    {
        var pane = OwningPane(tab) ?? FocusedPane;
        pane.BringToFront(tab);
        pane.ActivePage = tab;
        FocusedPane = pane;          // activating a tab focuses its pane
    }

    void IWindowHost.ShowError(string message) => ShowError("Error", message);
    void IWindowHost.ShowNotification(string message)
        => MessageCenter.Instance.Post(new NotificationItem { Title = "Info", Body = message });
    void IWindowHost.ShowConfirmation(string title, string prompt, Action onConfirm, Action? onCancel,
                                      string? confirmLabel, string? cancelLabel)
        => ShowConfirmation(title, prompt, onConfirm, onCancel, confirmLabel, cancelLabel);

    void IWindowHost.ShowPrompt(string title, string label, string initialValue,
                                Action<string> onConfirm, Action? onCancel)
        => ShowPrompt(title, label, initialValue, onConfirm, onCancel);

    void IWindowHost.AddRibbonPin(RibbonPinRequest request)
        => Ribbon?.PinFromHandlerCommand.Execute(request);

    /// <summary>Raised when something asks to insert text into this window's AI bar; the window
    /// (which owns the actual input control) inserts at the caret.</summary>
    public event Action<string>? ChatInputInsertRequested;

    void IWindowHost.InsertChatInput(string text) => ChatInputInsertRequested?.Invoke(text);

    void IWindowHost.SubmitAiQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        AiInputText = query;
        if (SendAiMessageCommand.CanExecute(null)) SendAiMessageCommand.Execute(null);
    }

    // ── Panes (each = a strip of pages + its active page) ─────────────────
    // The window's root content is either a single Pane or a SplitPaneNode wrapping two side-by-side
    // panes. FocusedPane is the leaf the user last interacted with: new tabs and AI context follow it,
    // while operations on a specific tab follow whichever pane owns that tab.

    public Pane RootPane { get; } = new();

    /// <summary>The window's root content — a lone <see cref="Pane"/>, or a <see cref="SplitPaneNode"/> when split.</summary>
    [ObservableProperty] private IPaneNode _rootPaneNode = null!;

    /// <summary>The leaf new tabs and AI context route to. Equals <see cref="RootPane"/> when unsplit.</summary>
    [ObservableProperty] private Pane _focusedPane = null!;

    /// <summary>The leaf panes under the current root — one when unsplit, two when split (First then Second).</summary>
    public IEnumerable<Pane> LeafPanes => RootPaneNode switch
    {
        SplitPaneNode s => [s.First, s.Second],
        Pane p          => [p],
        _               => [],
    };

    /// <summary>True while the tab area is split in two.</summary>
    public bool IsSplit => RootPaneNode is SplitPaneNode;

    private Pane? OwningPane(Page tab) => LeafPanes.FirstOrDefault(p => p.Pages.Contains(tab));

    // Legacy facades — kept so existing call sites keep working; all route through the focused/owning pane.
    public IEnumerable<Page> Tabs => LeafPanes.SelectMany(p => p.Pages);

    public Page? ActiveTab
    {
        get => FocusedPane.ActivePage;
        set => (value is null ? FocusedPane : OwningPane(value) ?? FocusedPane).ActivePage = value;
    }

    public UserControl? CurrentPage => FocusedPane.ActivePage?.GetOrCreateContent();

    private void WireRootPane()
    {
        FocusedPane  = RootPane;
        RootPaneNode = RootPane;     // fires OnRootPaneNodeChanged → subscribes the leaf pane(s)
    }

    // The breadcrumb/content bind each Pane directly in PaneView, so the shell only owes a re-notify of its
    // computed ActiveTab/CurrentPage facades when the *focused* pane's active page changes.
    private void OnLeafPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Pane.ActivePage) && ReferenceEquals(sender, FocusedPane))
        {
            OnPropertyChanged(nameof(ActiveTab));
            OnPropertyChanged(nameof(CurrentPage));
        }
    }

    private void SubscribeLeafPanes()
    {
        foreach (var pane in LeafPanes)
        {
            pane.PropertyChanged -= OnLeafPanePropertyChanged;
            pane.PropertyChanged += OnLeafPanePropertyChanged;
        }
    }

    partial void OnRootPaneNodeChanged(IPaneNode? oldValue, IPaneNode newValue)
    {
        SubscribeLeafPanes();
        OnPropertyChanged(nameof(IsSplit));
        OnPropertyChanged(nameof(LeafPanes));
    }

    partial void OnFocusedPaneChanged(Pane? oldValue, Pane newValue)
    {
        OnPropertyChanged(nameof(ActiveTab));
        OnPropertyChanged(nameof(CurrentPage));
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
                _ui.Invoke(() => { if (ActiveToast == message) DisplayNextToast(); });
        }, TaskScheduler.Default);
    }

    private void ShowError(string title, string message)
        => MessageCenter.Instance.Post(new NotificationItem
        {
            Title = title, Body = message, Severity = MessageSeverity.Error, ShowToast = true,
        });

    public void ShowErrorToast(string message) => ShowError("Settings", message);

    /// <summary>Posts a transient informational toast (e.g. "Exported N conversations").</summary>
    public void ShowInfoToast(string title, string message)
        => MessageCenter.Instance.Post(new NotificationItem
        {
            Title = title, Body = message, Severity = MessageSeverity.Info, ShowToast = true,
        });

    // ── Unified overlay host ──────────────────────────────────────────────
    // One ContentControl in MainWindow renders whatever modal is active, picked by DataTemplate from the
    // content's type — so each overlay's visual tree is built lazily, only when shown, and torn down on
    // close. The legacy per-overlay flags below (OptionsOpen/WorkspaceConfigOpen/ConfirmationVisible/PromptVisible)
    // remain the source of truth (they drive profile-switch gating, airspace coverage and deep-links);
    // ActiveOverlay is *derived* from them, and the heavy panel VMs are built on demand here. Features push
    // their own overlays via IShellServices.ShowOverlay (rendered by a DataTemplate they contribute).

    /// <summary>The content view-model shown in the shell's overlay host, or null when nothing is open.</summary>
    public object? ActiveOverlay { get; private set; }

    private OptionsViewModel?  _optionsPanel;
    private WorkspaceConfigViewModel? _workspaceConfigPanel;
    private object? _confirmationOverlay;
    private object? _promptOverlay;
    private object? _featureOverlay;

    /// <summary>True while a feature-pushed overlay (not a built-in modal) is showing.</summary>
    public bool HasFeatureOverlay => _featureOverlay is not null;

    private void SyncActiveOverlay()
    {
        object? next =
            _featureOverlay
            ?? (OptionsOpen        ? _optionsPanel       : null)
            ?? (WorkspaceConfigOpen       ? _workspaceConfigPanel      : null)
            ?? (ConfirmationVisible ? _confirmationOverlay : null)
            ?? (PromptVisible      ? _promptOverlay      : null);

        if (ReferenceEquals(next, ActiveOverlay)) return;
        ActiveOverlay = next;
        OnPropertyChanged(nameof(ActiveOverlay));
    }

    /// <summary>Shows a feature-supplied overlay view-model (rendered by a DataTemplate the feature ships
    /// via its IThemeContribution). Backs <see cref="IShellServices.ShowOverlay"/>.</summary>
    public void ShowOverlay(object overlayViewModel)
    {
        _featureOverlay = overlayViewModel;
        SyncActiveOverlay();
    }

    /// <summary>Closes whatever overlay is currently active (feature overlay or the built-in modal).</summary>
    public void CloseOverlay()
    {
        if (_featureOverlay is not null) { _featureOverlay = null; SyncActiveOverlay(); return; }
        if (OptionsOpen)             OptionsOpen = false;
        else if (WorkspaceConfigOpen)       WorkspaceConfigOpen = false;
        else if (ConfirmationVisible) CancelShellConfirmation();
        else if (PromptVisible)      CancelShellPrompt();
    }

    // Builds the Options panel VM on open and re-homes the wiring that used to live in MainWindow code-behind.
    private OptionsViewModel BuildOptionsPanel()
    {
        var vm = new OptionsViewModel(_shellServices);
        vm.SaveError           += ShowErrorToast;
        vm.TabRefreshRequested += RefreshTabs;
        vm.SaveCompleted       += () =>
        {
            OptionsOpen = false;
            // A theme change can't live-reflow (StaticResource by design); restart this window in place,
            // reopening the same tabs against the new theme.
            if (ConfigManager.Instance.GetAll().OfType<ShellConfig>().FirstOrDefault() is { } shell
                && shell.Theme != ThemeManager.Current)
                _shellServices.RestartWindowForTheme(this, shell.Theme);
        };
        if (RequestedOptionsSection is { } section)
        {
            vm.SelectSection(section);
            RequestedOptionsSection = null;
        }
        return vm;
    }

    private WorkspaceConfigViewModel BuildWorkspaceConfigPanel()
    {
        var profile = ConfigureTargetProfile ?? CurrentWorkspace.Profile;
        var vm = new WorkspaceConfigViewModel(profile, _shellServices);
        vm.ApplyError += ShowErrorToast;
        if (RequestedConfigureSection is { } section)
        {
            vm.SelectSection(section);
            RequestedConfigureSection = null;
        }
        return vm;
    }

    partial void OnOptionsOpenChanged(bool value)
    {
        _optionsPanel = value ? BuildOptionsPanel() : null;
        SyncActiveOverlay();
    }

    // ── Shell-level confirmation overlay ──────────────────────────────────
    // A modal yes/no overlay that lives at the window level (not inside any page).
    // Used by the ribbon's right-click Delete, and any other shell-side action that
    // needs to ask the user.

    [ObservableProperty] private bool   _confirmationVisible;
    [ObservableProperty] private string _confirmationTitle  = string.Empty;
    [ObservableProperty] private string _confirmationPrompt = string.Empty;

    private Action? _confirmationOnConfirm;
    private Action? _confirmationOnCancel;
    private string  _confirmationConfirmLabel = "Confirm";
    private string  _confirmationCancelLabel  = "Cancel";

    public void ShowConfirmation(string title, string prompt, Action onConfirm, Action? onCancel = null,
                                 string? confirmLabel = null, string? cancelLabel = null)
    {
        ConfirmationTitle         = title;
        ConfirmationPrompt        = prompt;
        _confirmationConfirmLabel = string.IsNullOrWhiteSpace(confirmLabel) ? "Confirm" : confirmLabel!;
        _confirmationCancelLabel  = string.IsNullOrWhiteSpace(cancelLabel)  ? "Cancel"  : cancelLabel!;
        _confirmationOnConfirm    = onConfirm;
        _confirmationOnCancel     = onCancel;
        ConfirmationVisible       = true;
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

    partial void OnConfirmationVisibleChanged(bool value)
    {
        // Title/Prompt/labels are set before ConfirmationVisible flips true (see ShowConfirmation).
        _confirmationOverlay = value
            ? new ConfirmationOverlay
              {
                  Title          = ConfirmationTitle,
                  Prompt         = ConfirmationPrompt,
                  ConfirmCommand = ConfirmShellConfirmationCommand,
                  CancelCommand  = CancelShellConfirmationCommand,
                  ConfirmLabel   = _confirmationConfirmLabel,
                  CancelLabel    = _confirmationCancelLabel,
              }
            : null;
        SyncActiveOverlay();
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

    partial void OnPromptVisibleChanged(bool value)
    {
        // The editable PromptValue stays on this VM; the template two-way-binds to it via the window.
        _promptOverlay = value
            ? new PromptOverlay
              {
                  Title          = PromptTitle,
                  Label          = PromptLabel,
                  ConfirmCommand = ConfirmShellPromptCommand,
                  CancelCommand  = CancelShellPromptCommand,
              }
            : null;
        SyncActiveOverlay();
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

    // ── Profiles / workspace ──────────────────────────────────────────────
    [ObservableProperty] private WorkspaceRuntime _currentWorkspace = null!;

    /// <summary>The saved profiles shown in the dropdown.</summary>
    public ObservableCollection<Profile> Profiles => WorkspaceManager.Instance.Profiles;

    /// <summary>False while a modal overlay (Options / Manage-AI) is open — blocks profile switching.</summary>
    public bool CanSwitchProfile => !OptionsOpen && !WorkspaceConfigOpen;

    [RelayCommand]
    private void SelectProfile(Profile profile)
    {
        // Modal overlays block switching (their edits target the active workspace/profile).
        if (!CanSwitchProfile) return;
        if (ReferenceEquals(profile, CurrentWorkspace?.Profile)) return;

        // Cancel any in-flight AI send before the workspace is reconfigured under it.
        _aiSendCts?.Cancel();

        // Collapse the workspace to this window: the others would otherwise be left empty (their
        // tabs close on reconfigure) and still showing the old profile's ribbon.
        _shellServices.CloseOtherWindows(this);

        // In-place: keep this Workspace object, reconfigure it for the new profile (tabs close,
        // providers/AIService rebuilt). ShellServices/windows reference the Workspace, which is
        // unchanged — only its internals swap — so no window fix-up is needed.
        WorkspaceManager.Instance.SwitchProfile(CurrentWorkspace!, profile);

        // Same Workspace reference; re-raise so the ribbon (bound to CurrentWorkspace.Profile)
        // and the selector reload for the new profile.
        OnPropertyChanged(nameof(CurrentWorkspace));
    }

    // ── Options overlay ───────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitchProfile))]
    private bool _optionsOpen;

    /// <summary>Section (config name) the Options panel should land on next time it (re)builds.
    /// Set via <see cref="OpenOptionsAt"/>; consumed and cleared by the window.</summary>
    public string? RequestedOptionsSection { get; set; }

    /// <summary>Opens Options on the section for <paramref name="configName"/>.</summary>
    public void OpenOptionsAt(string configName)
    {
        RequestedOptionsSection = configName;
        OptionsOpen = true;
    }

    // ── Manage AI / Configure overlay ─────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitchProfile))]
    private bool _workspaceConfigOpen;

    /// <summary>
    /// The profile the Configure (Manage-AI) overlay targets — set by <see cref="ConfigureProfile"/>
    /// right before the overlay opens. Null means the active workspace's profile. Cleared on close.
    /// </summary>
    public Profile? ConfigureTargetProfile { get; private set; }

    /// <summary>Section (config name) the Configure overlay should land on when it next builds.
    /// Set via <see cref="OpenConfigureAt"/>; consumed and cleared by the window.</summary>
    public string? RequestedConfigureSection { get; set; }

    /// <summary>Opens the Configure overlay for <paramref name="profile"/> on the section for
    /// <paramref name="configName"/> (a deep link, e.g. from the console page's Configure button).</summary>
    public void OpenConfigureAt(Profile? profile, string configName)
    {
        RequestedConfigureSection = configName;
        ConfigureProfile(profile);
    }

    /// <summary>
    /// True when Configure was opened from the Options → Workspaces tab. If the user makes no changes,
    /// closing Configure returns to the Options popup (handled by MainWindow). Reset after each close.
    /// </summary>
    public bool ConfigureReturnToOptions { get; set; }

    partial void OnWorkspaceConfigOpenChanged(bool value)
    {
        if (value)
        {
            // ConfigureTargetProfile / RequestedConfigureSection were set just before this flipped true.
            _workspaceConfigPanel = BuildWorkspaceConfigPanel();
        }
        else
        {
            // Closing: apply pending disk changes to the running workspace. If nothing changed and the
            // panel was opened from the Options → Workspaces tab, bounce back to Options on that tab.
            bool changed = _workspaceConfigPanel?.Close() ?? false;
            _workspaceConfigPanel         = null;
            ConfigureTargetProfile = null;

            bool returnToOptions = ConfigureReturnToOptions;
            ConfigureReturnToOptions = false;
            if (!changed && returnToOptions)
            {
                RequestedOptionsSection = "workcontexts";   // WorkspacesConfig.ConfigName
                OptionsOpen = true;
            }
        }
        SyncActiveOverlay();
    }

    /// <summary>Opens the Configure panel for a profile chosen in the Options → Workspaces tab,
    /// remembering to return to Options if nothing is changed.</summary>
    public void OpenConfigureFromOptions(Profile profile)
    {
        OptionsOpen = false;
        ConfigureReturnToOptions = true;
        ConfigureProfile(profile);
    }

    /// <summary>
    /// The shell's default AI response handler — the in-shell popup overlay (progress, approvals,
    /// final answer, "Continue as Conversation"). Used whenever the active page does not render AI
    /// responses itself. <c>AiResponseOverlay.xaml</c> binds to this. See <see cref="IAIResponseHandler"/>.
    /// </summary>
    public ShellAIResponseHandler Ai { get; }

    private ShellServices _shellServices;

    /// <summary>
    /// Set by MainWindow after the RibbonControl is initialised so the shell can
    /// remove ribbon items from handler execution (e.g. "files no longer exist").
    /// The setter also subscribes to the ribbon editor's open state, which covers the page area.
    /// </summary>
    public RibbonViewModel? Ribbon
    {
        get => _ribbon;
        set
        {
            if (_ribbon is not null) _ribbon.PropertyChanged -= OnRibbonPropertyChangedForOverlay;
            _ribbon = value;
            if (_ribbon is not null) _ribbon.PropertyChanged += OnRibbonPropertyChangedForOverlay;
            RefreshOverlayCoverage();
        }
    }
    private RibbonViewModel? _ribbon;

    // ── Airspace overlay coverage ─────────────────────────────────────────
    // A WebView2 tab hosts a native child window (HWND) that renders above ALL WPF content, so the
    // shell's modal overlays would be hidden behind it. We track whether any overlay covers the page
    // area and tell each realized airspace-hosting page view to hide/show its native control to match.

    private bool _pageCoveredByOverlay;

    /// <summary>True when any window-modal overlay currently covers the page content area.</summary>
    private bool AnyOverlayCoversPage =>
        OptionsOpen || WorkspaceConfigOpen || ConfirmationVisible || PromptVisible || NotificationsOpen
        || Ai.AiResponseOverlayOpen || _ribbon?.IsEditOpen == true;

    private void OnShellPropertyChangedForOverlay(object? _, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OptionsOpen) or nameof(WorkspaceConfigOpen) or nameof(ConfirmationVisible)
            or nameof(PromptVisible) or nameof(NotificationsOpen))
            RefreshOverlayCoverage();
    }

    private void OnRibbonPropertyChangedForOverlay(object? _, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RibbonViewModel.IsEditOpen))
            RefreshOverlayCoverage();
    }

    /// <summary>
    /// Recomputes overlay coverage and, when it flips, notifies every realized airspace-hosting page
    /// view (e.g. a WebView2 tab) so it can collapse/restore its native control behind/in front of the overlay.
    /// </summary>
    private void RefreshOverlayCoverage()
    {
        var covered = AnyOverlayCoversPage;
        if (covered == _pageCoveredByOverlay) return;
        _pageCoveredByOverlay = covered;

        foreach (var page in LeafPanes.SelectMany(p => p.Pages))
            if (page.Content is IAirspaceContent airspace)
                airspace.SetCoveredByOverlay(covered);
    }

    public ShellViewModel(BackgroundActivityManager activityManager,
                          WorkspaceRuntime workspace)
    {
        _activityManager = activityManager;
        // NB: AiIsBusy is NOT coupled to background activity — the AI bar locks only while an actual
        // AI run is in flight (driven by SendAiMessage). Background tasks (NuGet checks, downloads,
        // conversation analysis) report through the activity ticker without blocking AI input.
        _currentWorkspace = workspace;
        _shellServices = workspace.ShellServices!;
        Ai = new ShellAIResponseHandler(this);

        // Keep airspace-hosting pages (WebView2) hidden while a modal overlay covers the page area —
        // its native HWND would otherwise render above the overlay. Track the contributing overlay flags.
        PropertyChanged    += OnShellPropertyChangedForOverlay;
        Ai.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellAIResponseHandler.AiResponseOverlayOpen))
                RefreshOverlayCoverage();
        };

        WireRootPane();

        // If this workspace's profile was removed in the Options panel, switch to the first available.
        WorkspaceManager.Instance.ProfilesRefreshed += (_, _) =>
        {
            if (CurrentWorkspace is null
                || !WorkspaceManager.Instance.Profiles.Contains(CurrentWorkspace.Profile))
            {
                var fallback = WorkspaceManager.Instance.Profiles.FirstOrDefault();
                if (fallback is not null)
                    _ui.Invoke(() =>
                    {
                        WorkspaceManager.Instance.SwitchProfile(CurrentWorkspace!, fallback);
                        OnPropertyChanged(nameof(CurrentWorkspace));
                    });
            }
        };

        // ── Voice input availability + transcription wiring ──────────────────
        HostCapabilityService.Instance.CapabilitiesReady += (_, _) => RecomputeVoiceAvailable();
        WhisperModelManager.Instance.ModelReadyChanged   += (_, _) => RecomputeVoiceAvailable();

        VoiceManager.Instance.RecordingChanged += (_, rec) =>
            _ui.Invoke(() => { IsRecording = rec; VoiceActive = rec; });
        // Voice owns the input display while listening — set, don't append; it
        // self-corrects each pass and a final pass lands when listening stops.
        VoiceManager.Instance.TranscriptionUpdated += (_, text) =>
            _ui.Invoke(() => AiInputText = text);
        VoiceManager.Instance.Error += (_, msg) =>
            _ui.Invoke(() => ShowError("Voice", msg));

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

    /// <summary>Drop target: receive a dragged tab into the focused pane — whether it comes from another
    /// pane of this window (a cross-pane move) or from another window (a cross-window move). The drop
    /// handler focuses the drop pane first, so "the focused pane" is the one the user dropped on.</summary>
    [RelayCommand]
    private void ReceiveTab(Page tab)
    {
        if (OwningPane(tab) is { } source)          // already in this window
        {
            if (!ReferenceEquals(source, FocusedPane))
                MoveTabToPane(tab, FocusedPane);     // move it between this window's panes
            return;
        }
        _shellServices.MoveTab(tab, this);           // from another window → lands in the focused pane
    }

    /// <summary>
    /// Generic "open a page" entry point used by the breadcrumb bar's
    /// follow-link buttons (and any other shell-level page opener).
    /// </summary>
    [RelayCommand]
    private void OpenPage(OpenPageRequest req)
        => _shellServices.OpenTab(req.PageKind, req.PageParams, CurrentPage as IPageView);

    /// <summary>Closes every tab in <paramref name="tab"/>'s pane except <paramref name="tab"/> itself.</summary>
    [RelayCommand(CanExecute = nameof(CanCloseOthersInPane))]
    private void CloseOthersInPane(Page tab)
    {
        var pane = OwningPane(tab);
        if (pane is null) return;
        foreach (var other in pane.Pages.Where(p => p != tab).ToList())
            _shellServices.CloseTab(other);
    }

    private bool CanCloseOthersInPane(Page? tab) => tab is not null && (OwningPane(tab)?.Pages.Count ?? 0) >= 2;

    /// <summary>Forwards a tab-strip "Pin to Ribbon" request to the ribbon. Exposed here so the pane
    /// DataTemplates can bind it without reaching the RibbonControl by element name (a DataTemplate can't).</summary>
    [RelayCommand]
    private void PinTabToRibbon(TabPinRequest request) => Ribbon?.PinCommand.Execute(request);

    // ── Split panes ───────────────────────────────────────────────────────
    // v1: a single, vertical (left/right) split — RootPaneNode is either a lone Pane or one SplitPaneNode.

    /// <summary>Splits the tab area, moving <paramref name="tab"/> into a new right-hand pane.</summary>
    [RelayCommand(CanExecute = nameof(CanSplitRight))]
    private void SplitRight(Page tab)
    {
        var left = OwningPane(tab) ?? RootPane;
        left.Remove(tab);
        var right = new Pane();
        right.Add(tab);
        Split(left, right);
    }

    // Can't split when already split (v1 is two panes max), nor split off the only tab (the source would empty).
    private bool CanSplitRight(Page? tab) => !IsSplit && tab is not null && (OwningPane(tab)?.Pages.Count ?? 0) >= 2;

    /// <summary>Splits the tab area, adding an empty right-hand pane.</summary>
    [RelayCommand(CanExecute = nameof(CanSplitEmpty))]
    private void SplitEmpty() => Split(RootPane, new Pane());

    private bool CanSplitEmpty() => !IsSplit;

    private void Split(Pane left, Pane right)
    {
        RootPaneNode = new SplitPaneNode(left, right);
        FocusedPane  = right;
    }

    /// <summary>Collapses the split back to one pane, moving <paramref name="pane"/>'s tabs into the survivor.</summary>
    [RelayCommand(CanExecute = nameof(CanClosePane))]
    private void ClosePane(Pane pane)
    {
        if (RootPaneNode is not SplitPaneNode split) return;
        var keep = ReferenceEquals(split.First, pane) ? split.Second : split.First;
        foreach (var p in pane.Pages.ToList())   // remove-then-add so each PaneView releases/adopts the control
        {
            pane.Remove(p);
            keep.Add(p);
        }
        Collapse(keep);
    }

    private bool CanClosePane(Pane? pane) => IsSplit && pane is not null;

    /// <summary>Moves <paramref name="tab"/> into <paramref name="target"/> (a pane in this same window).</summary>
    private void MoveTabToPane(Page tab, Pane target)
    {
        var source = OwningPane(tab);
        if (source is null || ReferenceEquals(source, target)) return;
        source.Remove(tab);
        target.Add(tab);
        FocusedPane = target;
        CollapseEmptyPane();                     // dragging out the last tab collapses the source pane
    }

    // Drops back to a single pane when a split leaf has emptied (last tab closed / torn off / dragged away).
    private void CollapseEmptyPane()
    {
        if (RootPaneNode is not SplitPaneNode split) return;
        if (split.First.Pages.Count == 0)       Collapse(split.Second);
        else if (split.Second.Pages.Count == 0) Collapse(split.First);
    }

    private void Collapse(Pane survivor)
    {
        RootPaneNode = survivor;
        FocusedPane  = survivor;
    }

    /// <summary>Marks <paramref name="pane"/> the focused pane — new tabs and AI context route here.
    /// Fired by a <see cref="Controls.PaneView"/> when the user clicks or focuses into it.</summary>
    [RelayCommand]
    private void PaneActivated(Pane pane)
    {
        if (pane is not null) FocusedPane = pane;
    }

    // ── Ribbon (shell-side: just "open a page from a ribbon item") ────────

    [RelayCommand]
    private void OpenRibbonItem(RibbonItem item)
    {
        if (item.PageKind is not null)
        {
            var executor = FeatureManager.Instance.GetRibbonItemExecutor(item.PageKind, CurrentWorkspace);
            if (executor is not null)
            {
                var paths = (CurrentPage as ISelectionProvider)?.SelectedFilePaths
                            ?? (IReadOnlyList<string>)[];
                var ctx = new RibbonExecutionContext(
                    paths,
                    msg           => ShowError("Ribbon Action", msg),
                    (t, m, ok, c) => ShowConfirmation(t, m, ok, c),
                    ()            => Ribbon?.Items.Remove(item));
                executor.Execute(item.PageParams, ctx);
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

        var executor = FeatureManager.Instance.GetRibbonItemExecutor(item.PageKind, CurrentWorkspace);
        if (executor is not null)
        {
            // Create the new window first so any OpenTab calls inside the executor land there.
            var newHost = _shellServices.CreateAndShowNewWindow();
            if (newHost is null) return;

            var paths = (CurrentPage as ISelectionProvider)?.SelectedFilePaths
                        ?? (IReadOnlyList<string>)[];
            var ctx = new RibbonExecutionContext(
                paths,
                msg           => ShowError("Ribbon Action", msg),
                (t, m, ok, c) => ShowConfirmation(t, m, ok, c),
                ()            => Ribbon?.Items.Remove(item));
            executor.Execute(item.PageParams, ctx);

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

    // ── Configure (per-workspace) ─────────────────────────────────────────

    [RelayCommand]
    private void CloseWorkspaceConfig() => WorkspaceConfigOpen = false;

    /// <summary>Opens the per-workspace Configure panel for a specific profile (may be non-active).</summary>
    [RelayCommand]
    private void ConfigureProfile(Profile? profile)
    {
        if (profile is null) return;
        ConfigureTargetProfile = profile;
        WorkspaceConfigOpen = true;
    }

    /// <summary>Right-click entry point: configure the currently active workspace.</summary>
    [RelayCommand]
    private void ConfigureCurrentWorkspace() => ConfigureProfile(CurrentWorkspace?.Profile);

    /// <summary>
    /// Right-click entry point: create a new workspace by cloning the current one (settings copied,
    /// fresh symbol/colour) and open the Configure panel on it. Mirrors the Options "Clone" action but
    /// routes straight into Configure so the new workspace can be named/recoloured and tuned at once.
    /// </summary>
    [RelayCommand]
    private void NewWorkspace()
    {
        if (CurrentWorkspace?.Profile is not { } source) return;

        var mgr   = WorkspaceManager.Instance;
        var clone = mgr.CloneProfile(source, mgr.UniqueProfileName($"{source.Name} copy"));
        mgr.SaveProfiles();        // persist the new profile in the dropdown list immediately

        ConfigureProfile(clone);   // open Configure on its identity page
    }

    /// <summary>
    /// Right-click entry point: capture this window's current tabs — grouped by pane, so a left/right split
    /// is reproduced — as the active workspace's startup tabset, and persist it. Tabs that can't be
    /// recreated (no PageKind) are skipped; capturing nothing is valid (the workspace then starts blank).
    /// Order within a pane isn't guaranteed on restore.
    /// </summary>
    [RelayCommand]
    private void UseTabsetAsDefault()
    {
        if (CurrentWorkspace?.Profile is not { } profile) return;

        var tabs = CaptureLayout();
        profile.DefaultTabs = tabs;
        WorkspaceManager.Instance.SaveProfiles();

        ShowInfoToast("Default tabs set",
            tabs.Count == 0
                ? "This workspace will start with no tabs open."
                : $"Saved {tabs.Count} tab{(tabs.Count == 1 ? "" : "s")} as this workspace's startup layout.");
    }

    /// <summary>Snapshots the current tabs grouped by pane (index 0 = left/primary, 1 = the right split),
    /// each pane's active tab flagged. Shared by "Use Tabset as Default" and the pane-aware restart snapshot
    /// (<see cref="IWindowHost.CaptureTabLayout"/>). Skips tabs that can't be recreated (no PageKind).</summary>
    private List<DefaultTabDescriptor> CaptureLayout()
    {
        var tabs = new List<DefaultTabDescriptor>();
        int paneIndex = 0;
        foreach (var pane in LeafPanes)
        {
            foreach (var page in pane.Pages)
            {
                if (string.IsNullOrEmpty(page.PageKind)) continue;   // unrestorable (e.g. placeholder) — skip
                tabs.Add(new DefaultTabDescriptor
                {
                    PageKind   = page.PageKind!,
                    PageParams = page.PageParams is null ? null : new Dictionary<string, string>(page.PageParams),
                    Pane       = paneIndex,
                    Title      = page.Title,
                    IsActive   = ReferenceEquals(page, pane.ActivePage),
                });
            }
            paneIndex++;
        }
        return tabs;
    }

    IReadOnlyList<DefaultTabDescriptor> IWindowHost.CaptureTabLayout() => CaptureLayout();

    /// <summary>
    /// Configure panel's "Delete Workspace": confirms, then permanently deletes the profile (config +
    /// every conversation). Refuses the last remaining workspace (the button is also disabled then).
    /// A deletable workspace may be the active one or one open elsewhere: its windows are closed first
    /// (collapsing the current one to a single instance), the data is deleted, then the remaining
    /// window closes — removing the workspace from active memory.
    /// </summary>
    public void RequestDeleteWorkspace(Profile profile)
    {
        var mgr = WorkspaceManager.Instance;
        if (mgr.Profiles.Count <= 1) { ShowErrorToast("The last workspace can't be deleted."); return; }

        var liveWs = mgr.FindLiveWorkspace(profile);

        var message = liveWs is not null
            ? $"Delete “{profile.Name}”?\n\nThis is irreversible: it permanently deletes the workspace and every "
              + "conversation in it, and closes all of its open windows."
            : $"Delete “{profile.Name}”?\n\nThis is irreversible: it permanently deletes the workspace and every "
              + "conversation in it.";

        ShowConfirmation("Delete workspace", message, onConfirm: () => DeleteWorkspace(profile, liveWs));
    }

    private void DeleteWorkspace(Profile profile, WorkspaceRuntime? liveWs)
    {
        var mgr = WorkspaceManager.Instance;

        if (ReferenceEquals(liveWs, CurrentWorkspace))
        {
            // The current workspace: collapse to this window, delete the data while a window is still
            // alive, then close this last window — its close removes the workspace from active memory
            // (and quits the app if no other window remains).
            _shellServices.CloseOtherWindows(this);
            mgr.DeleteProfile(profile, force: true);
            _shellServices.CloseAllWindows();
            return;
        }

        if (liveWs is not null)
        {
            // A different live workspace: tear down its windows (last close disposes it), then delete.
            liveWs.ShellServices?.CloseAllWindows();
            mgr.DeleteProfile(profile, force: true);
            WorkspaceConfigOpen = false;
            return;
        }

        // An inactive profile: straightforward delete; close the Configure overlay.
        mgr.DeleteProfile(profile);
        WorkspaceConfigOpen = false;
    }

    // ── AI ────────────────────────────────────────────────────────────────

    partial void OnAiInputTextChanged(string value)
    {
        // Mirror what's being typed to the active page (e.g. the terminal's faux-prompt echo). Always
        // pushed — including an empty value — so the preview clears on send/clear.
        PushInputPreview(value);

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
            _ui.Invoke(() => EvaluateHandlers(value, cts.Token));
        }, TaskScheduler.Default);
    }

    private void EvaluateHandlers(string text, CancellationToken token)
    {
        var pageVm               = (CurrentPage as IPageView)?.ViewModel;
        var (_, clearWinner, _)  = CurrentWorkspace.AiService!.ScoreHandlers(text, pageVm);

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

    // ── Chat-bar feature handlers (keys / drop / live preview) ────────────
    //
    // The AI input bar is shell chrome, but the active page's feature may want to participate: claim a
    // key (terminal history / completion), turn a drop into inserted text, or mirror typing. Each is a
    // contract discovered + instantiated per workspace by FeatureManager, consulted against the active
    // page's view-model — mirroring how IQueryHandler is scored against it.

    /// <summary>Consulted by the AI input control before its own key handling. Returns the first handled
    /// result (optionally rewriting the bar text), or <see cref="ChatKeyResult.NotHandled"/>.</summary>
    public ChatKeyResult HandleChatKey(System.Windows.Input.Key key,
                                       System.Windows.Input.ModifierKeys modifiers,
                                       string text, int caretIndex)
    {
        var pageVm = (CurrentPage as IPageView)?.ViewModel;
        foreach (var h in FeatureManager.Instance.GetChatKeyHandlers(CurrentWorkspace))
        {
            if (!h.CanHandle(pageVm)) continue;
            var result = h.HandleKey(key, modifiers, text, caretIndex, pageVm);
            if (result.Handled) return result;
        }
        return ChatKeyResult.NotHandled;
    }

    /// <summary>True if any feature can turn this drag payload into bar text (cheap format check).</summary>
    public bool CanHandleChatDrop(IDataObject data)
    {
        var pageVm = (CurrentPage as IPageView)?.ViewModel;
        return FeatureManager.Instance.GetChatDropHandlers(CurrentWorkspace)
            .Any(h => h.CanHandle(pageVm) && h.AcceptedFormats.Any(data.GetDataPresent));
    }

    /// <summary>The text the first matching feature wants to insert for this drop, or null.</summary>
    public string? BuildChatDropText(IDataObject data)
    {
        var pageVm = (CurrentPage as IPageView)?.ViewModel;
        foreach (var h in FeatureManager.Instance.GetChatDropHandlers(CurrentWorkspace))
        {
            if (!h.CanHandle(pageVm) || !h.AcceptedFormats.Any(data.GetDataPresent)) continue;
            var text = h.BuildInsertText(data, pageVm);
            if (!string.IsNullOrEmpty(text)) return text;
        }
        return null;
    }

    private void PushInputPreview(string text)
    {
        var pageVm = (CurrentPage as IPageView)?.ViewModel;
        foreach (var p in FeatureManager.Instance.GetChatInputPreviews(CurrentWorkspace))
            if (p.CanPreview(pageVm)) p.OnInputChanged(text, pageVm);
    }

    /// <summary>Block until the active page's context is ready (or the send is cancelled), showing a
    /// "waiting for data collection…" line meanwhile. No-op for ready or unobservable pages so a page
    /// that can't signal readiness never wedges the send.</summary>
    private static async Task WaitForContextAsync(IAIResponseHandler handler, IPageViewModel? pageVm, CancellationToken ct)
    {
        if (pageVm is null || pageVm.IsContextReady) return;
        if (pageVm is not INotifyPropertyChanged inpc) return;

        handler.ReportProgress("Waiting for data collection…");

        var tcs = new TaskCompletionSource();
        void OnChanged(object? _, PropertyChangedEventArgs __)
        {
            if (pageVm.IsContextReady) tcs.TrySetResult();
        }

        inpc.PropertyChanged += OnChanged;
        using var reg = ct.Register(() => tcs.TrySetResult());
        try
        {
            if (!pageVm.IsContextReady)   // re-check after subscribing — guard the lost-update race
                await tcs.Task;
        }
        finally { inpc.PropertyChanged -= OnChanged; }
    }

    private enum TurnOutcome { Completed, Cancelled, Prefill, Error }

    /// <summary>One inline banner handler per engagement page (keyed by the page's view-model).</summary>
    private readonly Dictionary<IChatEngagement, InlineAiResponseHandler> _inlineHandlers = [];

    /// <summary>
    /// Gets — or lazily creates and wires — the inline banner handler for an engagement page: builds the
    /// banner, injects it into the page's host, hands the handler to the page (so it can drive its own
    /// banner, e.g. for a background-command result), and caches it. Called only while the engagement page
    /// is the active tab, so <see cref="ActiveTab"/> is that page.
    /// </summary>
    private InlineAiResponseHandler AcquireInlineHandler(IChatEngagement eng)
    {
        if (_inlineHandlers.TryGetValue(eng, out var existing)) return existing;

        var page    = ActiveTab!;
        var handler = new InlineAiResponseHandler(this, eng, page);
        eng.GetHostUserControl().Content = new Controls.AiResponseBanner { DataContext = handler };
        eng.SetResponseHandler(handler);
        _inlineHandlers[eng] = handler;
        page.Closed += (_, _) => _inlineHandlers.Remove(eng);
        return handler;
    }

    /// <summary>The handler driving the in-flight run, or null when idle. While set, a new submission is
    /// queued into it as an interjection rather than starting a fresh run.</summary>
    private IAIResponseHandler? _runningHandler;

    [RelayCommand]
    private async Task SendAiMessage()
    {
        var text = AiInputText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        AiInputText            = string.Empty;
        AiHandlerSymbol        = null;
        AiIsListening          = false;
        AiCompletionSuggestion = null;

        // A run is already in flight → queue this as an interjection; the agent picks it up at the next
        // turn boundary (it can steer mid-task). Cancel is still available separately.
        if (_runningHandler is not null) { _runningHandler.Enqueue(text); return; }

        var originalInput = text;   // restored to the bar if the user cancels this run

        // A new send cancels any in-flight agent run; its pending approval (if any) resolves
        // to "deny" through the token registration below.
        _aiSendCts?.Cancel();
        _aiSendCts = new CancellationTokenSource();
        var sendToken = _aiSendCts.Token;

        // On an EXPLICIT cancel (the Cancel button cancels this run's token in place), put the user's
        // message back in the bar so they can tweak and resend. A newer send installs a fresh CTS, so
        // we never clobber a message the user is already sending.
        void RestoreInputIfCancelled()
        {
            var superseded = _aiSendCts is null || _aiSendCts.Token != sendToken;
            if (sendToken.IsCancellationRequested && !superseded)
                AiInputText = originalInput;
        }

        var pageVm = (CurrentPage as IPageView)?.ViewModel;
        var svc    = CurrentWorkspace.AiService!;

        // Pick the response surface: a page that renders AI itself (e.g. the conversation) uses its own;
        // a page that hosts the shared inline banner (IChatEngagement, e.g. the console) gets that banner;
        // otherwise the shell's modal overlay (Ai). Either way the agent loop talks only to this handler.
        IAIResponseHandler handler =
            pageVm as IAIResponseHandler
            ?? (pageVm is IChatEngagement eng ? (IAIResponseHandler)AcquireInlineHandler(eng) : null)
            ?? Ai;

        // Runs one prompt → answer. Returns how it ended so the outer loop can deliver any late interjections.
        async Task<TurnOutcome> RunTurn(string input)
        {
            handler.BeginResponse(input);   // echo the user + show a "considering" placeholder

            // Pinned context may still be gathering (e.g. SystemInfo's background scan). Hold the turn
            // until the active page reports ready so the model sees real data, not a "still gathering…" stub.
            await WaitForContextAsync(handler, pageVm, sendToken);
            if (sendToken.IsCancellationRequested) { handler.Abort(); return TurnOutcome.Cancelled; }

            var (scored, clearWinner, effectiveText) = svc.ScoreHandlers(input, pageVm);
            var selected = clearWinner;
            var effective = effectiveText;

            if (selected is null && scored.Count > 0)
            {
                try   { selected = await svc.DisambiguateToolSelection(pageVm, effective, scored); }
                catch (Exception ex) { handler.Abort(); ShowError("AI error", ex.Message); return TurnOutcome.Error; }
            }

            // A query handler matched — run it; its result is the final answer.
            if (selected is not null)
            {
                string? result;
                try   { result = await selected.ProcessAsync(effective, pageVm); }
                catch (Exception ex) { handler.Abort(); ShowError("AI error", ex.Message); return TurnOutcome.Error; }

                if (result is not null) handler.ShowFinal(result);
                else                    handler.Abort();
                return TurnOutcome.Completed;
            }

            // No handler → client-side agent run; the loop drives `handler` (progress / approval / final).
            AiResponse? response;
            try
            {
                response = await svc.RunAgentAsync(pageVm, effective, IncludeContext, handler, sendToken);
            }
            catch (OperationCanceledException) { handler.Abort(); return TurnOutcome.Cancelled; }
            catch (Exception ex)               { handler.Abort(); ShowError("AI error", ex.Message); return TurnOutcome.Error; }

            if (response is null)
            {
                handler.Abort();
                return sendToken.IsCancellationRequested ? TurnOutcome.Cancelled : TurnOutcome.Completed;
            }

            // Carry the agent's touched files for the shell handler's "Continue as Conversation".
            Ai.AgentContextPaths = response.Attachments;

            if (response.Kind == AiResponseKind.Prefill)
            {
                handler.Abort();
                await AnimatePrefillAsync(response.Text!);
                return TurnOutcome.Prefill;
            }
            return TurnOutcome.Completed;   // Message: already rendered by handler.ShowFinal in the loop
        }

        _runningHandler = handler;
        AiIsBusy = true;
        try
        {
            var input = text;
            while (true)
            {
                var outcome = await RunTurn(input);
                if (outcome == TurnOutcome.Cancelled) { RestoreInputIfCancelled(); break; }
                if (outcome != TurnOutcome.Completed) break;   // Prefill / Error

                // A message arrived after the run's last turn boundary — run it as a fresh follow-up.
                var leftover = handler.DrainQueue();
                if (leftover.Count == 0) break;
                input = string.Join("\n", leftover);
            }
        }
        finally { _runningHandler = null; AiIsBusy = false; }
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

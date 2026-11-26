using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

using Nexaflow.Core.Help;
using Nexaflow.Localization;
using Nexaflow.Interaction;
using Nexaflow.Assistant.Requests;

namespace Nexaflow.Core.ViewModels;

public partial class ShellViewModel : ObservableObject, IWindowHost, IHelpPaneHost, IAssistantBar, IBarReplyHost
{
    // ── IWindowHost ───────────────────────────────────────────────────────

    public Window Window { get; init; } = null!;

    /// <summary>Keeps this window's intended placement; installed by the window once it has a handle.</summary>
    internal WindowPlacementTracker Placement { get; init; } = null!;

    WindowPlacement? IWindowHost.IntendedPlacement => Placement.Intended;

    void IWindowHost.OpenAt(WindowPlacement placement) => Placement.OpenAt(placement);

    string IWindowHost.Title => Window.Title;

    void IWindowHost.Show() => Window.Show();

    void IWindowHost.Close() => Window.Close();

    void IWindowHost.Activate()
    {
        try { Window.Activate(); } catch (InvalidOperationException) { /* closing, or not yet realised */ }
    }

    void IWindowHost.Restore()
    {
        if (Window.WindowState == WindowState.Minimized) SystemCommands.RestoreWindow(Window);
    }

    void IWindowHost.ShowAtPointer()
    {
        // The new window's tab strip lands under the pointer, kept inside the monitor's work area.
        const double TopBarHeight           = 72;
        const double TabBarHeight           = 38;
        const double TabStripColumnFraction = 0.45;
        const double TabStripInternalMargin = 8;
        const double DefaultWindowWidth     = 1280;
        const double DefaultWindowHeight    = 780;

        var (dropX, dropY, _, _, work) = WindowManager.GetCursorInfo();
        double tabOffsetX = DefaultWindowWidth  * TabStripColumnFraction + TabStripInternalMargin;
        double tabOffsetY = TopBarHeight + TabBarHeight / 2.0;

        Window.Left = Math.Max(work.Left, Math.Min(dropX - tabOffsetX, work.Right  - DefaultWindowWidth));
        Window.Top  = Math.Max(work.Top,  Math.Min(dropY - tabOffsetY, work.Bottom - DefaultWindowHeight));
        Window.Show();
    }

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
    void IWindowHost.AddTab(Page tab) { FocusedPane.Add(tab); ChatInputFocusRequested?.Invoke(); }

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
        ChatInputFocusRequested?.Invoke();
    }

    void IWindowHost.ShowError(string message) => ShowError(Str.Get("Shell.Errors.Error"), message);
    void IWindowHost.ShowNotification(string message)
        => _inbox.Post(new NotificationItem { Workspace = CurrentRuntime.Workspace, Title = Str.Get("Shell.Notifications.InfoTitle"), Body = message });
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

    IReadOnlyList<QuickOpenTarget> IWindowHost.GetRibbonQuickOpenTargets()
    {
        var targets = new List<QuickOpenTarget>();
        foreach (var item in Ribbon?.Items ?? [])
        {
            if (item.Kind != RibbonItemKind.Button || string.IsNullOrWhiteSpace(item.Label)) continue;
            var captured = item;
            targets.Add(new QuickOpenTarget(item.Label, () => OpenRibbonItem(captured)));
        }
        return targets;
    }

    /// <summary>Raised when a tab is added or activated, so the window moves focus to its AI bar — a request
    /// can be typed straight away. The window owns the actual input control.</summary>
    public event Action? ChatInputFocusRequested;

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

    /// <summary>The window's root content — a lone <see cref="Pane"/>, or a <see cref="SplitPaneNode"/> when split.</summary>
    [ObservableProperty] private IPaneNode _rootPaneNode = null!;

    /// <summary>The leaf new tabs and AI context route to — the lone pane while unsplit.</summary>
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

    /// <summary>The one pane while unsplit. Read from the live root rather than a pane captured at startup: once
    /// the left pane has collapsed away the survivor is the old right pane, and a split built on anything else
    /// would leave every open tab outside the tree.</summary>
    private Pane SoleLeaf => RootPaneNode as Pane ?? FocusedPane;

    /// <summary>Opens, re-points and closes this window's Help tab (the Help button / F1).</summary>
    private readonly HelpPaneController _help;

    private Pane? OwningPane(Page tab) => LeafPanes.FirstOrDefault(p => p.Pages.Contains(tab));

    // Legacy facades — kept so existing call sites keep working; all route through the focused/owning pane.
    public IEnumerable<Page> Tabs => LeafPanes.SelectMany(p => p.Pages);

    public Page? ActiveTab
    {
        get => FocusedPane.ActivePage;
        set => (value is null ? FocusedPane : OwningPane(value) ?? FocusedPane).ActivePage = value;
    }

    /// <summary>The focused pane's page view, when it has been shown: what a view itself offers (keys, a selection).</summary>
    internal FrameworkElement? ActivePageView => ActiveTab is { } tab ? _views.Shown(tab) : null;

    private void WireRootPane()
    {
        var first    = new Pane(_views);
        FocusedPane  = first;
        RootPaneNode = first;     // fires OnRootPaneNodeChanged → subscribes the leaf pane(s)
    }

    // The breadcrumb/content bind each Pane directly in PaneView, so the shell only owes a re-notify of its
    // computed ActiveTab facade when the *focused* pane's active page changes.
    private void OnLeafPanePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Any pane: an open Help tab in the other one follows what this one now shows.
        if (e.PropertyName == nameof(Pane.ActivePage) && sender is Pane changed)
            _help.OnActivePageChanged(changed);

        if (e.PropertyName == nameof(Pane.ActivePage) && ReferenceEquals(sender, FocusedPane))
        {
            OnPropertyChanged(nameof(ActiveTab));
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
    }

    // ── Notifications ─────────────────────────────────────────────────────
    // The inbox is a single app-wide store shared by every window (see MessageCenter).
    public ObservableCollection<NotificationItem> Notifications => _inbox.Messages;

    [ObservableProperty] private int  _unreadCount;
    [ObservableProperty] private bool _notificationsOpen;

    // ── Background activity ───────────────────────────────────────────────
    private readonly BackgroundActivityManager _activityManager;

    public ObservableCollection<BackgroundTask> BackgroundTasks => _activityManager.Tasks;

    /// <summary>The jobs this window's workspace has running in the background: the AI bar's pill and its list.</summary>
    public WorkspaceBackgroundTasks WorkspaceTasks { get; }

    partial void OnCurrentRuntimeChanged(WorkspaceRuntime value) => WorkspaceTasks.Refresh();

    /// <summary>The workspace's assistant may now hold a spoken conversation or not, or its own models came into or went out of reach:
    /// the mic and the AI's line follow it.</summary>
    private void OnAssistantReadinessChanged(object? sender, EventArgs e) => _ui.InvokeAsync(() =>
    {
        RecomputeVoiceAvailable();
        RecomputeLocalAiNotice();
    });

    /// <summary>Page-registered controls shown just left of the activity ticker (per-workspace, shared across
    /// this workspace's windows). See <see cref="ShellServices.MediatedTasks"/> / <c>RegisterMediatedTask</c>.</summary>
    public ObservableCollection<MediatedTaskRegistration> MediatedTasks => _shellServices.MediatedTasks;

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

    /// <summary>Mic button enabled only when the workspace can hold a spoken conversation: its Speech In chain has a model ready to
    /// listen, a voice speaks the language, and a model answers (<see cref="AIService.CanConverse"/>).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleVoiceCommand))]
    private bool _voiceAvailable;

    /// <summary>This window's spoken conversation: the bar in voice mode, and what it shows over the page.</summary>
    public VoiceConversationViewModel Voice { get; }

    /// <summary>Whether the AI bar gives its top line to the assistant: in a voice conversation, while a response is in the bar,
    /// and while the workspace has background tasks, whose pill sits at the line's end.</summary>
    public bool ShowsAiLine => Voice.IsActive || Ai.InBar || WorkspaceTasks.Any || LocalAiNotice is not null;

    /// <summary>Why the workspace's models of Nexaflow's own cannot run — the host they run in is out of reach, the machine's AI host
    /// service stopped, say — where the workspace uses any; null otherwise. The AI's line says it while it has nothing else to say, and
    /// a request that needs one is answered with it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsLocalAiNotice))]
    private string? _localAiNotice;

    /// <summary>The AI's line shows <see cref="LocalAiNotice"/>: it has one, and nothing else to say.</summary>
    public bool ShowsLocalAiNotice => LocalAiNotice is not null && !Voice.IsActive && !Ai.InBar;

    partial void OnLocalAiNoticeChanged(string? value) => RecheckAiLine();

    private void RecomputeLocalAiNotice()
    {
        LocalAiNotice = CurrentRuntime.AiService.OwnModelsUnreachable is { } why ? Str.Format("Shell.AiBar.LocalAiUnreachable", why) : null;
    }

    /// <summary>Whether the AI's line was shown when last told: its coming and going is animated, so it is told only when it changes.</summary>
    private bool _aiLineShown;

    private void RecheckAiLine()
    {
        if (ShowsAiLine == _aiLineShown) return;
        _aiLineShown = ShowsAiLine;
        OnPropertyChanged(nameof(ShowsAiLine));
    }

    /// <summary>How much of the model's context the conversation in the AI bar fills — the voice conversation's while one is
    /// held, else the typed one's while its reply is in the bar; null when there is none, or no model has said.</summary>
    public ContextUse? AiContextUse => Voice.IsActive ? Voice.ContextUse : Ai.InBar ? Ai.ContextUse : null;

    /// <summary>When on, the active page's context is attached to conversational AI calls.</summary>
    [ObservableProperty] private bool    _includeContext = true;

    /// <summary>Inline completion proposed by the clear-winner query handler; null = none.</summary>
    [ObservableProperty] private string? _aiCompletionSuggestion;

    /// <summary>The requests this window's AI bar sends, and how each is answered.</summary>
    private readonly AssistantRequests _requests;

    /// <summary>What the words in the AI bar point at, which the bar's symbol, listening state and completion show.</summary>
    private readonly TypedQueryPreview _preview;

    // ── Toast (transient presentation of a message) ────────────────────────
    // One toast shows at a time; further posts queue. ActiveToast is the message currently displayed.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToastVisible))]
    private NotificationItem? _activeToast;

    public bool ToastVisible => ActiveToast is not null;

    /// <summary>True during the toast's fade-out tail — the view animates opacity to 0 over
    /// <see cref="ToastFadeMs"/>ms; the message stays "active" until the fade completes.</summary>
    [ObservableProperty] private bool _toastFading;

    /// <summary>How long the fade-out tail runs after a toast's visible duration elapses.</summary>
    private const int ToastFadeMs = 2000;

    private readonly Queue<NotificationItem> _toastQueue = new();
    private CancellationTokenSource? _toastDismissCts;

    /// <summary>Queues a message as a toast; shows it immediately if nothing is currently displayed.</summary>
    private void ShowToast(NotificationItem message)
    {
        _toastQueue.Enqueue(message);
        if (ActiveToast is null) DisplayNextToast();
    }

    private void DisplayNextToast()
    {
        _toastDismissCts?.Cancel();
        if (_toastQueue.Count == 0) { ActiveToast = null; ToastFading = false; return; }

        var message = _toastQueue.Dequeue();
        ActiveToast  = message;
        ToastFading  = false;   // fully opaque until its visible duration elapses

        _toastDismissCts = new CancellationTokenSource();
        _ = RunToastLifetimeAsync(message, _toastDismissCts.Token);
    }

    /// <summary>Holds the toast for its visible duration, fades it over <see cref="ToastFadeMs"/>ms, then
    /// advances. Cancelled (manual dismiss / a newer toast) short-circuits without fading.</summary>
    private async Task RunToastLifetimeAsync(NotificationItem message, CancellationToken token)
    {
        try
        {
            await Task.Delay(message.ToastLifetime, token);
            await _ui.InvokeAsync(() => { if (ActiveToast == message) ToastFading = true; });
            await Task.Delay(ToastFadeMs, token);
        }
        catch (OperationCanceledException) { return; }

        _ui.Invoke(() => { if (ActiveToast == message) DisplayNextToast(); });
    }

    private void ShowError(string title, string message)
        => _inbox.Post(new NotificationItem
        {
            Workspace = CurrentRuntime.Workspace, Title = title, Body = message, Severity = MessageSeverity.Error, ShowToast = true,
        });

    public void ShowErrorToast(string message) => ShowError(Str.Get("Shell.Errors.Settings"), message);

    /// <summary>Posts a transient informational toast (e.g. "Exported N conversations").</summary>
    public void ShowInfoToast(string title, string message)
        => _inbox.Post(new NotificationItem
        {
            Workspace = CurrentRuntime.Workspace, Title = title, Body = message, Severity = MessageSeverity.Info, ShowToast = true,
        });

    // ── Unified overlay host ──────────────────────────────────────────────
    // The modal-overlay state machine lives in Overlays (OverlayCoordinator): the overlay host's
    // ActiveOverlay, feature-pushed overlays, and the built-in confirmation/prompt modals. The
    // per-overlay flags OptionsOpen/WorkspaceConfigOpen stay HERE (they drive workspace-switch
    // gating, airspace coverage and deep-links); their lazily-built panel VMs are pushed into the
    // coordinator when a flag flips. The members below are thin forwarders — the coordinator's
    // PropertyChanged is re-raised on this VM under the same names, so XAML bindings are unchanged.

    public OverlayCoordinator Overlays { get; }

    private OptionsViewModel?  _optionsPanel;
    private WorkspaceConfigViewModel? _workspaceConfigPanel;

    /// <summary>The content view-model shown in the shell's overlay host, or null when nothing is open.</summary>
    public object? ActiveOverlay => Overlays.ActiveOverlay;

    /// <summary>The open confirmation/prompt, shown by the modal host above the overlay host.</summary>
    public object? ActiveModal => Overlays.ActiveModal;

    /// <summary>True while a feature-pushed overlay (not a built-in modal) is showing.</summary>
    public bool HasFeatureOverlay => Overlays.HasFeatureOverlay;

    public bool   ConfirmationVisible => Overlays.ConfirmationVisible;
    public bool   PromptVisible       => Overlays.PromptVisible;

    /// <summary>Shows a feature-supplied overlay view-model. Backs <see cref="IShellServices.ShowOverlay"/>.</summary>
    public void ShowOverlay(object overlayViewModel) => Overlays.ShowOverlay(overlayViewModel);

    /// <summary>Closes whatever overlay is currently active (feature overlay or the built-in modal).</summary>
    public void CloseOverlay() => Overlays.CloseOverlay();

    public void ShowConfirmation(string title, string prompt, Action onConfirm, Action? onCancel = null,
                                 string? confirmLabel = null, string? cancelLabel = null)
        => Overlays.ShowConfirmation(title, prompt, onConfirm, onCancel, confirmLabel, cancelLabel);

    public void ShowPrompt(string title, string label, string initialValue,
                           Action<string> onConfirm, Action? onCancel = null)
        => Overlays.ShowPrompt(title, label, initialValue, onConfirm, onCancel);

    // Builds the Options panel VM on open and re-homes the wiring that used to live in MainWindow code-behind.
    private OptionsViewModel BuildOptionsPanel()
    {
        var vm = new OptionsViewModel(_session.Config, ConfigPageModels.Drawn(_views.Registry, _session), _session.Features, _runtimes, _shellServices);
        vm.SaveError           += ShowErrorToast;
        
        vm.SaveCompleted       += () =>
        {
            OptionsOpen = false;
            // Neither a theme nor a language live-reflows (StaticResource, and strings resolved once as the XAML
            // loads — both by design); restart this window in place, reopening the same pane layout.
            var shell = _session.Shell;
            if (shell.Theme == ThemeManager.Current && _session.Language.IsCurrent(shell.Language)) return;
            if (shell.Theme != ThemeManager.Current)
                ThemeManager.Apply(shell.Theme, _session.Features.Activation.ThemeContributionUris);
            _session.Language.Select(shell.Language);
            _shellServices.ReplaceWindow(this);
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
        var workspace = ConfigureTargetWorkspace ?? CurrentRuntime.Workspace;
        var vm = new WorkspaceConfigViewModel(workspace, _session, ConfigPageModels.Drawn(_views.Registry, _session), _shellServices);
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
        Overlays.OptionsPanel = _optionsPanel;
    }

    // ── Workspaces / workspace ──────────────────────────────────────────────
    [ObservableProperty, NotifyPropertyChangedFor(nameof(BreadcrumbDisplayFilters))] private WorkspaceRuntime _currentRuntime = null!;

    /// <summary>The saved workspaces shown in the dropdown.</summary>
    public ObservableCollection<Workspace> Workspaces => _session.SavedWorkspaces.Workspaces;

    /// <summary>False while a modal overlay (Options / Manage-AI) is open — blocks workspace switching.</summary>
    public bool CanSwitchWorkspace => !OptionsOpen && !WorkspaceConfigOpen;

    [RelayCommand]
    private void SelectWorkspace(Workspace workspace)
    {
        // Modal overlays block switching (their edits target the active workspace/workspace).
        if (!CanSwitchWorkspace) return;
        if (ReferenceEquals(workspace, CurrentRuntime?.Workspace)) return;

        // Cancel any in-flight AI send before the window leaves the workspace it was sent in.
        _requests.Cancel();

        MoveToWorkspace(workspace);
    }

    /// <summary>
    /// Moves this window to <paramref name="workspace"/>. It joins that workspace's runtime — the one running, the dormant one
    /// resumed, or a new one — before it leaves its own, so the session is never without a window. Leaving closes its tabs; its
    /// workspace's other windows stay as they are, and when this was the last of them the workspace records its session and turns
    /// dormant. The window then shows the workspace it joined: the session it left, when it was dormant; its startup tabs otherwise.
    /// </summary>
    private void MoveToWorkspace(Workspace workspace) => _session.WorkspaceWindows.Move(this, CurrentRuntime, workspace);

    /// <summary>This window now shows <paramref name="to"/>: what it follows of a runtime — its providers, for the mic and the
    /// AI's line — it follows there.</summary>
    private void AttachRuntime(WorkspaceRuntime from, WorkspaceRuntime to)
    {
        from.AiService.ReadinessChanged -= OnAssistantReadinessChanged;
        CurrentRuntime = to;
        to.AiService.ReadinessChanged += OnAssistantReadinessChanged;
        RecomputeVoiceAvailable();
        RecomputeLocalAiNotice();
    }

    /// <summary>The window moved to <paramref name="to"/>: leaving closed its tabs, so the panes that showed them go too, and the
    /// window starts over on one empty pane, as a new window does, for the workspace it joined to fill.</summary>
    void IWindowHost.Rejoin(WorkspaceRuntime from, WorkspaceRuntime to)
    {
        WireRootPane();
        AttachRuntime(from, to);
    }

    // ── Options overlay ───────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitchWorkspace))]
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
    [NotifyPropertyChangedFor(nameof(CanSwitchWorkspace))]
    private bool _workspaceConfigOpen;

    /// <summary>
    /// The workspace the Configure (Manage-AI) overlay targets — set by <see cref="ConfigureWorkspace"/>
    /// right before the overlay opens. Null means the active workspace. Cleared on close.
    /// </summary>
    public Workspace? ConfigureTargetWorkspace { get; private set; }

    /// <summary>Section (config name) the Configure overlay should land on when it next builds.
    /// Set via <see cref="OpenConfigureAt"/>; consumed and cleared by the window.</summary>
    public string? RequestedConfigureSection { get; set; }

    /// <summary>Opens the Configure overlay for <paramref name="workspace"/> on the section for
    /// <paramref name="configName"/> (a deep link, e.g. from the console page's Configure button).</summary>
    public void OpenConfigureAt(Workspace? workspace, string configName)
    {
        RequestedConfigureSection = configName;
        ConfigureWorkspace(workspace);
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
            // ConfigureTargetWorkspace / RequestedConfigureSection were set just before this flipped true.
            _workspaceConfigPanel = BuildWorkspaceConfigPanel();
            Overlays.WorkspaceConfigPanel = _workspaceConfigPanel;
        }
        else
        {
            // Closing: apply pending disk changes to the running workspace. If nothing changed and the
            // panel was opened from the Options → Workspaces tab, bounce back to Options on that tab.
            bool changed = _workspaceConfigPanel?.Close() ?? false;
            _workspaceConfigPanel         = null;
            Overlays.WorkspaceConfigPanel = null;
            ConfigureTargetWorkspace = null;

            bool returnToOptions = ConfigureReturnToOptions;
            ConfigureReturnToOptions = false;
            if (!changed && returnToOptions)
            {
                RequestedOptionsSection = "workcontexts";   // WorkspacesConfig.ConfigName
                OptionsOpen = true;
            }
        }
        // Both branches pushed the panel into Overlays, whose setter re-derives ActiveOverlay.
    }

    /// <summary>Opens the Configure panel for a workspace chosen in the Options → Workspaces tab,
    /// remembering to return to Options if nothing is changed.</summary>
    public void OpenConfigureFromOptions(Workspace workspace)
    {
        OptionsOpen = false;
        ConfigureReturnToOptions = true;
        ConfigureWorkspace(workspace);
    }

    /// <summary>
    /// The shell's default AI response handler: the AI bar's own line for progress, approvals and replies, and the reply card
    /// above it for the whole of one. Used whenever the active page does not render AI responses itself. The bar and
    /// <c>AiReplyCard.xaml</c> bind to this. See <see cref="IAIResponseHandler"/>.
    /// </summary>
    public ShellAIResponseHandler Ai { get; }

    /// <summary>The window and tab registry of the workspace this window shows now.</summary>
    private ShellServices _shellServices => CurrentRuntime.ShellServices;

    /// <summary>The session's live workspaces, which this window moves between.</summary>
    private readonly WorkspaceRuntimes _runtimes;

    /// <summary>The session's inbox: this window shows every message in it and toasts the new ones.</summary>
    private readonly MessageCenter _inbox;

        /// <summary>What this window is given of the session: its page views, language and components among them.</summary>
        private readonly WindowServices _session;

        /// <summary>The view each open page is drawn with, which this window's panes show.</summary>
        private readonly PageViews _views;

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
        || Ai.Card.IsOpen || _ribbon?.IsEditOpen == true || WorkspaceTasks.IsOpen;

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
            if ((_views.Shown(page) as IAirspaceContent ?? page.ViewModel as IAirspaceContent) is { } airspace)
                airspace.SetCoveredByOverlay(covered);
    }

    public ShellViewModel(WindowServices session, PageViews views, WorkspaceRuntime workspace)
    {
        _session = session;
        _views   = views;
        var activityManager = session.Activities;
        _activityManager = activityManager;
        _inbox           = session.Inbox;
        // NB: AiIsBusy is NOT coupled to background activity — the AI bar locks only while an actual
        // AI run is in flight (driven by SendAiMessage). Background tasks (NuGet checks, downloads,
        // conversation analysis) report through the activity ticker without blocking AI input.
        _currentRuntime = workspace;
        _runtimes       = session.Workspaces;
        WorkspaceTasks = new WorkspaceBackgroundTasks(activityManager, () => CurrentRuntime);
        WorkspaceTasks.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceBackgroundTasks.IsOpen)) RefreshOverlayCoverage();
            if (e.PropertyName == nameof(WorkspaceBackgroundTasks.Any))    RecheckAiLine();
        };
        Ai = new ShellAIResponseHandler(new BarReply(new WpfUiThread(_ui), this));
        _requests = new AssistantRequests(() => CurrentRuntime.AiService, this);
        _requests.BusyChanged += (_, _) => AiIsBusy = _requests.IsBusy;
        _requests.Failed      += ex => ShowError(Str.Get("Shell.Errors.Ai"), ex.Message);
        // The user cancelled what they sent: it goes back in the bar to be changed and sent again.
        _requests.Cancelled   += text => AiInputText = text;
        _preview = new TypedQueryPreview(new WpfUiThread(_ui), () => CurrentRuntime.AiService, () => ActivePageViewModel);
        _preview.Changed += (_, _) =>
        {
            AiHandlerSymbol        = _preview.Current.Symbol;
            AiIsListening          = _preview.Current.Listening;
            AiCompletionSuggestion = _preview.Current.Completion;
        };
        Voice = new VoiceConversationViewModel(_ui);

        // Modal-overlay state machine; the flags stay here (they gate switching/deep-links), the
        // coordinator owns everything else. Re-raise its changes under the same property names so
        // existing XAML bindings (ActiveOverlay, ConfirmationVisible, …) and the airspace-coverage watcher
        // keep working against this VM.
        // Escape closes a panel the way its ✕ does: asking first when it holds edits closing would lose.
        Overlays = new OverlayCoordinator(
            () => OptionsOpen, () => WorkspaceConfigOpen,
            DismissOptions, CloseWorkspaceConfig);
        Overlays.PropertyChanged += (_, e) => OnPropertyChanged(e.PropertyName);

        // Keep airspace-hosting pages (WebView2) hidden while a modal overlay covers the page area —
        // its native HWND would otherwise render above the overlay. Track the contributing overlay flags.
        PropertyChanged    += OnShellPropertyChangedForOverlay;
        Ai.PropertyChanged += (_, e) =>
        {
        if (e.PropertyName == nameof(ShellAIResponseHandler.InBar))
            {
                RecheckAiLine();
                OnPropertyChanged(nameof(ShowsLocalAiNotice));
            }
            if (e.PropertyName is nameof(ShellAIResponseHandler.InBar) or nameof(ShellAIResponseHandler.ContextUse))
                OnPropertyChanged(nameof(AiContextUse));
        };
        Voice.PropertyChanged += (_, e) =>
        {
        if (e.PropertyName == nameof(VoiceConversationViewModel.IsActive))
            {
                RecheckAiLine();
                OnPropertyChanged(nameof(ShowsLocalAiNotice));
            }
            if (e.PropertyName is nameof(VoiceConversationViewModel.IsActive) or nameof(VoiceConversationViewModel.ContextUse))
                OnPropertyChanged(nameof(AiContextUse));
        };
        Ai.Card.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReplyCard.IsOpen)) RefreshOverlayCoverage();
        };

        _help = new HelpPaneController(this);   // before the panes exist: their first page change already asks it
        WireRootPane();

        

        // ── Voice input availability + the AI's line ─────────────────────────
        // The mic follows the workspace's Speech In chain: a model assigned, downloaded, or taken away. What the
        // workspace's own models need is the host they run in: out of reach, the AI's line says so.
        workspace.AiService.ReadinessChanged += OnAssistantReadinessChanged;

        // A spoken conversation owns the bar as dictation does: what is heard fills the box, and the mic stops it.
        Voice.Heard += text => AiInputText = text;
        Voice.TurnTaken += _ => AiInputText = string.Empty;
        Voice.OpenInConversationRequested += said => _ = OpenVoiceConversationAsync(said);
        Voice.Ended += reason =>
        {
            AiInputText = string.Empty;
            if (reason is not null) ShowError(Str.Get("Shell.Errors.Voice"), reason);
        };
        Voice.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(VoiceConversationViewModel.IsActive))
            {
                IsRecording = VoiceActive = Voice.IsActive;
                ToggleVoiceCommand.NotifyCanExecuteChanged();
            }
            else if (e.PropertyName == nameof(VoiceConversationViewModel.State))
                AiIsListening = Voice.State == VoiceState.Listening;
        };

        RecomputeVoiceAvailable();
        RecomputeLocalAiNotice();

        // ── Shared inbox: this window's unread badge, and the toasts it claims ──
        _inbox.UnreadChanged += OnUnreadChanged;
        _inbox.MessagePosted += OnMessagePosted;
        UnreadCount = _inbox.UnreadCount;
    }

    /// <summary>Detaches shared-store handlers so a closed window's view-model can be collected.</summary>
    public void Detach()
    {
        _inbox.MessagePosted -= OnMessagePosted;

        // The runtime and the AI host outlive their windows: unhook them, or a closed window stays rooted. A window that closes while
        // listening stops listening.
        CurrentRuntime.AiService.ReadinessChanged -= OnAssistantReadinessChanged;
        _ = Voice.StopAsync();
        _preview.Dispose();

        _inbox.UnreadChanged -= OnUnreadChanged;

        // The Workspace outlives its windows — unhook its RibbonChanged or this window's ribbon
        // view-model stays rooted (and dead-reloads on every ribbon edit in surviving windows).
        Ribbon?.Detach();
        Ribbon = null;
    }

    private void OnMessagePosted(object? sender, NotificationItem message)
    {
        // Only the focused window pops the toast; unfocused windows leave it pending until they gain focus (DrainPendingToasts).
        if (_isFocused && _inbox.ClaimToast(message)) ShowToast(message);
    }

    private void DrainPendingToasts()
    {
        foreach (var message in _inbox.ClaimPendingToasts()) ShowToast(message);
    }

    private void OnUnreadChanged(object? sender, EventArgs e) => UnreadCount = _inbox.UnreadCount;

    private void RecomputeVoiceAvailable()
    {
        VoiceAvailable = CurrentRuntime.AiService.CanHoldSpokenConversation;
    }

    // ── Tab commands ──────────────────────────────────────────────────────

    [RelayCommand]
    private void ActivateTab(Page tab)
    {
        if (tab.IsActive && tab == ActiveTab)
        {
            tab.GetOrCreateViewModel();
            tab.Reinitialize(tab.PageParams ?? []);
            return;
        }

        ((IWindowHost)this).BringToFront(tab);
        ((IWindowHost)this).SetActiveTab(tab);
        tab.GetOrCreateViewModel();
        tab.Reinitialize(tab.PageParams ?? []);
    }

    [RelayCommand]
    private void CloseTab(Page tab) => _shellServices.CloseTab(tab);

    /// <summary>Ctrl+W: close the focused pane's active tab. No-ops while a window-modal overlay owns the
    /// page (Options, Manage-AI, a confirmation prompt, …) so the shortcut can't pull a tab out from under
    /// it, or when the pane has no tab to close. The guard lives here rather than in a command CanExecute so
    /// the shell key handler's trigger stays a simple key match (a throwing/false CanExecute in the key
    /// handler's condition would silently swallow the shortcut).</summary>
    [RelayCommand]
    private void CloseActiveTab()
    {
        if (AnyOverlayCoversPage) return;
        if (ActiveTab is { } tab) CloseTab(tab);
    }

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
        => _shellServices.OpenTab(req.PageKind, req.PageParams, ActiveTab);

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
        var left = OwningPane(tab) ?? SoleLeaf;
        left.Remove(tab);
        var right = new Pane(_views);
        right.Add(tab);
        Split(left, right);
    }

    // Can't split when already split (v1 is two panes max), nor split off the only tab (the source would empty).
    private bool CanSplitRight(Page? tab) => !IsSplit && tab is not null && (OwningPane(tab)?.Pages.Count ?? 0) >= 2;

    /// <summary>Splits the tab area, adding an empty right-hand pane.</summary>
    [RelayCommand(CanExecute = nameof(CanSplitEmpty))]
    private void SplitEmpty() => Split(SoleLeaf, new Pane(_views));

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
            var executor = _session.Features.GetRibbonItemExecutor(item.PageKind, CurrentRuntime);
            if (executor is not null)
            {
                var paths = (ActivePageView as ISelectionProvider ?? ActivePageViewModel as ISelectionProvider)?.SelectedFilePaths
                            ?? (IReadOnlyList<string>)[];
                var ctx = new RibbonExecutionContext(
                    paths,
                    msg           => ShowError(Str.Get("Shell.Ribbon.ActionError"), msg),
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

        var executor = _session.Features.GetRibbonItemExecutor(item.PageKind, CurrentRuntime);
        if (executor is not null)
        {
            // Create the new window first so any OpenTab calls inside the executor land there.
            var newHost = _shellServices.CreateAndShowNewWindow();
            if (newHost is null) return;

            var paths = (ActivePageView as ISelectionProvider ?? ActivePageViewModel as ISelectionProvider)?.SelectedFilePaths
                        ?? (IReadOnlyList<string>)[];
            var ctx = new RibbonExecutionContext(
                paths,
                msg           => ShowError(Str.Get("Shell.Ribbon.ActionError"), msg),
                (t, m, ok, c) => ShowConfirmation(t, m, ok, c),
                ()            => Ribbon?.Items.Remove(item));
            executor.Execute(item.PageParams, ctx);

            // If the action didn't open a tab, the new window serves no purpose.
            if (newHost.Tabs.Count == 0)
                newHost.Close();
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
        if (NotificationsOpen) _inbox.MarkAllRead();
    }

    [RelayCommand]
    private void DismissNotification(NotificationItem item) => _inbox.Remove(item);

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

    /// <summary>
    /// Options' ✕ and Escape: asks first when a page holds unsaved edits. Its Cancel button is the deliberate discard
    /// and closes without asking (<see cref="CloseOptions"/>). Saving closes the panel itself when it succeeds.
    /// </summary>
    [RelayCommand]
    private void DismissOptions()
        => Overlays.CloseOrAskAboutEdits(
            _optionsPanel?.UnsavedSections ?? [],
            apply: () => { _optionsPanel?.SaveCommand.Execute(null); return false; },
            Str.Get("Shell.Prompt.Unapplied.Save"),
            close: () => OptionsOpen = false);

    // ── Help ──────────────────────────────────────────────────────────────
    // The Help button / F1: this page's help in the pane beside it (see HelpPaneController).

    [RelayCommand]
    private void ToggleHelp()
    {
        if (OptionsOpen || WorkspaceConfigOpen) return;   // a modal overlay owns the window
        _help.Toggle();
    }

    IReadOnlyList<Pane> IHelpPaneHost.LeafPanes => LeafPanes.ToList();

    Pane IHelpPaneHost.FocusedPane => FocusedPane;

    Pane IHelpPaneHost.SplitBeside(Pane subject)
    {
        if (RootPaneNode is SplitPaneNode split)
            return ReferenceEquals(split.First, subject) ? split.Second : split.First;
        var beside = new Pane(_views);
        Split(subject, beside);
        return beside;
    }

    void IHelpPaneHost.OpenInPane(Pane pane, string pageKind, Dictionary<string, string> pageParams)
    {
        FocusedPane = pane;   // AddTab lands in the focused pane
        _shellServices.AddFreshTab(this, pageKind, pageParams);
    }

    void IHelpPaneHost.Activate(Page page) => ((IWindowHost)this).SetActiveTab(page);

    void IHelpPaneHost.Close(Page page) => _shellServices.CloseTab(page);

    // ── Configure (per-workspace) ─────────────────────────────────────────

    /// <summary>Close and ✕: asks first when a page holds edits not yet applied.</summary>
    [RelayCommand]
    private void CloseWorkspaceConfig()
        => Overlays.CloseOrAskAboutEdits(
            _workspaceConfigPanel?.UnappliedSections ?? [],
            apply: () => _workspaceConfigPanel?.ApplyAllChanges() ?? true,
            Str.Get("Shell.Prompt.Unapplied.Apply"),
            close: () => WorkspaceConfigOpen = false);

    /// <summary>Opens the per-workspace Configure panel for a specific workspace (may be non-active).</summary>
    [RelayCommand]
    private void ConfigureWorkspace(Workspace? workspace)
    {
        if (workspace is null) return;
        ConfigureTargetWorkspace = workspace;
        WorkspaceConfigOpen = true;
    }

    /// <summary>Right-click entry point: configure the currently active workspace.</summary>
    [RelayCommand]
    private void ConfigureCurrentWorkspace() => ConfigureWorkspace(CurrentRuntime?.Workspace);

    /// <summary>
    /// Right-click entry point: create a new workspace by cloning the current one (settings copied,
    /// fresh symbol/colour) and open the Configure panel on it. Mirrors the Options "Clone" action but
    /// routes straight into Configure so the new workspace can be named/recoloured and tuned at once.
    /// </summary>
    [RelayCommand]
    private void NewWorkspace()
    {
        if (CurrentRuntime?.Workspace is not { } source) return;

        ConfigureWorkspace(_session.WorkspaceWindows.CopyOf(source));   // open Configure on its identity page
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
        if (CurrentRuntime?.Workspace is not { } workspace) return;

        var count = _session.WorkspaceWindows.UseAsDefaultTabs(workspace, this);
        ShowInfoToast(Str.Get("Shell.Workspace.DefaultTabs.Set"),
        count == 0 ? Str.Get("Shell.Workspace.DefaultTabs.SavedNone")
        : count == 1 ? Str.Get("Shell.Workspace.DefaultTabs.SavedOne")
        : Str.Format("Shell.Workspace.DefaultTabs.SavedFormat", count));
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
    /// Configure panel's "Delete Workspace": confirms, then permanently deletes the workspace (config +
    /// every conversation). Refuses the last remaining workspace (the button is also disabled then).
    /// A deletable workspace may be the active one or one open elsewhere: its windows are closed first
    /// (collapsing the current one to a single instance), the data is deleted, then the remaining
    /// window closes — removing the workspace from active memory.
    /// </summary>
    public void RequestDeleteWorkspace(Workspace workspace)
    {
        if (!_session.WorkspaceWindows.CanDelete(workspace)) { ShowErrorToast(Str.Get("Shell.Workspace.LastOne")); return; }

        var liveWs = _runtimes.Find(workspace);

        var message = ReferenceEquals(liveWs, CurrentRuntime) && liveWs is not null
        ? Str.Format("Shell.Workspace.Delete.AskActiveFormat", workspace.Name)
        : liveWs is not null
        ? Str.Format("Shell.Workspace.Delete.AskOpenFormat", workspace.Name)
        : Str.Format("Shell.Workspace.Delete.AskFormat", workspace.Name);

        ShowConfirmation(Str.Get("Shell.Workspace.DeleteZone.Title"), message, onConfirm: () => DeleteWorkspace(workspace));
    }

    private void DeleteWorkspace(Workspace workspace)
    {
        if (!_session.WorkspaceWindows.Delete(workspace, this)) { ShowErrorToast(Str.Get("Shell.Workspace.LastOne")); return; }
        WorkspaceConfigOpen = false;
    }

    // ── AI ────────────────────────────────────────────────────────────────

    partial void OnAiInputTextChanged(string value)
    {
        // Mirror what's being typed to the active page (e.g. the terminal's faux-prompt echo). Always
        // pushed — including an empty value — so the preview clears on send/clear.
        PushInputPreview(value);

        // While listening, voice owns the display — no handler is scored, nor completion proposed, against the live transcription.
        if (IsRecording)
        {
            _preview.Cancel();
            AiHandlerSymbol        = null;
            AiCompletionSuggestion = null;
            return;
        }

        _preview.Update(value);
    }

    // ── Chat-bar feature handlers (keys / drop / live preview) ────────────
    //
    // The AI input bar is shell chrome, but the active page's feature may want to participate: claim a
    // key (terminal history / completion), turn a drop into inserted text, or mirror typing. Each is a
    // contract discovered + instantiated per workspace by FeatureManager, consulted against the active
    // page's view-model — mirroring how IQueryHandler is scored against it.

    /// <summary>Consulted by the AI input control before its own key handling. Returns the first handled
    /// result (optionally rewriting the bar text), or <see cref="ChatKeyResult.NotHandled"/>.</summary>
    public ChatKeyResult HandleChatKey(ConsoleKey key, KeyModifiers modifiers, string text, int caretIndex)
    {
        var pageVm = ActivePageViewModel;
        foreach (var h in _session.Features.GetChatKeyHandlers(CurrentRuntime))
        {
            if (!h.CanHandle(pageVm)) continue;
            var result = h.HandleKey(key, modifiers, text, caretIndex, pageVm);
            if (result.Handled) return result;
        }
        return ChatKeyResult.NotHandled;
    }

    /// <summary>The handlers the AI bar's drops are put to, for this window's runtime.</summary>
    internal IReadOnlyList<IChatDropHandler> ChatDropHandlers => _session.Features.GetChatDropHandlers(CurrentRuntime);

    /// <summary>What restates this window's crumbs before the bar draws them — the current runtime's filters.</summary>
    public IReadOnlyList<IBreadcrumbDisplayFilter> BreadcrumbDisplayFilters => _session.Features.GetBreadcrumbDisplayFilters(CurrentRuntime);

    /// <summary>The focused pane's page view-model, made if the page has not been shown yet.</summary>
    internal IPageViewModel? ActivePageViewModel => FocusedPane.ActivePage?.GetOrCreateViewModel();

    IPageViewModel? IBarReplyHost.ActivePageViewModel => ActivePageViewModel;

    /// <summary>
    /// What was dropped on a pane's tabs — files, or a link — each opened in a tab of its own. This window is made the
    /// focused one first: a drag from another application lands on a window that has not got the focus, and tabs open in
    /// the one that has.
    /// </summary>
    [RelayCommand]
    private void OpenDropped(IReadOnlyList<object> dropped)
    {
        _shellServices.SetFocused(this);
        try { Window.Activate(); } catch (InvalidOperationException) { /* closing */ }

        foreach (var one in dropped)
            if (!_shellServices.HandleObject(one))
                _shellServices.ShowError(Str.Format("Shell.OpenFromOutside.CannotOpenFormat", one is FileViewRequest file ? file.Path : one));
    }

    private void PushInputPreview(string text)
    {
        var pageVm = ActivePageViewModel;
        foreach (var p in _session.Features.GetChatInputPreviews(CurrentRuntime))
            if (p.CanPreview(pageVm)) p.OnInputChanged(text, pageVm);
    }

    [RelayCommand]
    private async Task SendAiMessage()
    {
        var text = AiInputText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        // Talking: a typed turn is a turn of the spoken conversation, answered aloud.
        if (Voice.IsActive)
        {
            AiInputText = string.Empty;
            Voice.Submit(text);
            return;
        }

        AiInputText = string.Empty;
        await _requests.SendAsync(text, ActivePageViewModel);
    }

    /// <summary>Picks up work the assistant set going in the background that has ended, while the user is still on <paramref name="context"/>.</summary>
    public Task ContinueAfterBackgroundJobAsync(IPageViewModel context, string about, string outcome)
        => _requests.PickUpAsync(context, about, outcome);

    IAIResponseHandler IAssistantBar.Surface => Ai.Reply;

    AgentRunOptions IAssistantBar.RunOptions() => Ai.Reply.Run();

    Task IAssistantBar.PrefillAsync(string text) => AnimatePrefillAsync(text);

    void IAssistantBar.Touched(IReadOnlyList<string> paths) => Ai.Reply.AgentContextPaths = paths;

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

    // Mic is usable when Speech In has a model — one still to download counts, the first press offering it — and no AI
    // request is processing. While recording, AiIsBusy stays false so this remains true (to allow stop).
    private bool CanToggleVoice => Voice.IsActive || (VoiceAvailable && !AiIsBusy);

    /// <summary>
    /// The mic: a spoken conversation where the workspace's models can hold one — something listening for turns, a voice
    /// for the app's language, a model to answer — dictation into the box where they cannot. Pressed again, it stops.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanToggleVoice))]
    private async Task ToggleVoice()
    {
        if (Voice.IsActive) { await Voice.StopAsync(); return; }
        

        var ai = CurrentRuntime.AiService;
        if (ai is null || !await ai.MakeReadyAsync(AiAbility.SpeechIn)) return;   // a model still to download offers it, and stops here
        if (!ai.CanConverse(ai.ConversationLanguage)) return;   // the workspace's models changed since the mic was offered; it is offered again when they can
        var pageVm = ActivePageViewModel;
        try
        {
            await Voice.StartAsync(ai, () => ActivePageViewModel, () => IncludeContext, pageVm as IAIResponseHandler);
        }
        catch (Exception ex)
        {
            ShowError(Str.Get("Shell.Errors.Voice"), ex.Message);
        }
    }

    /// <summary>Carries a voice conversation on in a conversation page: what was said becomes the conversation's first
    /// messages, saved like any other, and the mic there carries it on by voice.</summary>
    private async Task OpenVoiceConversationAsync(IReadOnlyList<LlmMessage> said)
    {
        var id = await CurrentRuntime.AiService.SaveSpokenConversationAsync(said);
        ShellServices.OpenTab("Conversation", new Dictionary<string, string> { ["conversationId"] = id });
    }

    /// <summary>Cancels what the bar sent: the agent's run stops at its next step, and the words go back in the bar.</summary>
    [RelayCommand]
    private void CancelAi() => _requests.Cancel();

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

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.AvaloniaShell.Views;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Localization;
using SessionRect = Nexaflow.Session.Models.PixelRect;

namespace Nexaflow.AvaloniaShell.Shell;

/// <summary>
/// One shell window, as the session sees it (<see cref="IWindowHost"/>) and as its view binds it: the workspace it shows and the
/// selector that moves it to another (<see cref="WorkspaceWindows.Move"/>), that workspace's ribbon, the session's background tasks
/// and messages, the open tabs and the active one's breadcrumb trail, and the AI bar (<see cref="AssistantRequests"/>,
/// <see cref="TypedQueryPreview"/>). Built for its Avalonia view, as Nexaflow.Core's <c>ShellViewModel</c> is for the WPF window;
/// the session is what the two share.
/// </summary>
internal sealed partial class ShellWindowModel : ObservableObject, IWindowHost, IAssistantBar
{
    /// <summary>How a reply in the bar is written. Nexaflow.Core keeps the same words in its <c>ShellAIResponseHandler</c>.</summary>
    private const string BarStyle =
        "Your reply is shown on one line of the AI bar, the whole of it a hover away: lead with the answer in a sentence or " +
        "two, and write more only when the request needs it.";

    /// <summary>How long a toast stays before it fades.</summary>
    private static readonly TimeSpan ToastStays = TimeSpan.FromSeconds(5);

    private readonly WindowServices _session;
    private readonly AvaloniaPageViews _views;
    private readonly IUiThread _ui;
    private readonly AssistantRequests _requests;
    private readonly TypedQueryPreview _preview;
    private readonly IUiTimer _toastTimer;
    private Page? _crumbsOf;

    public ShellWindowModel(ShellWindow window, WindowServices session, AvaloniaPageViews views, IUiThread ui, WorkspaceRuntime runtime)
    {
        Window   = window;
        _session = session;
        _views   = views;
        _ui      = ui;
        CurrentRuntime = runtime;
        Bar = new BarSurface(ui);
        Bar.PropertyChanged += OnBarChanged;
        _toastTimer = ui.CreateTimer(ToastStays, () => IsToastShown = false, UiTimerPriority.Background, once: true);

        _requests = new AssistantRequests(() => CurrentRuntime.AiService, this);
        _requests.BusyChanged += (_, _) => { AiIsBusy = _requests.IsBusy; ShowDot(); };
        _requests.Failed      += ex => ShowError(ex.Message);
        _requests.Cancelled   += text => AiInputText = text;
        _preview = new TypedQueryPreview(ui, () => CurrentRuntime.AiService, () => ActiveTab?.ViewModel);
        _preview.Changed += (_, _) => ShowDot();

        session.Inbox.UnreadChanged += OnUnreadChanged;
        session.Inbox.MessagePosted += OnMessagePosted;
        UnreadCount = session.Inbox.UnreadCount;

        AttachRuntime(null, runtime);
        foreach (var toast in session.Inbox.ClaimPendingToasts()) Toast(toast.Title, toast.Body);
    }

    /// <summary>The window this model is the session's handle on.</summary>
    public ShellWindow Window { get; }

    /// <summary>The runtime of the workspace the window shows now; a move changes it.</summary>
    public WorkspaceRuntime CurrentRuntime { get; private set; }

    /// <summary>Where the bar shows its replies.</summary>
    public BarSurface Bar { get; }

    /// <summary>The saved workspaces, which the selector lists.</summary>
    public ObservableCollection<Workspace> Workspaces => _session.SavedWorkspaces.Workspaces;

    /// <summary>The session's background tasks, the first of which the ticker under the logo tells.</summary>
    public ObservableCollection<BackgroundTask> Activities => _session.Activities.Tasks;

    /// <summary>The session's messages, newest first.</summary>
    public ObservableCollection<NotificationItem> Messages => _session.Inbox.Messages;

    /// <summary>The ribbon's buttons and the separators between their groups.</summary>
    public ObservableCollection<object> Ribbon { get; } = [];

    public ObservableCollection<ShellTab> OpenTabs { get; } = [];

    /// <summary>The active page's breadcrumb trail.</summary>
    public ObservableCollection<Crumb> Crumbs { get; } = [];

    [ObservableProperty] private ShellTab? _selectedTab;
    [ObservableProperty] private string _title = "";

    /// <summary>The workspace's own colour, which fills its selector and the ribbon's accent.</summary>
    [ObservableProperty] private IBrush _workspaceBrush = Brushes.Transparent;

    /// <summary>The workspace's own icon, drawn on its selector.</summary>
    [ObservableProperty] private string _workspaceIcon = "";

    [ObservableProperty] private string _workspaceTip = "";

    [ObservableProperty] private bool _isWorkspaceListOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    private int _unreadCount;

    public bool HasUnread => UnreadCount > 0;

    [ObservableProperty] private bool _isMessagesOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlay))]
    private Control? _overlay;

    public bool HasOverlay => Overlay is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrompt))]
    private ShellPrompt? _prompt;

    public bool HasPrompt => Prompt is not null;

    [ObservableProperty] private string _toastTitle = "";
    [ObservableProperty] private string _toastBody = "";
    [ObservableProperty] private bool _isToastShown;

    [ObservableProperty] private string _aiInputText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _aiIsBusy;

    public bool IsIdle => !AiIsBusy;

    [ObservableProperty] private bool _includeContext = true;

    /// <summary>The bar shows the assistant's line above the user's: a request is under way or has been answered.</summary>
    public bool ShowsAiLine => Bar.IsOpen;

    // The status dot: one of these at a time, in this order — busy, a handler owns the words, the assistant would answer, ready.
    [ObservableProperty] private bool _dotBusy;
    [ObservableProperty] private bool _dotHandler;
    [ObservableProperty] private bool _dotListening;
    [ObservableProperty] private string _dotSymbol = "";
    [ObservableProperty] private string _dotTip = "";

    /// <summary>What the handler that owns the typed words would complete them with; Tab takes it.</summary>
    [ObservableProperty] private string _completion = "";

    // ── The window's own commands ─────────────────────────────────────────

    partial void OnSelectedTabChanged(ShellTab? oldValue, ShellTab? newValue)
    {
        if (oldValue is not null) oldValue.IsActive = false;
        if (newValue is not null) newValue.IsActive = true;
        FollowCrumbs(newValue?.Page);
    }

    partial void OnAiInputTextChanged(string value) => _preview.Update(value);

    /// <summary>Sends what is typed; while a run is under way it joins that run, so the command stays open.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Send()
    {
        var text = AiInputText.Trim();
        if (text.Length == 0) return;
        AiInputText = "";
        await _requests.SendAsync(text, ActiveTab?.ViewModel);
    }

    [RelayCommand]
    private void CancelAi() => _requests.Cancel();

    [RelayCommand]
    private void ClearInput() => AiInputText = "";

    [RelayCommand]
    private void ToggleWorkspaces() => IsWorkspaceListOpen = !IsWorkspaceListOpen;

    /// <summary>Moves the window to <paramref name="workspace"/>'s runtime — opened if it is not live.</summary>
    [RelayCommand]
    private void ShowWorkspace(Workspace workspace)
    {
        IsWorkspaceListOpen = false;
        if (ReferenceEquals(workspace, CurrentRuntime.Workspace)) return;
        _session.WorkspaceWindows.Move(this, CurrentRuntime, workspace);
    }

    /// <summary>A copy of the workspace the window shows, saved in the list, and the window moved to it.</summary>
    [RelayCommand]
    private void CopyWorkspace() => ShowWorkspace(_session.WorkspaceWindows.CopyOf(CurrentRuntime.Workspace));

    [RelayCommand]
    private void ToggleMessages()
    {
        IsMessagesOpen = !IsMessagesOpen;
        if (IsMessagesOpen) _session.Inbox.MarkAllRead();
    }

    [RelayCommand]
    private void ShowOptions() => OpenOptionsAt("");

    /// <summary>The Help pane is Core's page; this shell has none to open beside the page.</summary>
    [RelayCommand]
    private void ToggleHelp() => ShowNotification("Help has no Avalonia pane yet: the Help page is the WPF app's.");

    /// <summary>The ribbon editor is Core's view-model over the workspace's ribbon; this shell has none.</summary>
    [RelayCommand]
    private void EditRibbon() => ShowNotification("The ribbon editor has no Avalonia view yet.");

    /// <summary>The window is closing: everything the session outlives lets go of it.</summary>
    public void Detach()
    {
        _session.Inbox.UnreadChanged -= OnUnreadChanged;
        _session.Inbox.MessagePosted -= OnMessagePosted;
        if (CurrentRuntime.Workspace.Ribbon is { } ribbon) ribbon.Changed -= OnRibbonChanged;
        FollowCrumbs(null);
        _requests.Cancel();
        _preview.Dispose();
        _toastTimer.Dispose();
    }

    // ── IWindowHost ───────────────────────────────────────────────────────

    public IReadOnlyList<Page> Tabs => [.. OpenTabs.Select(tab => tab.Page)];

    public IReadOnlyList<DefaultTabDescriptor> CaptureTabLayout() =>
    [
        .. OpenTabs.Where(tab => tab.Page.PageKind is not null).Select(tab => new DefaultTabDescriptor
        {
            PageKind   = tab.Page.PageKind!,
            PageParams = tab.Page.PageParams is null ? null : new(tab.Page.PageParams),
            Title      = tab.Page.Title,
            IsActive   = ReferenceEquals(tab, SelectedTab),
        }),
    ];

    public bool IsFocused { get; set; }

    public Page? ActiveTab => SelectedTab?.Page;

    public void Show() => Window.Show();

    public void Close() => Window.Close();

    public void Activate() => Window.Activate();

    public void Restore()
    {
        if (Window.WindowState == WindowState.Minimized) Window.WindowState = WindowState.Normal;
    }

    public void ShowAtPointer()
    {
        if (GetCursorPos(out var pointer)) Window.Position = new PixelPoint(pointer.X, pointer.Y);
        Window.Show();
        Window.Activate();
    }

    /// <summary>The Options and Configure panels are Nexaflow.Core's view-models over the session; this shell has none to open.</summary>
    public void OpenOptionsAt(string configName) =>
        ShowNotification("Options has no Avalonia panel yet: the settings panels are the WPF app's.");

    public void OpenConfigureAt(Workspace? workspace, string configName) =>
        ShowNotification($"Configure ({workspace?.Name ?? CurrentRuntime.Name}) has no Avalonia panel yet.");

    public void ShowOverlay(object overlayViewModel) => Overlay = _views.Draw(overlayViewModel);

    public void CloseOverlay() => Overlay = null;

    public WindowPlacement? IntendedPlacement
    {
        get
        {
            if (Window.Screens.ScreenFromWindow(Window) is not { } screen) return null;
            var size = PixelSize.FromSize(Window.ClientSize, Window.DesktopScaling);
            var normal = new SessionRect(Window.Position.X, Window.Position.Y, Window.Position.X + size.Width, Window.Position.Y + size.Height);
            return new WindowPlacement(normal, Window.WindowState == WindowState.Maximized,
                                       new MonitorArea(screen.DisplayName ?? "", Rect(screen.Bounds), Rect(screen.WorkingArea), screen.IsPrimary));
        }
    }

    public void OpenAt(WindowPlacement placement)
    {
        Window.Position = new PixelPoint(placement.Normal.Left, placement.Normal.Top);
        Window.Width    = placement.Normal.Width / Window.DesktopScaling;
        Window.Height   = placement.Normal.Height / Window.DesktopScaling;
        if (placement.Maximized) Window.WindowState = WindowState.Maximized;
    }

    /// <summary>The window has moved to <paramref name="to"/>'s runtime: the tabs it had are closed (the session closed their pages),
    /// and it shows that workspace from now.</summary>
    public void Rejoin(WorkspaceRuntime from, WorkspaceRuntime to)
    {
        foreach (var tab in OpenTabs) tab.Detach();
        OpenTabs.Clear();
        SelectedTab = null;
        AttachRuntime(from, to);
    }

    public void AddTab(Page tab)
    {
        var shown = new ShellTab(tab, _views.ViewOf(tab), activating => SelectedTab = activating,
                                 closing => CurrentRuntime.ShellServices.CloseTab(closing.Page));
        OpenTabs.Add(shown);
        SelectedTab = shown;
    }

    /// <summary>The window has one pane, so a page meant for the second opens in it.</summary>
    public void FocusSecondPane() { }

    public void RemoveTab(Page tab)
    {
        if (TabOf(tab) is not { } shown) return;
        var at = OpenTabs.IndexOf(shown);
        shown.Detach();
        OpenTabs.Remove(shown);
        if (ReferenceEquals(SelectedTab, shown)) SelectedTab = OpenTabs.Count == 0 ? null : OpenTabs[Math.Min(at, OpenTabs.Count - 1)];
    }

    public void BringToFront(Page tab) => SelectedTab = TabOf(tab) ?? SelectedTab;

    public void SetActiveTab(Page tab) => SelectedTab = TabOf(tab) ?? SelectedTab;

    public void ShowError(string message) => _ui.Run(() => Toast("⚠ " + message, ""));

    public void ShowNotification(string message) => _ui.Run(() => Toast(message, ""));

    public void ShowConfirmation(string title, string prompt, Action onConfirm, Action? onCancel = null, string? confirmLabel = null,
                                 string? cancelLabel = null)
        => Prompt = new ShellPrompt(title, prompt, asksValue: false, "", confirmLabel ?? Str.Get("Shell.Prompt.Ok"),
                                    cancelLabel ?? Str.Get("Shell.Prompt.Cancel"), _ => onConfirm(), onCancel, Answered);

    public void ShowPrompt(string title, string label, string initialValue, Action<string> onConfirm, Action? onCancel = null)
        => Prompt = new ShellPrompt(title, label, asksValue: true, initialValue, Str.Get("Shell.Prompt.Ok"), Str.Get("Shell.Prompt.Cancel"),
                                    onConfirm, onCancel, Answered);

    public void AddRibbonPin(RibbonPinRequest request) => CurrentRuntime.Workspace.Ribbon?.PinDropped(request, CurrentRuntime);

    public void InsertChatInput(string text) => AiInputText += text;

    public IReadOnlyList<QuickOpenTarget> GetRibbonQuickOpenTargets()
        => [.. Ribbon.OfType<RibbonButton>().Select(button => new QuickOpenTarget(button.Label, button.Open))];

    public void SubmitAiQuery(string query)
    {
        AiInputText = query;
        SendCommand.Execute(null);
    }

    public Task ContinueAfterBackgroundJobAsync(IPageViewModel context, string about, string outcome)
        => _requests.PickUpAsync(context, about, outcome);

    // ── IAssistantBar ─────────────────────────────────────────────────────

    IAIResponseHandler IAssistantBar.Surface => Bar;

    AgentRunOptions IAssistantBar.RunOptions() => Bar.Conversation.Run(BarStyle, keep =>
    {
        if (_ui.IsCurrent) keep();
        else _ui.Post(keep);
    });

    Task IAssistantBar.PrefillAsync(string text)
    {
        AiInputText = text;
        return Task.CompletedTask;
    }

    void IAssistantBar.Touched(IReadOnlyList<string> paths) => Bar.Conversation.Touched(paths);

    // ── The runtime the window shows ──────────────────────────────────────

    private void AttachRuntime(WorkspaceRuntime? from, WorkspaceRuntime to)
    {
        if (from?.Workspace.Ribbon is { } left) left.Changed -= OnRibbonChanged;
        CurrentRuntime = to;
        if (to.Workspace.Ribbon is { } ribbon) ribbon.Changed += OnRibbonChanged;

        Title          = $"NexaFlow - {to.Name}";
        WorkspaceBrush = Color.TryParse(to.Color, out var colour) ? new SolidColorBrush(colour) : Brushes.Transparent;
        WorkspaceIcon  = to.Icon;
        WorkspaceTip   = $"{to.Name} Workspace";
        RebuildRibbon();
    }

    private void OnRibbonChanged(object? by) => _ui.Post(RebuildRibbon);

    /// <summary>The workspace's ribbon: each button, and each of a half group's, in order, with the separators between groups.</summary>
    private void RebuildRibbon()
    {
        Ribbon.Clear();
        if (CurrentRuntime.Workspace.Ribbon is not { } ribbon) return;
        foreach (var entry in ribbon.Entries.SelectMany(entry => entry.Kind == RibbonItemKind.HalfGroup ? entry.HalfItems ?? [] : [entry]))
        {
            if (entry.Kind == RibbonItemKind.Separator)
            {
                Ribbon.Add(new RibbonSeparator());
                continue;
            }
            if (entry.PageKind is not { } kind) continue;
            var pageParams = entry.PageParams;
            Ribbon.Add(new RibbonButton(entry.Label, entry.Icon,
                () => CurrentRuntime.ShellServices.OpenTab(kind, pageParams is null ? null : new(pageParams))));
        }
    }

    /// <summary>Follows the breadcrumb trail of <paramref name="page"/>, the one in front.</summary>
    private void FollowCrumbs(Page? page)
    {
        if (_crumbsOf is not null) _crumbsOf.Breadcrumbs.CollectionChanged -= OnCrumbsChanged;
        _crumbsOf = page;
        if (page is not null) page.Breadcrumbs.CollectionChanged += OnCrumbsChanged;
        RebuildCrumbs();
    }

    private void OnCrumbsChanged(object? sender, NotifyCollectionChangedEventArgs e) => _ui.Post(RebuildCrumbs);

    private void RebuildCrumbs()
    {
        Crumbs.Clear();
        if (_crumbsOf is not { } page) return;
        for (var i = 0; i < page.Breadcrumbs.Count; i++) Crumbs.Add(new Crumb(page.Breadcrumbs[i], i == page.Breadcrumbs.Count - 1));
    }

    private void OnBarChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BarSurface.IsOpen)) OnPropertyChanged(nameof(ShowsAiLine));
    }

    private void OnUnreadChanged(object? sender, EventArgs e) => _ui.Post(() => UnreadCount = _session.Inbox.UnreadCount);

    private void OnMessagePosted(object? sender, NotificationItem message) => _ui.Post(() =>
    {
        if (_session.Inbox.ClaimToast(message)) Toast(message.Title, message.Body);
    });

    /// <summary>Shows a toast at the window's foot, which fades after a while.</summary>
    private void Toast(string title, string body)
    {
        ToastTitle   = title;
        ToastBody    = body;
        IsToastShown = true;
        _toastTimer.Restart();
    }

    /// <summary>The status dot shows the most pressing of: a run under way, a handler owning the typed words, the assistant
    /// listening for them, or ready.</summary>
    private void ShowDot()
    {
        var preview = _preview.Current;
        DotBusy      = AiIsBusy;
        DotHandler   = !AiIsBusy && preview.Symbol is not null;
        DotListening = !AiIsBusy && preview.Symbol is null && preview.Listening;
        DotSymbol    = preview.Symbol ?? "";
        DotTip       = DotBusy ? "Busy" : DotHandler ? preview.Symbol! : DotListening ? "Listening" : "Ready";
        Completion   = preview.Completion ?? "";
    }

    private void Answered(ShellPrompt answered)
    {
        if (ReferenceEquals(Prompt, answered)) Prompt = null;
    }

    private ShellTab? TabOf(Page page) => OpenTabs.FirstOrDefault(tab => ReferenceEquals(tab.Page, page));

    private static SessionRect Rect(Avalonia.PixelRect rect) => new(rect.X, rect.Y, rect.Right, rect.Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);
}

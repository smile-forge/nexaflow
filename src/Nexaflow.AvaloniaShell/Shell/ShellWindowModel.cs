using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.AvaloniaShell.Views;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Icons;
using SessionRect = Nexaflow.Session.Models.PixelRect;

namespace Nexaflow.AvaloniaShell.Shell;

/// <summary>
/// One shell window, as the session sees it (<see cref="IWindowHost"/>) and as its view binds it: the workspace it shows and the
/// selector that moves it to another (<see cref="WorkspaceWindows.Move"/>), that workspace's ribbon, the session's inbox, the open
/// tabs, and the AI bar (<see cref="AssistantRequests"/>, <see cref="TypedQueryPreview"/>). It supports its own view, as
/// Nexaflow.Core's <c>ShellViewModel</c> supports the WPF window; the session is what the two share.
/// </summary>
internal sealed partial class ShellWindowModel : ObservableObject, IWindowHost, IAssistantBar
{
    /// <summary>How a reply in the bar is written. Nexaflow.Core keeps the same words in its <c>ShellAIResponseHandler</c>.</summary>
    private const string BarStyle =
        "Your reply is shown on one line of the AI bar, the whole of it a hover away: lead with the answer in a sentence or " +
        "two, and write more only when the request needs it.";

    private readonly WindowServices _session;
    private readonly AvaloniaPageViews _views;
    private readonly IUiThread _ui;
    private readonly AssistantRequests _requests;
    private readonly TypedQueryPreview _preview;

    /// <summary>The selector follows a move the session made, rather than asking for one.</summary>
    private bool _following;

    public ShellWindowModel(ShellWindow window, WindowServices session, AvaloniaPageViews views, IUiThread ui, WorkspaceRuntime runtime)
    {
        Window   = window;
        _session = session;
        _views   = views;
        _ui      = ui;
        CurrentRuntime = runtime;
        Bar = new BarSurface(ui);

        _requests = new AssistantRequests(() => CurrentRuntime.AiService, this);
        _requests.BusyChanged += (_, _) => AiIsBusy = _requests.IsBusy;
        _requests.Failed      += ex => ShowError(ex.Message);
        _requests.Cancelled   += text => AiInputText = text;
        _preview = new TypedQueryPreview(ui, () => CurrentRuntime.AiService, () => ActiveTab?.ViewModel);
        _preview.Changed += (_, _) => OnPreviewChanged();

        session.Inbox.UnreadChanged += OnUnreadChanged;
        session.Inbox.MessagePosted += OnMessagePosted;
        UnreadCount = session.Inbox.UnreadCount;
        foreach (var toast in session.Inbox.ClaimPendingToasts()) Status = toast.Title;

        AttachRuntime(null, runtime);
    }

    /// <summary>The window this model is the session's handle on.</summary>
    public ShellWindow Window { get; }

    /// <summary>The runtime of the workspace the window shows now; a move changes it.</summary>
    public WorkspaceRuntime CurrentRuntime { get; private set; }

    /// <summary>Where the bar shows its replies.</summary>
    public BarSurface Bar { get; }

    /// <summary>The saved workspaces, which the selector lists.</summary>
    public ObservableCollection<Workspace> Workspaces => _session.SavedWorkspaces.Workspaces;

    /// <summary>The session's messages, newest first.</summary>
    public ObservableCollection<NotificationItem> Messages => _session.Inbox.Messages;

    public ObservableCollection<RibbonButton> Ribbon { get; } = [];

    public ObservableCollection<ShellTab> OpenTabs { get; } = [];

    [ObservableProperty] private Workspace? _selectedWorkspace;
    [ObservableProperty] private ShellTab? _selectedTab;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _status = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnread))]
    private int _unreadCount;

    public bool HasUnread => UnreadCount > 0;

    [ObservableProperty] private bool _isInboxOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOverlay))]
    private Control? _overlay;

    public bool HasOverlay => Overlay is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrompt))]
    private ShellPrompt? _prompt;

    public bool HasPrompt => Prompt is not null;

    [ObservableProperty] private string _aiInputText = "";
    [ObservableProperty] private bool _aiIsBusy;
    [ObservableProperty] private bool _includeContext = true;

    /// <summary>What the words typed so far point at: the query handler that owns them, or the assistant, and its completion.</summary>
    [ObservableProperty] private string _previewLine = "";

    // ── The window's own commands ─────────────────────────────────────────

    partial void OnSelectedWorkspaceChanged(Workspace? value)
    {
        if (_following || value is null || ReferenceEquals(value, CurrentRuntime.Workspace)) return;
        _session.WorkspaceWindows.Move(this, CurrentRuntime, value);
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
    private void ToggleInbox()
    {
        IsInboxOpen = !IsInboxOpen;
        if (IsInboxOpen) _session.Inbox.MarkAllRead();
    }

    /// <summary>A copy of the workspace the window shows, saved in the list, and the window moved to it.</summary>
    [RelayCommand]
    private void CopyWorkspace() => SelectedWorkspace = _session.WorkspaceWindows.CopyOf(CurrentRuntime.Workspace);

    [RelayCommand]
    private void DismissStatus() => Status = "";

    /// <summary>The window is closing: everything the session outlives lets go of it.</summary>
    public void Detach()
    {
        _session.Inbox.UnreadChanged -= OnUnreadChanged;
        _session.Inbox.MessagePosted -= OnMessagePosted;
        if (CurrentRuntime.Workspace.Ribbon is { } ribbon) ribbon.Changed -= OnRibbonChanged;
        _requests.Cancel();
        _preview.Dispose();
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
        ShowNotification($"Options ({configName}) has no Avalonia page: the settings panels are the WPF app's view-models.");

    public void OpenConfigureAt(Workspace? workspace, string configName) =>
        ShowNotification($"Configure ({workspace?.Name ?? CurrentRuntime.Name}, {configName}) has no Avalonia page.");

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
    /// and it shows that workspace's ribbon from now.</summary>
    public void Rejoin(WorkspaceRuntime from, WorkspaceRuntime to)
    {
        OpenTabs.Clear();
        SelectedTab = null;
        AttachRuntime(from, to);
    }

    public void AddTab(Page tab)
    {
        var shown = new ShellTab(tab, _views.ViewOf(tab), closing => CurrentRuntime.ShellServices.CloseTab(closing.Page));
        OpenTabs.Add(shown);
        SelectedTab = shown;
    }

    /// <summary>The window has one pane, so a page meant for the second opens in it.</summary>
    public void FocusSecondPane() { }

    public void RemoveTab(Page tab)
    {
        if (TabOf(tab) is { } shown) OpenTabs.Remove(shown);
    }

    public void BringToFront(Page tab) => SelectedTab = TabOf(tab) ?? SelectedTab;

    public void SetActiveTab(Page tab) => SelectedTab = TabOf(tab) ?? SelectedTab;

    public void ShowError(string message) => _ui.Run(() => Status = "⚠ " + message);

    public void ShowNotification(string message) => _ui.Run(() => Status = message);

    public void ShowConfirmation(string title, string prompt, Action onConfirm, Action? onCancel = null, string? confirmLabel = null,
                                 string? cancelLabel = null)
        => Prompt = new ShellPrompt(title, prompt, asksValue: false, "", confirmLabel ?? "OK", cancelLabel ?? "Cancel",
                                    _ => onConfirm(), onCancel, Answered);

    public void ShowPrompt(string title, string label, string initialValue, Action<string> onConfirm, Action? onCancel = null)
        => Prompt = new ShellPrompt(title, label, asksValue: true, initialValue, "OK", "Cancel", onConfirm, onCancel, Answered);

    public void AddRibbonPin(RibbonPinRequest request) => CurrentRuntime.Workspace.Ribbon?.PinDropped(request, CurrentRuntime);

    public void InsertChatInput(string text) => AiInputText += text;

    public IReadOnlyList<QuickOpenTarget> GetRibbonQuickOpenTargets() => [.. Ribbon.Select(button => new QuickOpenTarget(button.Label, button.Open))];

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

        _following = true;
        SelectedWorkspace = to.Workspace;
        _following = false;
        Title = $"Nexaflow on Avalonia - {to.Name}";
        RebuildRibbon();
    }

    private void OnRibbonChanged(object? by) => _ui.Post(RebuildRibbon);

    /// <summary>The workspace's ribbon as plain buttons: each button, and each of a half group's, in order; separators left out.</summary>
    private void RebuildRibbon()
    {
        Ribbon.Clear();
        if (CurrentRuntime.Workspace.Ribbon is not { } ribbon) return;
        foreach (var entry in ribbon.Entries.SelectMany(entry => entry.Kind == RibbonItemKind.HalfGroup ? entry.HalfItems ?? [] : [entry]))
        {
            if (entry.Kind != RibbonItemKind.Button || entry.PageKind is not { } kind) continue;
            var pageParams = entry.PageParams;
            Ribbon.Add(new RibbonButton(entry.Label, entry.Icon.Set == IconSet.Emoji ? entry.Icon.Value : "",
                () => CurrentRuntime.ShellServices.OpenTab(kind, pageParams is null ? null : new(pageParams))));
        }
    }

    private void OnUnreadChanged(object? sender, EventArgs e) => _ui.Post(() => UnreadCount = _session.Inbox.UnreadCount);

    private void OnMessagePosted(object? sender, NotificationItem message) => _ui.Post(() =>
    {
        if (_session.Inbox.ClaimToast(message)) Status = message.Title;
    });

    private void OnPreviewChanged()
    {
        var preview = _preview.Current;
        PreviewLine = preview switch
        {
            { Symbol: { } symbol, Completion: { } completion } => $"{symbol}  {completion}",
            { Symbol: { } symbol }                             => symbol,
            { Listening: true }                                => "AI",
            _                                                  => "",
        };
    }

    private void Answered(ShellPrompt answered)
    {
        if (ReferenceEquals(Prompt, answered)) Prompt = null;
    }

    private ShellTab? TabOf(Page page) => OpenTabs.FirstOrDefault(tab => ReferenceEquals(tab.Page, page));

    private static SessionRect Rect(Avalonia.PixelRect rect) => new(rect.X, rect.Y, rect.Right, rect.Bottom);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);
}

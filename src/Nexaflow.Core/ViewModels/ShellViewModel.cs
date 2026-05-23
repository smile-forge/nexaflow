using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Core.ViewModels;

public partial class ShellViewModel : ObservableObject, IWindowHost
{
    // ── IWindowHost ───────────────────────────────────────────────────────

    public Window Window { get; init; } = null!;

    bool IWindowHost.IsFocused
    {
        get => _isFocused;
        set => _isFocused = value;
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
    {
        Notifications.Insert(0, new NotificationItem { Title = "Info", Body = message });
        UnreadCount = Notifications.Count(n => !n.IsRead);
    }
    void IWindowHost.ShowConfirmation(string title, string prompt, Action onConfirm, Action? onCancel)
        => ShowConfirmation(title, prompt, onConfirm, onCancel);

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
        RootPane.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Pane.ActivePage))
            {
                if (_activeBreadcrumbSource is not null)
                    _activeBreadcrumbSource.CollectionChanged -= ActiveTabBreadcrumbsChanged;
                _activeBreadcrumbSource = RootPane.ActivePage?.Breadcrumbs;
                if (_activeBreadcrumbSource is not null)
                    _activeBreadcrumbSource.CollectionChanged += ActiveTabBreadcrumbsChanged;

                OnPropertyChanged(nameof(ActiveTab));
                OnPropertyChanged(nameof(CurrentPage));
                SyncBreadcrumbsFromActive();
            }
        };
    }

    private ObservableCollection<BreadcrumbSegment>? _activeBreadcrumbSource;

    private void ActiveTabBreadcrumbsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => SyncBreadcrumbsFromActive();

    private void SyncBreadcrumbsFromActive()
    {
        Breadcrumbs.Clear();
        if (RootPane.ActivePage is null) return;
        foreach (var seg in RootPane.ActivePage.Breadcrumbs)
            Breadcrumbs.Add(seg);
    }

    // ── Breadcrumbs ────────────────────────────────────────────────────────
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];

    // ── Notifications ─────────────────────────────────────────────────────
    public ObservableCollection<NotificationItem> Notifications { get; } = [];

    [ObservableProperty] private int  _unreadCount;
    [ObservableProperty] private bool _notificationsOpen;

    // ── Background activity ───────────────────────────────────────────────
    private readonly BackgroundActivityManager _activityManager;

    public ObservableCollection<BackgroundTask> BackgroundTasks => _activityManager.Tasks;

    // ── AI interaction ────────────────────────────────────────────────────
    [ObservableProperty] private string  _aiInputText      = string.Empty;
    [ObservableProperty] private bool    _aiIsBusy;
    [ObservableProperty] private bool    _voiceActive;
    [ObservableProperty] private string? _aiHandlerSymbol;
    [ObservableProperty] private bool    _aiIsListening;
    [ObservableProperty] private bool    _aiInputIsAiTyping;

    private CancellationTokenSource? _handlerEvalCts;

    // ── Error toast ───────────────────────────────────────────────────────
    [ObservableProperty] private string? _errorToast;
    private CancellationTokenSource? _errorToastCts;

    // ── Update toast ──────────────────────────────────────────────────────
    [ObservableProperty] private string? _updateToastVersion;
    [ObservableProperty] private string? _updateToastChangelog;
    private CancellationTokenSource? _updateToastCts;

    private void ShowError(string title, string message)
    {
        Notifications.Insert(0, new NotificationItem { Title = title, Body = message });
        UnreadCount = Notifications.Count(n => !n.IsRead);

        _errorToastCts?.Cancel();
        _errorToastCts = new CancellationTokenSource();
        ErrorToast = message;

        var token = _errorToastCts.Token;
        _ = Task.Delay(TimeSpan.FromSeconds(8), token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                Application.Current.Dispatcher.Invoke(() => ErrorToast = null);
        }, TaskScheduler.Default);
    }

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

    public void ShowUpdateToast(string version, string? changelog)
    {
        _updateToastCts?.Cancel();
        _updateToastCts = new CancellationTokenSource();
        UpdateToastVersion   = version;
        UpdateToastChangelog = changelog ?? string.Empty;

        var token = _updateToastCts.Token;
        _ = Task.Delay(TimeSpan.FromMinutes(5), token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                Application.Current.Dispatcher.Invoke(() => UpdateToastVersion = null);
        }, TaskScheduler.Default);
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
        CurrentWorkContext = ctx;
        // The RibbonControl observes CurrentWorkContext via its WorkContext DP
        // and handles save/load of items per-context. The shell no longer touches the ribbon.
    }

    // ── Options overlay ───────────────────────────────────────────────────
    [ObservableProperty] private bool _optionsOpen;

    // ── Manage AI overlay ─────────────────────────────────────────────────
    [ObservableProperty] private bool _manageAiOpen;

    private readonly ShellServices _shellServices;

    public ShellViewModel(BackgroundActivityManager activityManager,
                          WorkContext workContext,
                          ShellServices shellServices)
    {
        _activityManager = activityManager;
        _activityManager.IsActiveChanged += (_, active) =>
            Application.Current.Dispatcher.Invoke(() => AiIsBusy = active);
        _currentWorkContext = workContext;
        _shellServices = shellServices;

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

        SeedNotifications();
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
            _shellServices.OpenTab(item.PageKind, item.PageParams);
        else
            item.Command?.Execute(null);
    }

    [RelayCommand]
    private void OpenRibbonItemInNewWindow(RibbonItem item)
    {
        if (item.PageKind is null) return;
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
    private void DismissNotification(NotificationItem item) => Notifications.Remove(item);

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

    [RelayCommand]
    private void ClearErrorToast()
    {
        _errorToastCts?.Cancel();
        ErrorToast = null;
    }

    [RelayCommand]
    private void DismissUpdateToast()
    {
        _updateToastCts?.Cancel();
        UpdateToastVersion = null;
    }

    [RelayCommand]
    private async Task AcceptUpdate()
    {
        _updateToastCts?.Cancel();
        UpdateToastVersion = null;
        await ((App)Application.Current).DownloadAndInstallUpdate();
    }

    // ── AI ────────────────────────────────────────────────────────────────

    partial void OnAiInputTextChanged(string value)
    {
        _handlerEvalCts?.Cancel();
        if (string.IsNullOrWhiteSpace(value))
        {
            AiHandlerSymbol = null;
            AiIsListening   = false;
            return;
        }

        _handlerEvalCts = new CancellationTokenSource();
        var cts = _handlerEvalCts;
        _ = Task.Delay(150, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Application.Current.Dispatcher.Invoke(() => EvaluateHandlers(value));
        }, TaskScheduler.Default);
    }

    private void EvaluateHandlers(string text)
    {
        var pageVm   = (CurrentPage as IPageView)?.ViewModel;
        var handlers = FeatureManager.Instance.QueryHandlers;

        var symbolMatch = handlers.FirstOrDefault(
            h => h.Symbol is { Length: 1 } s && text.StartsWith(s));
        if (symbolMatch?.Symbol is not null)
        {
            AiHandlerSymbol = symbolMatch.Symbol;
            AiIsListening   = false;
            return;
        }

        var matches = handlers.Where(h => h.CanProcess(text, pageVm) > 0).ToList();
        if (matches.Count == 1 && matches[0].Symbol is not null)
        {
            AiHandlerSymbol = matches[0].Symbol;
            AiIsListening   = false;
        }
        else
        {
            AiHandlerSymbol = null;
            AiIsListening   = true;
        }
    }

    [RelayCommand]
    private async Task SendAiMessage()
    {
        var text = AiInputText.Trim();
        if (string.IsNullOrEmpty(text)) return;

        AiInputText     = string.Empty;
        AiHandlerSymbol = null;
        AiIsListening   = false;

        var page     = CurrentPage as IPageView;
        var pageVm   = page?.ViewModel;
        var handlers = FeatureManager.Instance.QueryHandlers.ToList();

        // 1. Symbol prefix → explicit handler selection; strip prefix from input
        IQueryHandler? selected = null;
        // 1. Symbol prefix → filter handlers by symbol; strip prefix from input
        var symbolHandlers = handlers
            .Where(h => h.Symbol is { Length: 1 } s && text.StartsWith(s))
            .ToList();
        if (symbolHandlers.Count > 0)
        {
            text = text[1..].TrimStart();
            handlers = symbolHandlers;
        }

        // 2. Score candidates in the (possibly reduced) handler list
        var candidates = handlers.Where(h => h.CanProcess(text, pageVm) > 0).ToList();

        if (candidates.Count == 1)
        {
            selected = candidates[0];
        }
        else if (candidates.Count > 1)
        {
            try
            {
                if (CurrentWorkContext.AiService is { } svc)
                    selected = await svc.DisambiguateToolSelection(pageVm, text, candidates);
            }
            catch (Exception ex)
            {
                ShowError("AI error", ex.Message);
                return;
            }
        }

        // 3. A handler was identified — run it
        if (selected is not null)
        {
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
                OpenAiChatTab(text, result);
            return;
        }

        // 4. No handler → contextual LLM call
        AiResponse? response = null;
        try
        {
            if (CurrentWorkContext.AiService is { } svc)
                response = await svc.ContextChat(pageVm, text);
        }
        catch (Exception ex)
        {
            ShowError("AI error", ex.Message);
            return;
        }

        if (response is null) return;

        switch (response.Kind)
        {
            case AiResponseKind.Action:
                if (pageVm is not null && response.Action is not null)
                    pageVm.Execute(response.Action);
                else
                    ShowError("Action failed", "No active page to execute action on.");
                break;

            case AiResponseKind.Prefill:
                await AnimatePrefillAsync(response.Text!);
                break;

            case AiResponseKind.Message:
                OpenAiChatTab(text, response.Text!);
                break;
        }
    }

    private void OpenAiChatTab(string input, string response)
    {
        var params_ = new Dictionary<string, string>
            { ["input"] = input, ["output"] = response };
        _shellServices.OpenTab("AIChat", params_);
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

    [RelayCommand]
    private void ToggleVoice() => VoiceActive = !VoiceActive;

    [RelayCommand]
    private void AttachFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
        if (dlg.ShowDialog() == true)
        {
            // TODO: surface attached files into the active chat
        }
    }

    // ── Background tasks ──────────────────────────────────────────────────

    public void AddBackgroundTask(BackgroundTask task) => _activityManager.AddTask(task);

    private void SeedNotifications()
    {
        Notifications.Add(new NotificationItem { Title = "Report ready",   Body = "Monthly revenue report has been generated." });
        Notifications.Add(new NotificationItem { Title = "New assignment", Body = "Case #4821 assigned to you." });
        Notifications.Add(new NotificationItem { Title = "SLA warning",   Body = "Case #4799 approaching SLA deadline." });
        UnreadCount = Notifications.Count;
    }
}

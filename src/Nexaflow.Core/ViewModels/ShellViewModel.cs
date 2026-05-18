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

    IReadOnlyList<TabEntry> IWindowHost.Tabs => Tabs;

    void IWindowHost.AddTab(TabEntry tab)
    {
        foreach (var t in Tabs) t.IsActive = false;
        tab.IsActive = true;
        Tabs.Insert(0, tab);
        ActiveTab   = tab;
        CurrentPage = tab.GetOrCreatePage();
        RefreshBreadcrumbs(tab);
    }

    void IWindowHost.RemoveTab(TabEntry tab)
    {
        int idx = Tabs.IndexOf(tab);
        if (idx < 0) return;
        Tabs.Remove(tab);

        if (tab.IsActive && Tabs.Count > 0)
        {
            int next = Math.Min(idx, Tabs.Count - 1);
            ((IWindowHost)this).SetActiveTab(Tabs[next]);
        }
        else if (Tabs.Count == 0)
        {
            ActiveTab   = null;
            CurrentPage = null;
            Breadcrumbs.Clear();
        }
    }

    void IWindowHost.BringToFront(TabEntry tab)
    {
        int idx = Tabs.IndexOf(tab);
        if (idx > 0) Tabs.Move(idx, 0);
    }

    void IWindowHost.SetActiveTab(TabEntry tab)
    {
        foreach (var t in Tabs) t.IsActive = false;
        tab.IsActive = true;
        ActiveTab   = tab;
        CurrentPage = tab.GetOrCreatePage();
        RefreshBreadcrumbs(tab);
    }

    void IWindowHost.RefreshBreadcrumbs(TabEntry tab) => RefreshBreadcrumbs(tab);

    void IWindowHost.ShowError(string message) => ShowError("Error", message);
    void IWindowHost.ShowNotification(string message)
    {
        Notifications.Insert(0, new NotificationItem { Title = "Info", Body = message });
        UnreadCount = Notifications.Count(n => !n.IsRead);
    }

    private void RefreshBreadcrumbs(TabEntry tab)
    {
        if (tab != ActiveTab) return;
        Breadcrumbs.Clear();
        foreach (var seg in tab.Breadcrumbs)
            Breadcrumbs.Add(seg);
    }

    // ── Tab strip ──────────────────────────────────────────────────────────
    public ObservableCollection<TabEntry> Tabs { get; } = [];

    [ObservableProperty] private TabEntry? _activeTab;

    [ObservableProperty] private UserControl? _currentPage;

    // ── Breadcrumbs ────────────────────────────────────────────────────────
    public ObservableCollection<BreadcrumbSegment> Breadcrumbs { get; } = [];

    // ── Ribbon ────────────────────────────────────────────────────────────
    public ObservableCollection<RibbonItem> RibbonItems { get; } = [];

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

    // ── Options overlay ───────────────────────────────────────────────────
    [ObservableProperty] private bool _optionsOpen;

    // ── Ribbon edit mode ─────────────────────────────────────────────────
    [ObservableProperty] private bool _ribbonEditOpen;

    private readonly IAIService     _aiService;
    private readonly IShellServices _shellServices;

    public ShellViewModel(BackgroundActivityManager activityManager,
                          IAIService aiService,
                          IShellServices shellServices)
    {
        _activityManager = activityManager;
        _activityManager.IsActiveChanged += (_, active) =>
            Application.Current.Dispatcher.Invoke(() => AiIsBusy = active);
        _aiService     = aiService;
        _shellServices = shellServices;
        LoadOrBuildRibbon();
        RibbonItems.CollectionChanged += (_, e) =>
        {
            if (RibbonItems.Count == 0)
            {
                if (!RibbonEditOpen)
                    Application.Current.Dispatcher.BeginInvoke(() => BuildDefaultRibbon());
            }
            else
            {
                SaveRibbonLayout();
            }
        };
        SeedNotifications();
    }

    // ── Tab commands ──────────────────────────────────────────────────────

    [RelayCommand]
    private void ActivateTab(TabEntry tab)
    {
        if (tab.IsActive && tab == ActiveTab)
        {
            (CurrentPage as IPageView)?.Reinitialize(tab.PageParams ?? []);
            return;
        }

        ((IWindowHost)this).BringToFront(tab);
        ((IWindowHost)this).SetActiveTab(tab);
    }

    [RelayCommand]
    private void CloseTab(TabEntry tab) => _shellServices.CloseTab(tab);

    // ── Ribbon ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void RibbonAction(RibbonItem item)
    {
        if (item.PageKind is not null)
            _shellServices.OpenTab(item.PageKind, item.PageParams);
        else
            item.Command?.Execute(null);
    }

    [RelayCommand]
    private void ToggleRibbonEdit() => RibbonEditOpen = !RibbonEditOpen;

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
        var symbolMatch = handlers.FirstOrDefault(
            h => h.Symbol is { Length: 1 } s && text.StartsWith(s));
        if (symbolMatch is not null)
        {
            selected = symbolMatch;
            text = text[1..].TrimStart();
        }

        // 2. Score candidates when no symbol prefix was used
        if (selected is null)
        {
            var candidates = handlers.Where(h => h.CanProcess(text, pageVm) > 0).ToList();

            if (candidates.Count == 1)
            {
                selected = candidates[0];
            }
            else if (candidates.Count > 1)
            {
                try
                {
                    selected = await _aiService.DisambiguateToolSelection(pageVm, text, candidates);
                }
                catch (Exception ex)
                {
                    ShowError("AI error", ex.Message);
                    return;
                }
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
        AiResponse? response;
        try
        {
            response = await _aiService.ContextChat(pageVm, text);
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

    // ── FileSystem tab pinning ────────────────────────────────────────────

    [RelayCommand]
    private void PinTabToRibbon(TabPinRequest request)
    {
        var (tab, insertIndex) = request;
        if (tab.Page is not Views.FileSystemView fsPage) return;

        var vm       = fsPage.ViewModel;
        var path     = vm.CurrentPath;
        var isThisPc = string.IsNullOrEmpty(path) || path == "This PC";

        var label = tab.Title;
        var icon  = isThisPc ? "🖥" : "📁";

        if (RibbonItems.Any(r => r.Label == label && r.Kind == RibbonItemKind.Button))
            return;

        if (!isThisPc)
            vm.ResetRootToCurrentPath();

        var item = MakeButton(
            label, icon,
            pageKind:   PageKinds.FileSystem,
            pageParams: isThisPc
                ? new() { ["mode"] = "thispc" }
                : new() { ["mode"] = "path", ["path"] = path });

        AddRibbonItem(item, insertIndex);
    }

    // ── Ribbon persistence ────────────────────────────────────────────────

    public void SaveRibbonLayout() => RibbonLayoutService.Save(RibbonItems);

    private void LoadOrBuildRibbon()
    {
        var saved = RibbonLayoutService.Load();
        if (saved is { Count: > 0 })
        {
            foreach (var item in saved)
            {
                item.PropertyChanged += (_, _) => SaveRibbonLayout();
                RibbonItems.Add(item);
            }
        }
        else
        {
            BuildDefaultRibbon();
        }
    }

    private RibbonItem MakeButton(string label, string icon, string pageKind,
                                   Dictionary<string, string>? pageParams = null)
    {
        return new RibbonItem
        {
            Kind       = RibbonItemKind.Button,
            Label      = label,
            Icon       = icon,
            PageKind   = pageKind,
            PageParams = pageParams
        };
    }

    private void AddRibbonItem(RibbonItem item, int insertAt = -1)
    {
        item.PropertyChanged += (_, _) => SaveRibbonLayout();
        if (insertAt >= 0 && insertAt < RibbonItems.Count)
            RibbonItems.Insert(insertAt, item);
        else
            RibbonItems.Add(item);
    }

    private void BuildDefaultRibbon()
    {
        foreach (var item in BuildDefaultItems())
            AddRibbonItem(item);
    }

    public IList<RibbonItem> BuildDefaultItems()
    {
        return
        [
            MakeButton("Projects", "🗂", "Projects"),
            new RibbonItem { Kind = RibbonItemKind.Separator },
            MakeButton("This PC", "🖥", PageKinds.FileSystem, new() { ["mode"] = "thispc" }),
            MakeButton("AI Chat", "💬", PageKinds.AiChat),
            MakeButton("Console", "⌨", "Console"),
            MakeButton("Scratchpad", "📌", "Scratchpad"),
            new RibbonItem { Kind = RibbonItemKind.Separator },
            MakeButton("Documents", "📄", PageKinds.FileSystem,
                new() { ["mode"] = "path", ["path"] = KnownFolderService.DocumentsPath }),
            MakeButton("Pictures", "🖼", PageKinds.FileSystem,
                new() { ["mode"] = "path", ["path"] = KnownFolderService.PicturesPath }),
            MakeButton("Videos", "🎬", PageKinds.FileSystem,
                new() { ["mode"] = "path", ["path"] = KnownFolderService.VideosPath }),
            MakeButton("Music", "🎵", PageKinds.FileSystem,
                new() { ["mode"] = "path", ["path"] = KnownFolderService.MusicPath }),
        ];
    }

    private void SeedNotifications()
    {
        Notifications.Add(new NotificationItem { Title = "Report ready",   Body = "Monthly revenue report has been generated." });
        Notifications.Add(new NotificationItem { Title = "New assignment", Body = "Case #4821 assigned to you." });
        Notifications.Add(new NotificationItem { Title = "SLA warning",   Body = "Case #4799 approaching SLA deadline." });
        UnreadCount = Notifications.Count;
    }
}

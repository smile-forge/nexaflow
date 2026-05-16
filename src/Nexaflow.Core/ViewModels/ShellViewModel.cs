using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.FileSystem;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.Views;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows;

namespace Nexaflow.Core.ViewModels;

public partial class ShellViewModel : ObservableObject
{
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

    /// <summary>Bound to the ActivityTicker in MainWindow.xaml.</summary>
    public ObservableCollection<BackgroundTask> BackgroundTasks => _activityManager.Tasks;

    // ── AI interaction ────────────────────────────────────────────────────
    [ObservableProperty] private string  _aiInputText      = string.Empty;
    [ObservableProperty] private bool    _aiIsBusy;
    [ObservableProperty] private bool    _voiceActive;
    [ObservableProperty] private string? _aiHandlerSymbol;    // bound to AiStatusDot.HandlerSymbol
    [ObservableProperty] private bool    _aiIsListening;      // bound to AiStatusDot.IsListening
    [ObservableProperty] private bool    _aiInputIsAiTyping;  // triggers TextBox colour change

    private CancellationTokenSource? _handlerEvalCts;

    // ── Error toast ───────────────────────────────────────────────────────
    [ObservableProperty] private string? _errorToast;

    private CancellationTokenSource? _errorToastCts;

    // ── Update toast ──────────────────────────────────────────────────────
    [ObservableProperty] private string? _updateToastVersion;
    [ObservableProperty] private string? _updateToastChangelog;

    private CancellationTokenSource? _updateToastCts;

    /// <summary>
    /// Displays <paramref name="message"/> in the error toast for a few seconds,
    /// then also adds it as a persistent notification.
    /// </summary>
    private void ShowError(string title, string message)
    {
        // Add to persistent notifications panel
        Notifications.Insert(0, new NotificationItem { Title = title, Body = message });
        UnreadCount = Notifications.Count(n => !n.IsRead);

        // Show transient toast — cancel any in-flight dismiss timer first
        _errorToastCts?.Cancel();
        _errorToastCts = new CancellationTokenSource();
        ErrorToast = message;

        var token = _errorToastCts.Token;
        _ = Task.Delay(TimeSpan.FromSeconds(8), token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                System.Windows.Application.Current.Dispatcher.Invoke(() => ErrorToast = null);
        }, TaskScheduler.Default);
    }

    /// <summary>Called by MainWindow when the OptionsViewModel raises a SaveError event.</summary>
    public void ShowErrorToast(string message) => ShowError("Settings", message);

    public void ShowUpdateToast(string version, string? changelog)
    {
        _updateToastCts?.Cancel();
        _updateToastCts = new CancellationTokenSource();
        UpdateToastVersion  = version;
        UpdateToastChangelog = changelog ?? string.Empty;

        var token = _updateToastCts.Token;
        _ = Task.Delay(TimeSpan.FromMinutes(5), token).ContinueWith(t =>
        {
            if (!t.IsCanceled)
                Application.Current.Dispatcher.Invoke(() => UpdateToastVersion = null);
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Closes any open tabs whose PageKind matches one of <paramref name="pageKinds"/>
    /// and immediately reopens them so they pick up the updated config.
    /// </summary>
    public void RefreshTabs(IEnumerable<string> pageKinds)
    {
        var kinds = pageKinds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var toRefresh = Tabs.Where(t => kinds.Contains(t.PageKind ?? string.Empty)).ToList();
        foreach (var tab in toRefresh)
        {
            var pageKind = tab.PageKind;
            var pageParams = tab.PageParams;
            CloseTab(tab);
            if (!string.IsNullOrEmpty(pageKind))
                OpenTabForPageKind(pageKind, pageParams);
        }
    }

    // ── Options overlay ───────────────────────────────────────────────────
    [ObservableProperty] private bool _optionsOpen;

    // ── Ribbon edit mode ─────────────────────────────────────────────────
    [ObservableProperty] private bool _ribbonEditOpen;

    private readonly IAIService _aiService;

    public ShellViewModel(BackgroundActivityManager activityManager, IAIService aiService)
    {
        _activityManager = activityManager;
        _activityManager.IsActiveChanged += (_, active) =>
            Application.Current.Dispatcher.Invoke(() => AiIsBusy = active);
        _aiService = aiService;
        FeatureManager.Instance.TabOpenRequested += OnFeatureTabOpenRequested;
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

    private void OnFeatureTabOpenRequested(string pageKind, Dictionary<string, string>? pageParams)
        => Application.Current.Dispatcher.Invoke(() => OpenTabForPageKind(pageKind, pageParams));

    // ── Tab management ────────────────────────────────────────────────────

    /// <summary>
    /// Opens a new tab (prepended to list) or re-activates an existing one.
    /// </summary>
    public void OpenTab(TabEntry tab)
    {
        // Re-activate if already present
        var existing = Tabs.FirstOrDefault(t => t.Title == tab.Title);
        if (existing is not null)
        {
            ActivateTab(existing);
            return;
        }

        // Deactivate all, insert at front
        foreach (var t in Tabs) t.IsActive = false;
        tab.IsActive = true;
        Tabs.Insert(0, tab);
        ActiveTab = tab;
        CurrentPage = tab.GetOrCreatePage();
        UpdateBreadcrumbs(tab);
    }

    /// <summary>
    /// Called by the BreadcrumbBar when a segment with a <see cref="PageKinds"/> target is clicked.
    /// Resolves the correct tab factory for the page kind and opens or focuses the tab.
    /// </summary>
    public void OpenTabForPageKind(string pageKind, Dictionary<string, string>? pageParams)
    {
        // Check if a matching tab is already open first
        var existing = Tabs.FirstOrDefault(t =>
        {
            var ribbon = RibbonItems.FirstOrDefault(r => r.Label == t.Title);
            return ribbon?.PageKind == pageKind;
        });
        if (existing is not null) { ActivateTab(existing); return; }

        TabEntry? tab = pageKind switch
        {
            PageKinds.FileSystem   => MakeFileSystemTabFactory(
                                         pageParams?.GetValueOrDefault("label") ?? "Files",
                                         "📁",
                                         pageParams)(),
            PageKinds.Placeholder  => MakePlaceholderTab(pageKind, "📄"),
            _ when FeatureManager.Instance.IsRegistered(pageKind)
                                   => FeatureManager.Instance.CreateTab(pageKind, pageParams),
            _                      => MakePlaceholderTab(pageKind, "📄")
        };

        if (tab is not null) OpenTab(tab);
    }

    [RelayCommand]
    private void ActivateTab(TabEntry tab)
    {
        // If the tab is already active, refresh its page content instead
        if (tab.IsActive && tab == ActiveTab)
        {
            if (CurrentPage is IRefreshable refreshable)
                refreshable.Refresh();
            return;
        }

        // Move to front if not already there
        int idx = Tabs.IndexOf(tab);
        if (idx > 0)
            Tabs.Move(idx, 0);

        foreach (var t in Tabs) t.IsActive = false;
        tab.IsActive = true;
        ActiveTab   = tab;
        CurrentPage = tab.GetOrCreatePage();
        UpdateBreadcrumbs(tab);
    }

    [RelayCommand]
    private void CloseTab(TabEntry tab)
    {
        int idx = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (tab.IsActive && Tabs.Count > 0)
        {
            // Activate the next tab (or previous if at end)
            int next = Math.Min(idx, Tabs.Count - 1);
            ActivateTab(Tabs[next]);
        }
        else if (Tabs.Count == 0)
        {
            ActiveTab   = null;
            CurrentPage = null;
            Breadcrumbs.Clear();
        }
    }

    /// <summary>
    /// Accepts a tab arriving from another window (tearoff or cross-window drag).
    /// </summary>
    public void ReceiveTab(TabEntry tab)
    {
        if (Tabs.Contains(tab)) { ActivateTab(tab); return; }

        foreach (var t in Tabs) t.IsActive = false;
        tab.IsActive = true;
        Tabs.Insert(0, tab);
        ActiveTab   = tab;
        CurrentPage = tab.GetOrCreatePage();
        UpdateBreadcrumbs(tab);
    }

    /// <summary>
    /// Removes a tab without closing the window — used when a tab moves to another window.
    /// </summary>
    public void RemoveTab(TabEntry tab)
    {
        int idx = Tabs.IndexOf(tab);
        if (idx < 0) return;
        Tabs.Remove(tab);

        if (tab.IsActive && Tabs.Count > 0)
        {
            int next = Math.Min(idx, Tabs.Count - 1);
            ActivateTab(Tabs[next]);
        }
        else if (Tabs.Count == 0)
        {
            ActiveTab   = null;
            CurrentPage = null;
            Breadcrumbs.Clear();
        }
    }

    private void UpdateBreadcrumbs(TabEntry tab)
    {
        Breadcrumbs.Clear();
        foreach (var seg in tab.Breadcrumbs)
            Breadcrumbs.Add(seg);
    }

    // ── Ribbon ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void RibbonAction(RibbonItem item)
    {
        if (item.TabFactory is not null)
            OpenTab(item.TabFactory());
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

    /// <summary>
    /// Called on every keystroke; debounces handler evaluation so CanProcess
    /// is not called on every single character.
    /// </summary>
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
        var page     = CurrentPage as IPageView;
        var handlers = FeatureManager.Instance.QueryHandlers;

        var symbolMatch = handlers.FirstOrDefault(
            h => h.Symbol is { Length: 1 } s && text.StartsWith(s));
        if (symbolMatch?.Symbol is not null)
        {
            AiHandlerSymbol = symbolMatch.Symbol;
            AiIsListening   = false;
            return;
        }

        var matches = handlers.Where(h => h.CanProcess(text, page) > 0).ToList();
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
            var candidates = handlers.Where(h => h.CanProcess(text, page) > 0).ToList();

            if (candidates.Count == 1)
            {
                selected = candidates[0];
            }
            else if (candidates.Count > 1)
            {
                try
                {
                    selected = await _aiService.DisambiguateToolSelection(page, text, candidates);
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
                result = await selected.ProcessAsync(text, page);
            }
            catch (Exception ex)
            {
                ShowError("AI error", ex.Message);
                return;
            }

            if (result is not null)
                await SendToAiChat(text, result);
            return;
        }

        // 4. No handler → contextual LLM call
        AiResponse? response;
        try
        {
            response = await _aiService.ContextChat(page, text);
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
                if (page is not null && response.Action is not null)
                    page.Execute(response.Action);
                else
                    ShowError("Action failed", "No active page to execute action on.");
                break;

            case AiResponseKind.Prefill:
                await AnimatePrefillAsync(response.Text!);
                break;

            case AiResponseKind.Message:
                await SendToAiChat(text, response.Text!);
                break;
        }
    }

    private async Task SendToAiChat(string input, string response)
    {
        var existing = Tabs.FirstOrDefault(t => t.PageKind == "AIChat");
        if (existing is not null)
        {
            ActivateTab(existing);
            (CurrentPage as IPageView)?.Reinitialize(
                new Dictionary<string, string> { ["input"] = input, ["output"] = response });
        }
        else
        {
            var tab = FeatureManager.Instance.CreateTab("AIChat",
                new Dictionary<string, string> { ["input"] = input, ["output"] = response });
            if (tab is not null) OpenTab(tab);
        }
    }

    private async Task AnimatePrefillAsync(string prefill)
    {
        AiInputIsAiTyping = true;
        AiInputText       = string.Empty;

        foreach (char c in prefill)
        {
            AiInputText += c;
            await Task.Delay(15); // ~65 chars/sec; runs on UI thread via captured SynchronizationContext
        }

        await Task.Delay(400); // brief pause so user sees the full suggestion
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

    /// <summary>Handles a #focustab instruction returned by the LLM provider.</summary>
    private void HandleFocusTabInstruction(FocusTabInstruction ft)
    {
        // Check if a tab with that name is already open
        var existing = Tabs.FirstOrDefault(t =>
            string.Equals(t.Title, ft.TabName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ActivateTab(existing);
            return;
        }

        // Map well-known core tab names; delegate everything else to FeatureManager
        TabEntry? tab = ft.TabName.ToLowerInvariant() switch
        {
            "filesystem" or "files" or "this pc" =>
                MakeFileSystemTabFactory(ft.TabName, "🖥", new() { ["mode"] = "thispc" })(),
            _ when FeatureManager.Instance.IsRegistered(ft.TabName)
                => FeatureManager.Instance.CreateTab(ft.TabName),
            _ => MakePlaceholderTab(ft.TabName, "📄")
        };

        if (tab is not null) OpenTab(tab);
    }

    // ── Background tasks ──────────────────────────────────────────────────

    /// <summary>Adds an externally-created background task to the activity ticker.</summary>
    public void AddBackgroundTask(BackgroundTask task) => _activityManager.AddTask(task);

    // ── FileSystem tab helpers ────────────────────────────────────────────

    /// <summary>
    /// Pins the active filesystem tab's current folder to the ribbon as a new button.
    /// </summary>
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

        // Don't add a duplicate
        if (RibbonItems.Any(r => r.Label == label && r.Kind == RibbonItemKind.Button))
            return;

        // Re-root the live tab's tree at the current folder so it reflects the pin point
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

    private Views.FileSystemView CreateFileSystemPage(FileSystemViewModel fsVm, TabEntry tab)
    {
        var keyHandler = new FileSystemKeyboardHandler(fsVm);
        var dropTarget = new FileSystemDropTarget(fsVm);
        var page = new Views.FileSystemView(fsVm, keyHandler, dropTarget);
        page.NavigationChanged += segments => ApplyFileSystemBreadcrumbs(tab, page, segments);
        fsVm.TabOpenRequested  += OpenTab;
        return page;
    }

    private bool _applyingBreadcrumbs;

    private void ApplyFileSystemBreadcrumbs(
        TabEntry tab,
        Views.FileSystemView page,
        IReadOnlyList<(string Label, string Path)> segments)
    {
        if (_applyingBreadcrumbs) return;
        _applyingBreadcrumbs = true;
        try
        {
            var crumbs = segments.Select((seg, i) =>
            {
                var capturedPath = seg.Path;
                var isLast = i == segments.Count - 1;
                return new BreadcrumbSegment
                {
                    Label    = seg.Label,
                    Navigate = isLast ? null : (string.IsNullOrEmpty(capturedPath)
                            ? () => page.ViewModel.GoToThisPc(rebuildTree: true)
                            : () => page.ViewModel.NavigateTo(capturedPath))
                };
            }).ToList();

            tab.Breadcrumbs = crumbs;

            // Update tab title to current folder name, truncated if long
            var currentLabel = segments[^1].Label;
            tab.Title = currentLabel.Length > 15
                ? currentLabel[..10] + "…"
                : currentLabel;

            if (tab == ActiveTab)
                UpdateBreadcrumbs(tab);
        }
        finally
        {
            _applyingBreadcrumbs = false;
        }
    }

    // ── Seed data ─────────────────────────────────────────────────────────

    // ── Ribbon persistence ────────────────────────────────────────────────

    public void SaveRibbonLayout() => RibbonLayoutService.Save(RibbonItems);

    private void LoadOrBuildRibbon()
    {
        var saved = RibbonLayoutService.Load();
        if (saved is { Count: > 0 })
        {
            foreach (var item in saved)
            {
                ReattachTabFactory(item);
                item.PropertyChanged += (_, _) => SaveRibbonLayout();
                RibbonItems.Add(item);
            }
        }
        else
        {
            BuildDefaultRibbon();
        }
    }

    /// <summary>
    /// Maps a persisted <see cref="RibbonItem.PageKind"/> back to a live
    /// <see cref="RibbonItem.TabFactory"/> delegate.  Core page kinds are
    /// handled inline; all feature-registered kinds are resolved via
    /// <see cref="FeatureManager"/>.
    /// </summary>
    public void ReattachTabFactory(RibbonItem item)
    {
        if (item.PageKind is null) { item.TabFactory = null; return; }

        item.TabFactory = item.PageKind switch
        {
            PageKinds.FileSystem => MakeFileSystemTabFactory(item.Label, item.Icon, item.PageParams),
            _ when FeatureManager.Instance.IsRegistered(item.PageKind)
                                 => () => FeatureManager.Instance.CreateTab(item.PageKind, item.PageParams)!,
            _                    => () => MakePlaceholderTab(item.Label, item.Icon)
        };
    }

    // ── Factory helpers (core page types) ────────────────────────────────

    private Func<TabEntry> MakeFileSystemTabFactory(
        string label, string icon, Dictionary<string, string>? p)
    {
        // Determine mode from params; default to "thispc" when absent
        var mode = p?.GetValueOrDefault("mode") ?? "thispc";
        return mode == "path" && p!.TryGetValue("path", out var path)
            ? () =>
            {
                var tab = new TabEntry { Title = label, Icon = icon,
                    Breadcrumbs = [new BreadcrumbSegment { Label = label }] };
                tab.PageFactory = () => CreateFileSystemPage(new FileSystemViewModel(path), tab);
                return tab;
            }
            : () =>
            {
                var tab = new TabEntry { Title = "This PC", Icon = "🖥",
                    Breadcrumbs = [new BreadcrumbSegment { Label = "This PC" }] };
                tab.PageFactory = () => CreateFileSystemPage(FileSystemViewModel.CreateThisPc(), tab);
                return tab;
            };
    }



    private static TabEntry MakePlaceholderTab(string title, string icon) => new()
    {
        Title       = title,
        Icon        = icon,
        Breadcrumbs = [new BreadcrumbSegment { Label = title }],
        PageFactory = () => new Views.PlaceholderPage()
    };

    /// <summary>
    /// Convenience: builds a <see cref="RibbonItem"/> with page-kind metadata
    /// and immediately attaches its runtime factory.
    /// </summary>
    private RibbonItem MakeButton(
        string label, string icon,
        string pageKind,
        Dictionary<string, string>? pageParams = null)
    {
        var item = new RibbonItem
        {
            Kind       = RibbonItemKind.Button,
            Label      = label,
            Icon       = icon,
            PageKind   = pageKind,
            PageParams = pageParams
        };
        ReattachTabFactory(item);
        return item;
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

    /// <summary>
    /// Returns a fresh list of default ribbon items with tab factories attached.
    /// Used by <see cref="Controls.RibbonEditor"/> for reset-to-defaults.
    /// </summary>
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

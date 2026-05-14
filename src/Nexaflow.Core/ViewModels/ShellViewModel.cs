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
    [ObservableProperty] private string _aiInputText = string.Empty;
    [ObservableProperty] private bool   _aiIsBusy;
    [ObservableProperty] private bool   _voiceActive;

    private ILlmProvider LlmProvider => LlmProviderRegistry.Basic;

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

    public ShellViewModel(BackgroundActivityManager activityManager)
    {
        _activityManager = activityManager;
        _activityManager.IsActiveChanged += (_, active) =>
            Application.Current.Dispatcher.Invoke(() => AiIsBusy = active);

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
            PageKinds.AiChat       => MakeAiChatTabFactory(),
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

    // The currently active chat page VM, if any tab is an AI Chat tab
    private AiChatViewModel? ActiveChatVm =>
        (CurrentPage as AiChatPage)?.ViewModel;

    [RelayCommand]
    private async Task SendAiMessage()
    {
        var text = AiInputText.Trim();
        if (string.IsNullOrEmpty(text)) return;
        AiInputText = string.Empty;

        // 1. Score all handlers: globally registered + current tab's VM if applicable
        var handlers = new List<IQueryHandler>(FeatureManager.Instance.QueryHandlers);
        if (CurrentPage?.DataContext is IQueryHandler tabHandler)
            handlers.Add(tabHandler);

        var scored    = handlers.Select(h => (handler: h, score: h.CanProcess(text))).ToList();
        var positives = scored.Where(x => x.score > 0).ToList();
        var highConf  = scored.Where(x => x.score > 0.8f).ToList();

        // 2. Exactly one handler above 0.8 → process directly
        if (highConf.Count == 1)
        {
            var result = await highConf[0].handler.ProcessAsync(text);
            if (result is not null)
                await GetOrCreateChatVm().AddExchangeAsync(text, result);
            return;
        }

        // Gather context from the current tab
        var ctxProvider = (CurrentPage?.DataContext as IContextProvider)
                       ?? (CurrentPage as IContextProvider);
        var context = ctxProvider?.GetContext() ?? "No specific context available.";

        // 3. Multiple candidate handlers → ask the LLM to pick the right tool
        if (positives.Count > 0)
        {
            string? llmReply = null;
            try
            {
                var toolList = string.Join("\n", positives.Select((x, i) =>
                    $"{i + 1}. {x.handler.Description}"));
                var userPrompt =
                    $"The user asked: \"{text}\"\n" +
                    $"Current context: {context}\n\n" +
                    $"Available tools:\n{toolList}\n\n" +
                    "Which tool number should handle this request? " +
                    "Reply with only the number, or \"ask: <question>\" to request clarification.";
                var response = await LlmProvider.QueryAsync(string.Empty, userPrompt);
                llmReply = response?.RawText?.Trim();
            }
            catch (LlmProviderException ex)
            {
                ShowError("AI provider error", ex.Message);
                return;
            }

            if (llmReply is null) return;

            if (llmReply.StartsWith("ask:", StringComparison.OrdinalIgnoreCase))
            {
                await GetOrCreateChatVm().AddExchangeAsync(text, llmReply["ask:".Length..].Trim());
                return;
            }

            if (int.TryParse(llmReply, out var toolIdx) && toolIdx >= 1 && toolIdx <= positives.Count)
            {
                var result = await positives[toolIdx - 1].handler.ProcessAsync(text);
                if (result is not null)
                    await GetOrCreateChatVm().AddExchangeAsync(text, result);
                return;
            }

            // LLM gave an unexpected response — show it as conversation
            await GetOrCreateChatVm().AddExchangeAsync(text, llmReply);
            return;
        }

        // 4. No handler matched — use ChatAsync when on AiChat page (history context),
        //    otherwise QueryAsync with a context-enriched prompt.
        LlmResponse? actionResponse = null;
        try
        {
            var activeChatVm = ActiveChatVm;
            if (activeChatVm?.ActiveConversation is { Messages.Count: > 0 } conv)
            {
                var history = conv.Messages
                    .Select(m => new LlmMessage(m.IsUser, m.Text))
                    .ToList();
                actionResponse = await LlmProvider.ChatAsync(history, text);
            }
            else
            {
                var actions     = ctxProvider?.GetAvailableActions() ?? [];
                var actionsText = actions.Count > 0
                    ? string.Join("\n", actions.Select(a =>
                        $"- {a.Name}: {a.Description}" +
                        (a.Parameters is { Count: > 0 }
                            ? " (params: " + string.Join(", ", a.Parameters.Select(p => $"{p.Key}: {p.Value}")) + ")"
                            : string.Empty)))
                    : "No specific actions available.";

                var userPrompt =
                    $"The user asked: \"{text}\"\n" +
                    $"Current context: {context}\n\n" +
                    $"Available actions:\n{actionsText}\n\n" +
                    "If you can perform an action, reply with JSON only: " +
                    "{\"action\": \"ActionName\", \"paramName\": \"value\", ...}\n" +
                    "Otherwise reply conversationally.";

                actionResponse = await LlmProvider.QueryAsync(string.Empty, userPrompt);
            }
        }
        catch (LlmProviderException ex)
        {
            ShowError("AI provider error", ex.Message);
            return;
        }

        if (actionResponse?.FocusTab is { } ft)
        {
            HandleFocusTabInstruction(ft);
            return;
        }

        var rawReply = actionResponse?.RawText?.Trim() ?? string.Empty;

        // Try to execute a JSON action via the current tab's IActionExecutor
        if (rawReply.StartsWith('{'))
        {
            var executor = (CurrentPage?.DataContext as IActionExecutor)
                        ?? (CurrentPage as IActionExecutor);
            if (executor is not null)
            {
                try
                {
                    var handled = await executor.TryExecuteActionAsync(rawReply);
                    if (!handled)
                        ShowError("Action failed", $"Could not execute: {rawReply}");
                }
                catch (Exception ex)
                {
                    ShowError("Action error", ex.Message);
                }
                return;
            }
        }

        // Plain conversational reply → show in AI Chat
        if (!string.IsNullOrEmpty(rawReply))
            await GetOrCreateChatVm().AddExchangeAsync(text, rawReply);
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

    /// <summary>
    /// Returns the AiChatViewModel for the active (or newly opened) AI Chat tab.
    /// </summary>
    private AiChatViewModel GetOrCreateChatVm()
    {
        var existing = Tabs.FirstOrDefault(t => t.Title == "AI Chat");
        if (existing is not null)
        {
            ActivateTab(existing);
            return ((AiChatPage)existing.GetOrCreatePage()).ViewModel;
        }

        var vm  = new AiChatViewModel();
        var tab = new TabEntry
        {
            Title       = "AI Chat",
            Icon        = "💬",
            Breadcrumbs = [new BreadcrumbSegment { Label = "AI Chat" }]
        };
        tab.PageFactory = () =>
        {
            var page = new AiChatPage(vm);
            page.TitleChanged += title =>
            {
                tab.Title = title;
                tab.Breadcrumbs = [new BreadcrumbSegment { Label = title }];
                if (tab == ActiveTab) UpdateBreadcrumbs(tab);
            };
            return page;
        };
        OpenTab(tab);
        return vm;
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
            "ai chat" or "chat" =>
                MakeAiChatTabFactory(),
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
            PageKinds.AiChat     => MakeAiChatTabFactory,
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

    private TabEntry MakeAiChatTabFactory()
    {
        var vm  = new AiChatViewModel();
        var tab = new TabEntry
        {
            Title       = "AI Chat",
            Icon        = "💬",
            Breadcrumbs = [new BreadcrumbSegment { Label = "AI Chat" }]
        };
        tab.PageFactory = () =>
        {
            var page = new AiChatPage(vm);
            page.TitleChanged += title =>
            {
                tab.Title = title;
                tab.Breadcrumbs = [new BreadcrumbSegment { Label = title }];
                if (tab == ActiveTab) UpdateBreadcrumbs(tab);
            };
            return page;
        };
        return tab;
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

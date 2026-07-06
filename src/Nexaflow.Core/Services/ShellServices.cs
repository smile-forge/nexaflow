using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.Controls;
using Nexaflow.Core.Models;
using Nexaflow.Core.ViewModels;
using Nexaflow.Core.Views;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common;
using Nexaflow.IO.Common;
using Nexaflow.Providers.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Nexaflow.Core.Services;

/// <summary>
/// Per-Workspace shell service that owns that workspace's tab + window registry.
/// One instance per <see cref="Workspace"/> (created by <see cref="WorkspaceManager"/>
/// during bootstrap, before any window); each <see cref="MainWindow"/> showing this
/// workspace registers its <see cref="IWindowHost"/> on activation and unregisters on close.
/// </summary>
public sealed class ShellServices : IShellServices
{
    private readonly Workspace _workspace;
    private readonly IBackgroundActivityManager? _activity;
    private readonly Elevation.ElevatedBridgeLauncher _elevation = new();

    // This workspace's UI dispatcher, captured on the thread that bootstraps it (the UI thread).
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    public ShellServices(Workspace workspace, IBackgroundActivityManager? activity = null)
    {
        _workspace = workspace;
        _activity  = activity;
    }

    /// <summary>
    /// Runs an admin-only request out-of-process via the elevated privilege bridge (one UAC prompt per
    /// call). The host stays non-elevated. Never throws for expected outcomes — see <see cref="ElevatedResult"/>.
    /// </summary>
    public Task<ElevatedResult> RunElevatedAsync(ElevatedRequest request, CancellationToken ct = default)
        => _elevation.RunAsync(request, ct);

    public void QueueBackgroundTask(IBackgroundTask task, Action<bool>? onComplete = null,
                                    CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        var handle = _activity?.StartActivity(task.Description);
        _ = Task.Run(async () =>
        {
            var ok = false;
            var cancelled = false;
            try
            {
                await task.RunAsync(ct);
                handle?.Complete();
                ok = true;
            }
            catch (OperationCanceledException)
            {
                // Caller aborted (e.g. navigated away) — finish quietly, no failure reported.
                cancelled = true;
                handle?.Complete();
            }
            catch (Exception ex)
            {
                handle?.Fail(ex.Message);
            }
            finally
            {
                if (onComplete is not null && !cancelled)
                    _ui.Invoke(() => onComplete(ok));
            }
        });
    }

    // ── Window registry ───────────────────────────────────────────────────

    private readonly List<IWindowHost> _windows = [];

    // Active file watches, deduped by full path: one FileChangeWatcher per path fans out to its subscribers.
    private readonly Dictionary<string, WatchGroup> _watchGroups = new(StringComparer.OrdinalIgnoreCase);
    private IWindowHost? _focused;

    /// <summary>
    /// Factory invoked by <see cref="TearOffTab"/> to spawn a new shell window.
    /// The factory should create the window, call <see cref="RegisterWindow"/>, and
    /// return the host.  Set by <see cref="App"/> after initial window creation.
    /// </summary>
    internal Func<IWindowHost>? CreateWindowFactory { get; set; }

    internal bool HasWindows => _windows.Count > 0;

    /// <summary>The focused window, or the first open one (null when window-less).</summary>
    internal IWindowHost? FocusedWindow => _focused ?? _windows.FirstOrDefault();

    internal void RegisterWindow(IWindowHost host) => _windows.Add(host);

    internal void UnregisterWindow(IWindowHost host)
    {
        CloseWindowTabs(host);

        _windows.Remove(host);

        if (_focused == host)
            _focused = _windows.FirstOrDefault();

        // When this workspace's last window closes, release its runtime resources.
        WorkspaceManager.Instance.NotifyWindowClosed(_workspace);

        // Shut down only when no windows remain across all live workspaces.
        // In --prestart daemon mode, stay alive windowless so the next click can show a window
        // instantly. During an update the daemon must exit so the installer can replace its files.
        if (!WorkspaceManager.Instance.AnyWindowsOpen && (!App.IsResident || App.IsUpdating))
            Application.Current.Shutdown();
    }

    /// <summary>
    /// Raises <see cref="Page.Closed"/> for every tab of a closing window so each page's view-model
    /// disposes (terminal PTY shells, WebView2, …). This is the one teardown path that would otherwise
    /// drop tabs from the registry without firing Closed, orphaning their child processes. Tab tear-off
    /// and move re-key <c>_tabToWindow</c> and never pass through here, so a closing window only owns
    /// tabs that are genuinely going away; RaiseClosed is safe to double-fire (view-model Dispose is
    /// idempotent) should a tab also have been closed explicitly. Separated from
    /// <see cref="UnregisterWindow"/> so it can be unit-tested without that method's shutdown side-effects.
    /// </summary>
    internal void CloseWindowTabs(IWindowHost host)
    {
        foreach (var tab in host.Tabs.ToList())
        {
            tab.RaiseClosed();
            _tabToWindow.Remove(tab);
            DisposePage(tab);
        }
    }

    internal void SetFocused(IWindowHost host)
    {
        _focused = host;
        foreach (var w in _windows)
            w.IsFocused = w == host;
    }

    /// <summary>
    /// Applies <paramref name="theme"/> and restarts <paramref name="current"/> so the new theme takes
    /// effect, reopening the same tabs (order + active tab preserved). Themes bind via StaticResource
    /// for rendering performance and so do not live-reflow an open window — changing the theme in
    /// Options rebuilds the window instead. The fresh window is created and shown BEFORE the old one
    /// closes so the workspace is never left window-less (which would dispose it); closing the old
    /// window first clears its tabs from the registry, so the reopen below builds them fresh against
    /// the new theme rather than moving the old (old-theme) page objects across.
    /// </summary>
    internal void RestartWindowForTheme(IWindowHost current, ThemeOption theme)
    {
        if (CreateWindowFactory is null) return;

        // Snapshot the acting window's tabs (front-to-back) and which one is active.
        var snapshot = current.Tabs
            .Where(t => !string.IsNullOrEmpty(t.PageKind))
            .Select(t => (Kind: t.PageKind!, t.PageParams, t.IsActive))
            .ToList();

        ThemeManager.Apply(theme, FeatureManager.Instance.ThemeContributionUris);

        // Fresh window, placed where the old one was, then close the old one.
        var fresh = CreateWindowFactory();
        var src   = current.Window;
        var dst   = fresh.Window;
        dst.WindowStartupLocation = WindowStartupLocation.Manual;
        var bounds = src.WindowState == WindowState.Maximized
            ? src.RestoreBounds
            : new Rect(src.Left, src.Top, src.Width, src.Height);
        dst.Left = bounds.Left; dst.Top = bounds.Top; dst.Width = bounds.Width; dst.Height = bounds.Height;
        dst.Show();
        if (src.WindowState == WindowState.Maximized) dst.WindowState = WindowState.Maximized;

        current.Window.Close();   // synchronously unregisters its tabs from _tabToWindow

        // Reopen in reverse (Pane.Add prepends) so the original left-to-right order is preserved.
        for (int i = snapshot.Count - 1; i >= 0; i--)
            OpenTabCore(snapshot[i].Kind, snapshot[i].PageParams, null, inRightPane: false);

        var active = snapshot.FirstOrDefault(s => s.IsActive);
        if (active.Kind is not null && FindTabCore(active.Kind, active.PageParams) is { } activeTab)
            fresh.SetActiveTab(activeTab);
    }

    internal void ClearFocused(IWindowHost host)
    {
        if (_focused == host)
            _focused = _windows.FirstOrDefault(w => w != host);
    }

    // ── Tab registry ──────────────────────────────────────────────────────

    private readonly Dictionary<Page, IWindowHost> _tabToWindow = [];

    public IReadOnlyList<Window> OpenWindows => _windows.Select(w => w.Window).ToList();

    // ── IShellServices ────────────────────────────────────────────────────

    public void OpenTab(string pageKind, Dictionary<string, string>? pageParams = null,
                        IPageView? caller = null, bool inRightPane = false)
    {
        _ui.Invoke(() => OpenTabCore(pageKind, pageParams, caller, inRightPane));
    }

    /// <summary>Resolves the window a tab open should target: the one owning <paramref name="caller"/>,
    /// else the focused window, else the first.</summary>
    private IWindowHost? ResolveTargetWindow(IPageView? caller)
    {
        IWindowHost? targetWindow = null;

        if (caller is UserControl callerControl)
        {
            var callerTab = _tabToWindow.Keys.FirstOrDefault(t => t.Content == callerControl);
            if (callerTab is not null)
                _tabToWindow.TryGetValue(callerTab, out targetWindow);
        }

        return targetWindow ?? _focused ?? _windows.FirstOrDefault();
    }

    private void OpenTabCore(string pageKind, Dictionary<string, string>? pageParams,
                             IPageView? caller, bool inRightPane)
    {
        // 1. Resolve target window from caller page or focused window
        var targetWindow = ResolveTargetWindow(caller);
        if (targetWindow is null) return;

        // Right-pane open: split off (or focus the existing) right pane and always create a fresh
        // tab there — skip the global find-and-move so the same location can sit in both panes.
        if (inRightPane)
        {
            targetWindow.FocusSecondPane();
            var fresh = CreateTab(pageKind, pageParams);
            if (fresh is null) return;
            _tabToWindow[fresh] = targetWindow;
            targetWindow.AddTab(fresh);
            return;
        }

        // 2. Search globally for a matching tab
        var existing = FindTabCore(pageKind, pageParams);

        if (existing is not null)
        {
            if (_tabToWindow.TryGetValue(existing, out var ownerWindow) && ownerWindow != targetWindow)
                MoveTabCore(existing, ownerWindow, targetWindow);

            (existing.Content as IPageView)?.Reinitialize(pageParams ?? []);

            targetWindow.BringToFront(existing);
            targetWindow.SetActiveTab(existing);
            return;
        }

        // 3. Create new tab and register it
        var tab = CreateTab(pageKind, pageParams);
        if (tab is null) return;

        _tabToWindow[tab] = targetWindow;
        targetWindow.AddTab(tab);
    }

    public void CloseTab(Page tab)
    {
        if (!_tabToWindow.TryGetValue(tab, out var host)) return;
        tab.RaiseClosed();
        _tabToWindow.Remove(tab);
        host.RemoveTab(tab);
        DisposePage(tab);
    }

    /// <summary>
    /// Central teardown safety net: disposes a closed tab's materialized content view and its view-model
    /// when they implement <see cref="IDisposable"/>, so a feature that forgets to wire
    /// <c>page.Closed += vm.Dispose()</c> still can't leak native handles (file watchers, PTYs, media
    /// engines, …). Called from the two genuine-close paths (<see cref="CloseTab"/> and
    /// <see cref="CloseWindowTabs"/>) after <see cref="Page.RaiseClosed"/> — so a feature that DID wire its
    /// own teardown has already run and this is an idempotent second Dispose (per the IDisposable contract,
    /// which this file already relies on for double-fired Closed). Tear-off / move re-key the registry and
    /// never reach here, so a still-live tab is never disposed. Skips a tab never activated (no content).
    /// Never throws — teardown must not break tab/window close.
    /// </summary>
    private static void DisposePage(Page tab)
    {
        if (tab.Content is not { } content) return;   // never activated → nothing was created to dispose

        if (content is IPageView { ViewModel: IDisposable vm })
            try { vm.Dispose(); } catch { }

        if (content is IDisposable view)
            try { view.Dispose(); } catch { }
    }

    /// <summary>
    /// Closes every tab across all of this workspace's windows (windows stay open). Used when the
    /// workspace is reconfigured for a new profile — open pages captured the now-replaced services.
    /// </summary>
    internal void CloseAllTabs()
    {
        foreach (var tab in _tabToWindow.Keys.ToList())
            CloseTab(tab);
    }

    /// <summary>
    /// Closes every window in this workspace except <paramref name="keep"/>. Used on a profile
    /// switch so the change collapses to the acting window instead of leaving the others empty and
    /// showing the old profile.
    /// </summary>
    internal void CloseOtherWindows(IWindowHost keep)
    {
        foreach (var host in _windows.Where(w => !ReferenceEquals(w, keep)).ToList())
            host.Window.Close();
    }

    /// <summary>
    /// Closes every window in this workspace. The last close runs <see cref="UnregisterWindow"/> →
    /// <see cref="WorkspaceManager.NotifyWindowClosed"/>, releasing the workspace's providers and
    /// evicting its caches. Used by <see cref="WorkspaceManager.RestartWorkspace"/> after a fresh
    /// replacement workspace+window is already showing.
    /// </summary>
    internal void CloseAllWindows()
    {
        foreach (var host in _windows.ToList())
            host.Window.Close();
        DisposeAllWatches();
    }

    // ── File watching ──────────────────────────────────────────────────────
    // Features call WatchFile instead of creating their own FileChangeWatcher, so dispatcher
    // marshalling, per-path dedup and teardown live here rather than leaking into each feature.

    public IFileWatch WatchFile(string path, Action onChanged)
    {
        var key = SafeFullPath(path);
        if (!_watchGroups.TryGetValue(key, out var group))
        {
            group = new WatchGroup();
            try
            {
                group.Watcher = new FileChangeWatcher(key);
                group.Watcher.Changed += () => OnWatchChanged(key);
            }
            catch { /* missing directory etc. — the handle is inert but still disposable */ }
            _watchGroups[key] = group;
        }

        var handle = new FileWatch(this, key, onChanged);
        group.Subscribers.Add(handle);
        return handle;
    }

    public Task RunOnUiAsync(Action action)
    {
        if (_ui.CheckAccess()) { action(); return Task.CompletedTask; }
        return _ui.InvokeAsync(action).Task;
    }

    public Task<T> RunOnUiAsync<T>(Func<Task<T>> action)
        => _ui.CheckAccess() ? action() : _ui.InvokeAsync(action).Task.Unwrap();

    // The raw watcher event arrives on a thread-pool thread; hop to the UI thread, then fan out.
    private void OnWatchChanged(string key) => _ui.InvokeAsync(() =>
    {
        if (!_watchGroups.TryGetValue(key, out var group)) return;
        foreach (var sub in group.Subscribers.ToArray())
            sub.RaiseChanged();
    });

    private void Unwatch(string key, FileWatch handle)
    {
        if (!_watchGroups.TryGetValue(key, out var group)) return;
        group.Subscribers.Remove(handle);
        if (group.Subscribers.Count == 0)
        {
            group.Watcher?.Dispose();
            _watchGroups.Remove(key);
        }
    }

    private void DisposeAllWatches()
    {
        foreach (var group in _watchGroups.Values)
            group.Watcher?.Dispose();
        _watchGroups.Clear();
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            // Watch the outermost real container, so a request to watch a file *inside* an archive
            // (a non-existent OS path) watches the archive that actually changes on disk. Resolving such
            // a container change back to the specific inner view is deferred.
            return VirtualFileSystem.Instance.SplitOutermostContainer(Path.GetFullPath(path)).RealContainer;
        }
        catch { return path; }
    }

    private sealed class WatchGroup
    {
        public FileChangeWatcher? Watcher;
        public readonly List<FileWatch> Subscribers = [];
    }

    // The wrapper returned to callers: toggle Enabled to hold/flush, dispose to stop watching.
    private sealed class FileWatch(ShellServices owner, string key, Action onChanged) : IFileWatch
    {
        private bool _enabled = true;
        private bool _pending;
        private bool _disposed;

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (value && _pending) { _pending = false; onChanged(); }
            }
        }

        // Always invoked on the UI thread (from the marshalled fan-out).
        public void RaiseChanged()
        {
            if (_disposed) return;
            if (_enabled) onChanged();
            else _pending = true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            owner.Unwatch(key, this);
        }
    }

    public Page? FindTab(string pageKind, Dictionary<string, string>? pageParams = null)
        => FindTabCore(pageKind, pageParams);

    // Post to the global MessageCenter — the focused window toasts it (errors) or it lands in the
    // shared inbox (notifications). See MessageCenter / ShellViewModel.
    public void ShowError(string message) =>
        MessageCenter.Instance.Post(new NotificationItem
        {
            Title = "Error", Body = message, Severity = MessageSeverity.Error, ShowToast = true,
        });

    public void ShowNotification(string message) =>
        MessageCenter.Instance.Post(new NotificationItem
        {
            Title = "Info", Body = message, Severity = MessageSeverity.Info,
        });

    public void ShowNotification(string message, Page tab) =>
        MessageCenter.Instance.Post(new NotificationItem
        {
            Title = "Info", Body = message, Severity = MessageSeverity.Info, ShowToast = true,
            Actions = [new MessageAction("Open", new RelayCommand(() => ActivateTab(tab)), IsPrimary: true)],
        });

    /// <summary>Brings the window owning <paramref name="tab"/> to the front and selects it.</summary>
    private void ActivateTab(Page tab)
    {
        var host = _windows.FirstOrDefault(w => w.Tabs.Contains(tab)) ?? _focused ?? _windows.FirstOrDefault();
        if (host is null) return;
        host.SetActiveTab(tab);
        try { host.Window.Activate(); } catch { /* window closing / not realised */ }
    }

    public void InsertChatInput(string text)
        => (_focused ?? _windows.FirstOrDefault())?.InsertChatInput(text);

    public void SubmitAiQuery(string query)
        => (_focused ?? _windows.FirstOrDefault())?.SubmitAiQuery(query);

    // ── Cross-window tab operations ───────────────────────────────────────

    public void TearOffTab(Page tab)
    {
        if (!_tabToWindow.TryGetValue(tab, out var sourceHost)) return;
        _tabToWindow.Remove(tab);
        sourceHost.RemoveTab(tab);

        if (CreateWindowFactory is null) return;

        var (dropX, dropY, _, _, work) = WindowManager.GetCursorInfo();

        // Factory creates a new empty window and registers it with ShellServices
        var newHost = CreateWindowFactory();
        _tabToWindow[tab] = newHost;
        newHost.AddTab(tab);

        PositionWindow(newHost.Window, dropX, dropY, work);
        newHost.Window.Show();
    }

    /// <summary>
    /// Spawns a fresh shell window and opens a single tab in it for the given page kind.
    /// Used by the ribbon's "Open in new Window" context-menu action.
    /// </summary>
    public void OpenPageInNewWindow(string pageKind, Dictionary<string, string>? pageParams = null)
    {
        var newHost = CreateAndShowNewWindow();
        if (newHost is null) return;
        // SetFocused fires via Activated event before we hit OpenTab; the open lands in the new window.
        OpenTab(pageKind, pageParams);
    }

    /// <summary>
    /// Creates and shows a new shell window, making it the focused window.
    /// Returns null if no factory is registered.
    /// </summary>
    internal IWindowHost? CreateAndShowNewWindow()
    {
        if (CreateWindowFactory is null) return null;
        var newHost = CreateWindowFactory();
        newHost.Window.Show();
        return newHost;
    }

    internal void MoveTab(Page tab, IWindowHost targetHost)
    {
        if (!_tabToWindow.TryGetValue(tab, out var sourceHost)) return;
        if (sourceHost == targetHost) { targetHost.SetActiveTab(tab); return; }
        MoveTabCore(tab, sourceHost, targetHost);
    }

    private void MoveTabCore(Page tab, IWindowHost source, IWindowHost target)
    {
        _tabToWindow[tab] = target;
        source.RemoveTab(tab);
        target.AddTab(tab);
    }

    // ── Tab creation ──────────────────────────────────────────────────────

    public IReadOnlyList<Page> GetContextItemPages()
        => FeatureManager.Instance.GetContextItemPages(_workspace);

    private Page? CreateTab(string pageKind, Dictionary<string, string>? pageParams)
    {
        if (FeatureManager.Instance.IsRegistered(pageKind))
            return FeatureManager.Instance.CreateTab(pageKind, _workspace, pageParams);

        return MakePlaceholderTab(pageKind, "📄");
    }

    private static Page MakePlaceholderTab(string title, string icon) => new()
    {
        Title       = title,
        Icon        = icon,
        PageKind    = title,
        Breadcrumbs = {new BreadcrumbSegment { Label = title }},
        ContentFactory = () => new PlaceholderPage()
    };

    // ── Matching helpers ──────────────────────────────────────────────────

    private Page? FindTabCore(string pageKind, Dictionary<string, string>? pageParams)
    {
        foreach (var tab in _tabToWindow.Keys)
        {
            if (!string.Equals(tab.PageKind, pageKind, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!ParamsCompatible(tab.PageParams, pageParams))
                continue;
            return tab;
        }
        return null;
    }

    /// <summary>
    /// A tab with null/empty PageParams is a type-identity tab (matches any request for that
    /// PageKind). A tab with params is a location-identity tab — every entry in its PageParams
    /// must be present and equal in the requested params.
    /// </summary>
    private static bool ParamsCompatible(
        Dictionary<string, string>? tabParams,
        Dictionary<string, string>? requested)
    {
        if (tabParams is null || tabParams.Count == 0) return true;
        if (requested is null || requested.Count == 0) return false;
        foreach (var kv in tabParams)
        {
            if (!requested.TryGetValue(kv.Key, out var v) ||
                !string.Equals(v, kv.Value, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    // ── Shell-level overlays (routed to the focused window) ──────────────────

    void IShellServices.ShowPrompt(string title, string label, string initialValue,
                                   Action<string> onConfirm, Action onCancel)
        => (_focused ?? _windows.FirstOrDefault())
            ?.ShowPrompt(title, label, initialValue, onConfirm, onCancel);

    void IShellServices.ShowConfirmation(string title, string message,
                                         Action onConfirm, Action onCancel)
        => (_focused ?? _windows.FirstOrDefault())
            ?.ShowConfirmation(title, message, onConfirm, onCancel);

    public Task<bool> ConfirmAsync(string title, string message, CancellationToken ct = default)
        => ConfirmAsync(title, message, confirmLabel: null, cancelLabel: null, ct);

    public Task<bool> ConfirmAsync(string title, string message, string? confirmLabel, string? cancelLabel,
                                   CancellationToken ct = default)
    {
        var dispatcher = _ui;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            return dispatcher.Invoke(() => ConfirmAsync(title, message, confirmLabel, cancelLabel, ct));

        var host = _focused ?? _windows.FirstOrDefault();
        if (host is null) return Task.FromResult(false);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => tcs.TrySetResult(false));
        host.ShowConfirmation(title, message,
            onConfirm: () => tcs.TrySetResult(true),
            onCancel:  () => tcs.TrySetResult(false),
            confirmLabel: confirmLabel, cancelLabel: cancelLabel);
        return tcs.Task;
    }

    /// <summary>
    /// Snapshot of each open window in this workspace and its open tabs, taken on the UI thread so the
    /// (thread-affine) <see cref="Window.Title"/> is safe to read. Used to describe the shell to the AI.
    /// </summary>
    public IReadOnlyList<(string Title, IReadOnlyList<(string Title, string? PageKind)> Tabs)> GetWindowsWithTabs()
    {
        var dispatcher = _ui;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            return dispatcher.Invoke(GetWindowsWithTabs);

        var result = new List<(string, IReadOnlyList<(string, string?)>)>(_windows.Count);
        var n = 1;
        foreach (var host in _windows)
        {
            var title = string.IsNullOrWhiteSpace(host.Window?.Title) ? $"Window {n}" : host.Window!.Title;
            var tabs  = host.Tabs.Select(t => (t.Title, t.PageKind)).ToList();
            result.Add((title, tabs));
            n++;
        }
        return result;
    }

    // ── File pickers (reuse the shell's themed picker windows) ───────────────

    public Task<string?> PickOpenFileAsync(IReadOnlyList<string>? extensions = null,
                                           string? initialPath = null)
    {
        var dispatcher = _ui;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            return dispatcher.Invoke(() => PickOpenFileAsync(extensions, initialPath));

        var owner = FocusedWindow?.Window;
        return Task.FromResult(FileBrowserWindow.Show(initialPath, extensions, owner));
    }

    public Task<string?> PickFolderAsync(string? initialPath = null)
    {
        var dispatcher = _ui;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            return dispatcher.Invoke(() => PickFolderAsync(initialPath));

        return Task.FromResult(FolderBrowserWindow.Show(initialPath, FocusedWindow?.Window));
    }

    public Task<string?> PickSaveFileAsync(string defaultFileName,
                                           IReadOnlyList<string>? extensions = null,
                                           string? initialPath = null)
    {
        var dispatcher = _ui;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            return dispatcher.Invoke(() => PickSaveFileAsync(defaultFileName, extensions, initialPath));

        // Pick the destination folder, then prompt for the file name — composing the two existing
        // picker surfaces gives a save-target without a Win32 SaveFileDialog.
        var folder = FolderBrowserWindow.Show(initialPath, FocusedWindow?.Window);
        if (folder is null) return Task.FromResult<string?>(null);

        var ext = extensions is { Count: > 0 } ? extensions[0] : null;

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = FocusedWindow;
        if (host is null) return Task.FromResult<string?>(null);

        host.ShowPrompt("Save As", "File name:", defaultFileName,
            onConfirm: name =>
            {
                name = name.Trim();
                if (string.IsNullOrEmpty(name)) { tcs.TrySetResult(null); return; }
                if (ext is not null && !name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    name += ext;
                tcs.TrySetResult(System.IO.Path.Combine(folder, name));
            },
            onCancel: () => tcs.TrySetResult(null));
        return tcs.Task;
    }

    // Refreshes the focused window's active page by re-initialising it with its current params.
    // Reinitialize is the established refresh idiom (tab (re)activation calls it too); for the file
    // browser it re-enumerates the current folder. Actions that mutate the file system from a
    // shell-level overlay — rename / delete — call this from their confirm callback.
    void IShellServices.RequestRefresh()
    {
        _ui.Invoke(() =>
        {
            var active = FocusedWindow?.Tabs.FirstOrDefault(t => t.IsActive);
            if (active?.Content is IPageView view)
                view.Reinitialize(active.PageParams ?? []);
        });
    }

    void IShellServices.SaveFeatureConfig(IFeatureConfig config)
    {
        // Workspace-scoped configs are persisted per-profile under Contexts\<name>\ (the same place the
        // Configure panel loads them from); global feature configs go in the shared config root. Routing
        // a scoped config through the global Save would write where nothing reads it back.
        if (FeatureManager.Instance.IsWorkspaceScopedConfig(config.GetType()))
            ConfigManager.Instance.SaveTo(_workspace.Profile.Dir, config, config.ConfigName);
        else
            ConfigManager.Instance.Save(config, config.ConfigName);
    }

    void IShellServices.OpenOptions(string configName)
        => _ui.Invoke(() =>
        {
            // IWindowHost is implemented only by ShellViewModel; the feature reaches this via
            // IShellServices, never IWindowHost itself.
            if ((_focused ?? _windows.FirstOrDefault()) is ShellViewModel vm)
                vm.OpenOptionsAt(configName);
        });

    void IShellServices.OpenWorkspaceConfig(string configName)
        => _ui.Invoke(() =>
        {
            // Deep-link the Configure overlay to this workspace's profile, on the named section.
            if ((_focused ?? _windows.FirstOrDefault()) is ShellViewModel vm)
                vm.OpenConfigureAt(_workspace.Profile, configName);
        });

    void IShellServices.ShowOverlay(object overlayViewModel)
        => _ui.Invoke(() =>
        {
            if ((_focused ?? _windows.FirstOrDefault()) is ShellViewModel vm)
                vm.ShowOverlay(overlayViewModel);
        });

    void IShellServices.CloseOverlay()
        => _ui.Invoke(() =>
        {
            if ((_focused ?? _windows.FirstOrDefault()) is ShellViewModel vm)
                vm.CloseOverlay();
        });

    void IShellServices.PinToRibbon(string format, object payload)
        => (_focused ?? _windows.FirstOrDefault())
            ?.AddRibbonPin(new RibbonPinRequest(format, payload));

    public bool HandleObject(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        foreach (var type in DiscoverImplementations<IGenericObjectHandler>())
        {
            // Instances are cached per (type, workspace) and ctor-injected with this
            // workspace's shell/ai/configs — same path as feature tab creation.
            if (FeatureManager.Instance.Instantiate(type, _workspace) is IGenericObjectHandler handler
                && handler.CanHandleObject(obj))
            {
                handler.Handle(obj);
                return true;
            }
        }
        return false;
    }

    // Index-backed (FeatureCatalog): returns every implementation regardless of whether the owning feature
    // assembly has been warmed up yet — resolving each one loads + activates its assembly on demand. This is
    // what keeps the first FileSystem page's action set complete before the background warm-up finishes.
    public IEnumerable<Type> DiscoverImplementations<TInterface>()
        => FeatureCatalog.Instance.TypesImplementing<TInterface>();

    // ── Window positioning (tearoff) ──────────────────────────────────────

    private static void PositionWindow(Window win, double dropX, double dropY, Rect work)
    {
        const double TopBarHeight           = 72;
        const double TabBarHeight           = 38;
        const double TabStripColumnFraction = 0.45;
        const double TabStripInternalMargin = 8;
        const double DefaultWindowWidth     = 1280;
        const double DefaultWindowHeight    = 780;

        double tabOffsetX = DefaultWindowWidth  * TabStripColumnFraction + TabStripInternalMargin;
        double tabOffsetY = TopBarHeight + TabBarHeight / 2.0;

        win.Left = Math.Max(work.Left, Math.Min(dropX - tabOffsetX, work.Right  - DefaultWindowWidth));
        win.Top  = Math.Max(work.Top,  Math.Min(dropY - tabOffsetY, work.Bottom - DefaultWindowHeight));
    }
}

using Nexaflow.Core.Models;
using Nexaflow.Core.Views;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

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

    public ShellServices(Workspace workspace, IBackgroundActivityManager? activity = null)
    {
        _workspace = workspace;
        _activity  = activity;
    }

    public void QueueBackgroundTask(IBackgroundTask task, Action<bool>? onComplete = null)
    {
        ArgumentNullException.ThrowIfNull(task);

        var handle = _activity?.StartActivity(task.Description);
        _ = Task.Run(async () =>
        {
            var ok = false;
            try
            {
                await task.RunAsync(CancellationToken.None);
                handle?.Complete();
                ok = true;
            }
            catch (Exception ex)
            {
                handle?.Fail(ex.Message);
            }
            finally
            {
                if (onComplete is not null)
                    Application.Current?.Dispatcher.Invoke(() => onComplete(ok));
            }
        });
    }

    // ── Window registry ───────────────────────────────────────────────────

    private readonly List<IWindowHost> _windows = [];
    private IWindowHost? _focused;

    /// <summary>
    /// Factory invoked by <see cref="TearOffTab"/> to spawn a new shell window.
    /// The factory should create the window, call <see cref="RegisterWindow"/>, and
    /// return the host.  Set by <see cref="App"/> after initial window creation.
    /// </summary>
    internal Func<IWindowHost>? CreateWindowFactory { get; set; }

    internal bool HasWindows => _windows.Count > 0;

    internal void RegisterWindow(IWindowHost host) => _windows.Add(host);

    internal void UnregisterWindow(IWindowHost host)
    {
        foreach (var tab in host.Tabs.ToList())
            _tabToWindow.Remove(tab);

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

    internal void SetFocused(IWindowHost host)
    {
        _focused = host;
        foreach (var w in _windows)
            w.IsFocused = w == host;
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
                        IPageView? caller = null)
    {
        Application.Current.Dispatcher.Invoke(() => OpenTabCore(pageKind, pageParams, caller));
    }

    private void OpenTabCore(string pageKind, Dictionary<string, string>? pageParams,
                             IPageView? caller)
    {
        // 1. Resolve target window from caller page or focused window
        IWindowHost? targetWindow = null;

        if (caller is UserControl callerControl)
        {
            var callerTab = _tabToWindow.Keys.FirstOrDefault(t => t.Content == callerControl);
            if (callerTab is not null)
                _tabToWindow.TryGetValue(callerTab, out targetWindow);
        }

        targetWindow ??= _focused ?? _windows.FirstOrDefault();
        if (targetWindow is null) return;

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
            Title = "Info", Body = message, Severity = MessageSeverity.Info, ShowToast = false,
        });

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

    // No host-level refresh — feature views drive their own refresh from
    // file-system events; actions that mutate the file system rely on that.
    void IShellServices.RequestRefresh() { }

    void IShellServices.PinToRibbon(string contentKind, object payload)
        => (_focused ?? _windows.FirstOrDefault())
            ?.AddRibbonPin(new RibbonPinRequest(contentKind, payload));

    public IEnumerable<Type> DiscoverImplementations<TInterface>()
    {
        var target = typeof(TInterface);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = asm.GetName().Name;
            if (name is null) continue;
            if (!name.StartsWith("Nexaflow.", StringComparison.Ordinal)) continue;

            Type[] types;
            try { types = asm.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).ToArray()!; }

            foreach (var t in types)
            {
                if (t is null) continue;
                if (t.IsAbstract || t.IsInterface) continue;
                if (target.IsAssignableFrom(t)) yield return t;
            }
        }
    }

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

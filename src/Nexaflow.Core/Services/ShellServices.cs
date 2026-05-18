using Nexaflow.Core.FileSystem;
using Nexaflow.Core.Models;
using Nexaflow.Core.ViewModels;
using Nexaflow.Core.Views;
using Nexaflow.Features.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Nexaflow.Core.Services;

/// <summary>
/// Application-level singleton that owns the global tab registry.
/// Created in <see cref="App"/> before any windows; each <see cref="MainWindow"/>
/// registers its <see cref="IWindowHost"/> on activation and unregisters on close.
/// </summary>
public sealed class ShellServices : IShellServices
{
    // ── Window registry ───────────────────────────────────────────────────

    private readonly List<IWindowHost> _windows = [];
    private IWindowHost? _focused;

    /// <summary>
    /// Factory invoked by <see cref="TearOffTab"/> to spawn a new shell window.
    /// The factory should create the window, call <see cref="RegisterWindow"/>, and
    /// return the host.  Set by <see cref="App"/> after initial window creation.
    /// </summary>
    internal Func<TabEntry, IWindowHost>? CreateWindowFactory { get; set; }

    internal void RegisterWindow(IWindowHost host) => _windows.Add(host);

    internal void UnregisterWindow(IWindowHost host)
    {
        foreach (var tab in host.Tabs.ToList())
            _tabToWindow.Remove(tab);

        _windows.Remove(host);

        if (_focused == host)
            _focused = _windows.FirstOrDefault();

        if (_windows.Count == 0)
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

    private readonly Dictionary<TabEntry, IWindowHost> _tabToWindow = [];

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
            var callerTab = _tabToWindow.Keys.FirstOrDefault(t => t.Page == callerControl);
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

            (existing.Page as IPageView)?.Reinitialize(pageParams ?? []);

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

    public void CloseTab(TabEntry tab)
    {
        if (!_tabToWindow.TryGetValue(tab, out var host)) return;
        tab.RaiseClosed();
        _tabToWindow.Remove(tab);
        host.RemoveTab(tab);
    }

    public void UpdateTabMeta(TabEntry tab, string? title = null,
                              IReadOnlyList<BreadcrumbSegment>? breadcrumbs = null,
                              Dictionary<string, string>? pageParams = null)
    {
        if (title is not null)
            tab.Title = title;

        if (breadcrumbs is not null)
            tab.Breadcrumbs = breadcrumbs.ToList();

        if (pageParams is not null)
            tab.PageParams = pageParams;

        if (_tabToWindow.TryGetValue(tab, out var host))
            host.RefreshBreadcrumbs(tab);
    }

    public TabEntry? FindTab(string pageKind, Dictionary<string, string>? pageParams = null)
        => FindTabCore(pageKind, pageParams);

    public void ShowError(string message) =>
        (_focused ?? _windows.FirstOrDefault())?.ShowError(message);

    public void ShowNotification(string message) =>
        (_focused ?? _windows.FirstOrDefault())?.ShowNotification(message);

    // ── Cross-window tab operations ───────────────────────────────────────

    public void TearOffTab(TabEntry tab)
    {
        if (!_tabToWindow.TryGetValue(tab, out var sourceHost)) return;
        _tabToWindow.Remove(tab);
        sourceHost.RemoveTab(tab);

        if (CreateWindowFactory is null) return;

        var (dropX, dropY, _, _, work) = WindowManager.GetCursorInfo();

        // Factory creates a new empty window and registers it with ShellServices
        var newHost = CreateWindowFactory(tab);
        _tabToWindow[tab] = newHost;
        newHost.AddTab(tab);

        PositionWindow(newHost.Window, dropX, dropY, work);
        newHost.Window.Show();
    }

    internal void MoveTab(TabEntry tab, IWindowHost targetHost)
    {
        if (!_tabToWindow.TryGetValue(tab, out var sourceHost)) return;
        if (sourceHost == targetHost) { targetHost.SetActiveTab(tab); return; }
        MoveTabCore(tab, sourceHost, targetHost);
    }

    private void MoveTabCore(TabEntry tab, IWindowHost source, IWindowHost target)
    {
        _tabToWindow[tab] = target;
        source.RemoveTab(tab);
        target.AddTab(tab);
    }

    // ── Tab creation ──────────────────────────────────────────────────────

    private TabEntry? CreateTab(string pageKind, Dictionary<string, string>? pageParams)
    {
        if (string.Equals(pageKind, PageKinds.FileSystem, StringComparison.OrdinalIgnoreCase))
            return CreateFileSystemTab(
                pageParams?.GetValueOrDefault("label") ?? "Files", "📁", pageParams);

        if (FeatureManager.Instance.IsRegistered(pageKind))
            return FeatureManager.Instance.CreateTab(pageKind, pageParams);

        return MakePlaceholderTab(pageKind, "📄");
    }

    internal TabEntry CreateFileSystemTab(string label, string icon,
                                           Dictionary<string, string>? p)
    {
        var mode = p?.GetValueOrDefault("mode") ?? "thispc";

        if (mode == "path" && p!.TryGetValue("path", out var path))
        {
            var tab = new TabEntry
            {
                Title      = label,
                Icon       = icon,
                PageKind   = PageKinds.FileSystem,
                PageParams = p,
                Breadcrumbs = [new BreadcrumbSegment { Label = label }]
            };
            tab.PageFactory = () => CreateFileSystemPage(new FileSystemViewModel(path), tab);
            return tab;
        }
        else
        {
            var tab = new TabEntry
            {
                Title      = "This PC",
                Icon       = "🖥",
                PageKind   = PageKinds.FileSystem,
                PageParams = p ?? new() { ["mode"] = "thispc" },
                Breadcrumbs = [new BreadcrumbSegment { Label = "This PC" }]
            };
            tab.PageFactory = () => CreateFileSystemPage(FileSystemViewModel.CreateThisPc(), tab);
            return tab;
        }
    }

    private FileSystemView CreateFileSystemPage(FileSystemViewModel fsVm, TabEntry tab)
    {
        var keyHandler = new FileSystemKeyboardHandler(fsVm);
        var dropTarget = new FileSystemDropTarget(fsVm);
        var page = new FileSystemView(fsVm, keyHandler, dropTarget);
        page.NavigationChanged += segments => ApplyFileSystemBreadcrumbs(tab, page, segments);
        return page;
    }

    private bool _applyingBreadcrumbs;

    private void ApplyFileSystemBreadcrumbs(
        TabEntry tab,
        FileSystemView page,
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

            var currentLabel = segments[^1].Label;
            var newTitle = currentLabel.Length > 15
                ? currentLabel[..10] + "…"
                : currentLabel;

            var currentPath = segments[^1].Path;
            var newParams = string.IsNullOrEmpty(currentPath)
                ? new Dictionary<string, string> { ["mode"] = "thispc" }
                : new Dictionary<string, string> { ["mode"] = "path", ["path"] = currentPath };

            UpdateTabMeta(tab, newTitle, crumbs, newParams);
        }
        finally
        {
            _applyingBreadcrumbs = false;
        }
    }

    private static TabEntry MakePlaceholderTab(string title, string icon) => new()
    {
        Title       = title,
        Icon        = icon,
        PageKind    = title,
        Breadcrumbs = [new BreadcrumbSegment { Label = title }],
        PageFactory = () => new PlaceholderPage()
    };

    // ── Matching helpers ──────────────────────────────────────────────────

    private TabEntry? FindTabCore(string pageKind, Dictionary<string, string>? pageParams)
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

    // ── Per-view contextual stubs (no-op on the global singleton) ────────────

    void IShellServices.ShowPrompt(string title, string label, string initialValue,
                                   Action<string> onConfirm, Action onCancel) { }

    void IShellServices.ShowConfirmation(string title, string message,
                                         Action onConfirm, Action onCancel) { }

    void IShellServices.RequestRefresh() { }

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

using System.Windows;
using System.Windows.Threading;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;

namespace Nexaflow.Core;

/// <summary>
/// The window a resident Nexaflow (<c>--prestart</c>) keeps ready while it has none open, so the next plain launch shows
/// one at once instead of building it. It is built for the first workspace in the app's idle time — its runtime
/// bootstrapped, its view constructed, its startup tabs opened, then shown out of sight so it lays out and draws — and
/// set aside, hidden, until a launch claims it. What it holds is what that launch would have built, only earlier.
/// <para>
/// It stands only while no window is open: any window opening another way discards it, and the last window closing
/// builds the next. Whatever a person changes — theme, language, workspaces, settings — is changed from a window, so a
/// standby built after the last one closed is never stale against it. A standby runtime counts as no open workspace,
/// records no session and loads no model ahead (<see cref="WorkspaceRuntime.IsStandby"/>).
/// </para>
/// </summary>
internal sealed class StandbyWindow
{
    /// <summary>
    /// How long the app is left alone after its last window closes before the next standby is built: time for what the
    /// close set going to finish, and for a launch straight after it to take the usual path rather than meet a standby
    /// half built.
    /// </summary>
    public static readonly TimeSpan AfterLastWindow = TimeSpan.FromSeconds(2);

    private readonly Func<Workspace?> _target;
    private readonly Func<WorkspaceRuntime, MainWindow> _build;
    private readonly DispatcherTimer _delay;

    private MainWindow? _window;
    private WorkspaceRuntime? _runtime;
    private bool _rendered;

    /// <param name="target">The workspace to build a standby for now, or null while none should stand.</param>
    /// <param name="build">Builds a runtime's window, unshown, wired as every first window is.</param>
    public StandbyWindow(Func<Workspace?> target, Func<WorkspaceRuntime, MainWindow> build)
    {
        _target = target;
        _build  = build;
        _delay  = new DispatcherTimer(DispatcherPriority.ApplicationIdle);
        _delay.Tick += (_, _) =>
        {
            _delay.Stop();
            Build();
        };
    }

    /// <summary>Builds a standby once <paramref name="after"/> has passed and the app is idle, unless one stands already.</summary>
    public void Schedule(TimeSpan after)
    {
        _delay.Stop();
        _delay.Interval = after;
        _delay.Start();
    }

    /// <summary>
    /// The standby window, shown in front, when it stands for <paramref name="workspace"/>; null otherwise. Its runtime
    /// becomes live, as the launch's own would have. <paramref name="drawn"/> says whether it had drawn before it was
    /// shown — a launch that comes while it is still being built shows it all the same.
    /// </summary>
    public MainWindow? Claim(Workspace workspace, out bool drawn)
    {
        drawn = false;
        if (_window is not { } window || _runtime is not { } runtime || !ReferenceEquals(runtime.Workspace, workspace))
            return null;

        _delay.Stop();
        drawn    = _rendered;
        _window  = null;
        _runtime = null;

        WorkspaceManager.Instance.AdoptStandby(runtime);
        Application.Current.MainWindow ??= window;
        window.ShowFromStandby(PlacementOf(workspace));
        return window;
    }

    /// <summary>Discards the standby, if one stands or is due: its window closes and its runtime goes with it, recording nothing.</summary>
    public void Discard()
    {
        _delay.Stop();
        if (_window is not { } window) return;
        _window  = null;
        _runtime = null;
        window.Close();
    }

    private void Build()
    {
        if (_window is not null || _target() is not { } workspace) return;

        var runtime = WorkspaceManager.Instance.CreateStandbyWorkspace(workspace);
        var window  = _build(runtime);
        _window   = window;
        _runtime  = runtime;
        _rendered = false;

        // The app's main window is the one a launch shows, which this is not until one claims it.
        if (ReferenceEquals(Application.Current.MainWindow, window)) Application.Current.MainWindow = null;

        // Set aside once the shell and its startup tabs have both drawn: the tabs open on a timer of their own, after
        // the window is built, so either may come first.
        bool tabsOpen = false;
        void OnTabsOpen(object? sender, EventArgs e)
        {
            runtime.ShellServices!.DefaultTabsOpened -= OnTabsOpen;
            tabsOpen = true;
            SetAsideOnceDrawn();
        }
        void OnRendered(object? sender, EventArgs e)
        {
            window.ContentRendered -= OnRendered;
            if (!ReferenceEquals(_window, window)) return;   // claimed or discarded before it drew
            _rendered = true;
            SetAsideOnceDrawn();
        }
        void SetAsideOnceDrawn()
        {
            if (!tabsOpen || !ReferenceEquals(_window, window) || !_rendered) return;
            // After the tabs' own layout and render, which run ahead of an idle dispatcher.
            window.Dispatcher.InvokeAsync(() =>
            {
                if (ReferenceEquals(_window, window)) window.SetAside();   // not claimed or discarded meanwhile
            }, DispatcherPriority.ApplicationIdle);
        }

        runtime.ShellServices!.DefaultTabsOpened += OnTabsOpen;
        window.ContentRendered += OnRendered;
        window.ShowOutOfSight(PlacementOf(workspace));
    }

    private static WindowPlacement? PlacementOf(Workspace workspace)
        => workspace.LastWindowPlacement is { IsUsable: true } placement ? placement : null;
}

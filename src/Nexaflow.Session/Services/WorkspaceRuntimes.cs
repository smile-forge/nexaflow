using Microsoft.Extensions.Logging;
using Nexaflow.Features.Common;
using System.ComponentModel;

namespace Nexaflow.Session.Services;

/// <summary>
/// The session's live workspaces: at most one runtime per <see cref="Workspace"/>, which every window showing that workspace
/// attaches to. A person moves between workspaces through the day, and sometimes keeps two going at once; so a workspace
/// whose last window goes stays for a while, dormant (<see cref="WorkspaceRuntimeState.Dormant"/>) — its providers back in
/// the pool, its configuration and feature instances kept — and coming back to it within that while is instant, its tabs as
/// they were. After <see cref="ShellConfig.DormantWorkspaceMinutes"/> it ends. A workspace whose background work is still
/// running stays active until the work is done: it is the person's, and it needs the workspace's models.
/// <para>Used on the UI thread; a dormant workspace's time running out is told there.</para>
/// </summary>
public sealed class WorkspaceRuntimes : IDisposable
{
    private readonly WorkspaceRuntimeFactory _factory;
    private readonly IUiThread _ui;
    private readonly Func<TimeSpan> _dormancy;
    private readonly Func<Workspace, bool> _isListed;
    private readonly TimeProvider _time;

    private readonly ILogger<WorkspaceRuntimes> _log;

    /// <summary>Each workspace's one runtime, whatever state it is in short of ended.</summary>
    private readonly Dictionary<Workspace, WorkspaceRuntime> _byWorkspace = new(ReferenceEqualityComparer.Instance);

    /// <summary>The timer ending each dormant runtime.</summary>
    private readonly Dictionary<WorkspaceRuntime, ITimer> _endings = [];

    /// <summary>The runtimes whose last window went while their background work ran, with the jobs they wait on.</summary>
    private readonly Dictionary<WorkspaceRuntime, List<BackgroundTask>> _finishingWork = [];

    /// <param name="factory">Builds each runtime.</param>
    /// <param name="ui">Where a dormant runtime's time running out is told.</param>
    /// <param name="dormancy">How long a dormant runtime stays before it ends — read each time one turns dormant, so a change
    /// applies from the next. Zero ends it at once.</param>
    /// <param name="isListed">Whether a workspace is still one of the saved ones: a workspace deleted while it ran ends with its
    /// last window rather than staying dormant.</param>
    /// <param name="time">The clock the dormant runtimes' time is kept by.</param>
    /// <param name="log">Where a runtime that failed to end cleanly is put on record.</param>
    public WorkspaceRuntimes(WorkspaceRuntimeFactory factory, IUiThread ui, Func<TimeSpan> dormancy, Func<Workspace, bool> isListed,
                             TimeProvider time, ILogger<WorkspaceRuntimes> log)
    {
        _factory  = factory;
        _ui       = ui;
        _dormancy = dormancy;
        _isListed = isListed;
        _time     = time;
        _log      = log;
    }

    /// <summary>Stops every dormant runtime's clock and stops waiting on any background work: the session is ending, and ends
    /// every runtime itself.</summary>
    public void Dispose()
    {
        foreach (var runtime in _endings.Keys.Concat(_finishingWork.Keys).ToList()) StopEnding(runtime);
    }

    /// <summary>
    /// Builds the window a runtime opens into — unshown, opening no tabs — for a tear-off or a "new window". Set by the app,
    /// which knows what a window is; handed to each runtime this registry holds.
    /// </summary>
    public Func<WorkspaceRuntime, IWindowHost>? WindowFactory { get; set; }

    /// <summary>Raised when the last window open in any workspace has closed — the session has no window left. A standby closing
    /// is not that: it was never a window anyone saw.</summary>
    public event EventHandler? LastWindowClosed;

    /// <summary>Raised when a workspace runtime starts, turns active, dormant, or ends.</summary>
    public event EventHandler<WorkspaceRuntime>? StateChanged;

    /// <summary>Every runtime held: starting, active or dormant.</summary>
    public IReadOnlyCollection<WorkspaceRuntime> All => _byWorkspace.Values;

    /// <summary>True when a window is open in any workspace.</summary>
    public bool AnyWindowsOpen => _byWorkspace.Values.Any(HasWindows);

    /// <summary>The first workspace runtime with a window open, or null when none has one.</summary>
    public WorkspaceRuntime? FirstWithWindows => _byWorkspace.Values.FirstOrDefault(HasWindows);

    /// <summary>The workspaces with a window open.</summary>
    public IEnumerable<Workspace> WithWindows => _byWorkspace.Values.Where(HasWindows).Select(r => r.Workspace);

    /// <summary><paramref name="workspace"/>'s runtime, whatever state it is in short of ended; null when it has none.</summary>
    public WorkspaceRuntime? Find(Workspace workspace) => _byWorkspace.GetValueOrDefault(workspace);

    /// <summary>Whether a window shows <paramref name="workspace"/>.</summary>
    public bool HasWindows(Workspace workspace) => Find(workspace) is { } runtime && HasWindows(runtime);

    /// <summary>
    /// <paramref name="workspace"/>'s runtime, for a window to attach to: the one already running, the dormant one resumed, or
    /// a new one started.
    /// </summary>
    public WorkspaceRuntime Open(Workspace workspace)
    {
        if (Find(workspace) is { } held)
        {
            if (held.State == WorkspaceRuntimeState.Dormant)
            {
                StopEnding(held);
                held.Resume();
            }
            return held;
        }
        return Hold(_factory.Create(workspace));
    }

    /// <summary>A runtime for <paramref name="workspace"/> standing ready for a launch that has not come yet: its workspace's one
    /// runtime, though no window shows it, until a launch adopts it (<see cref="Adopt"/>) or it is set aside.</summary>
    public WorkspaceRuntime OpenStandby(Workspace workspace) => Hold(_factory.CreateStandby(workspace));

    /// <summary>A launch adopted the standby runtime: its models are weighed for loading ahead, as any opened runtime's are.</summary>
    public void Adopt(WorkspaceRuntime standby) => standby.Adopted();

    /// <summary>Has every runtime of <paramref name="workspace"/> take up its providers again: its grid changed under it.</summary>
    internal void Reacquire(Workspace workspace)
    {
        if (Find(workspace) is { State: not WorkspaceRuntimeState.Dormant } runtime) runtime.AcquireProviders();
    }

    /// <summary>
    /// Builds <paramref name="workspace"/>'s runtime afresh — its saved configuration changed in a way a running one cannot take
    /// up — and moves its windows' work onto it: the focused window's tabs reopen, pane for pane, in a replacement window opened
    /// where it was, shown before the old windows close so the workspace never goes window-less. A runtime with no window
    /// simply ends; the next window builds it afresh.
    /// </summary>
    internal void Restart(Workspace workspace)
    {
        if (Find(workspace) is not { } old) return;

        var acting = old.ShellServices.FocusedWindow;
        if (acting is null || WindowFactory is null)
        {
            _ = End(old);
            return;
        }

        var snapshot  = acting.CaptureTabLayout();
        var placement = acting.IntendedPlacement;

        _byWorkspace.Remove(workspace);
        var fresh = Hold(_factory.Create(workspace));

        var freshHost = WindowFactory(fresh);
        if (placement is not null) freshHost.OpenAt(placement);
        freshHost.Show();

        // The old runtime is no longer the workspace's: its last window closing ends it.
        old.ShellServices.CloseAllWindows();

        fresh.ShellServices.RestoreTabLayout(snapshot);
    }

    /// <summary>Ends <paramref name="workspace"/>'s runtime now, closing any window still showing it — the workspace is being
    /// deleted.</summary>
    public void Close(Workspace workspace)
    {
        if (Find(workspace) is not { } runtime) return;
        _byWorkspace.Remove(workspace);
        runtime.ShellServices.CloseAllWindows();
        _ = End(runtime);
    }

    /// <summary>Every tab open in a workspace.</summary>
    internal IEnumerable<Page> OpenTabs() => _byWorkspace.Values.SelectMany(runtime => runtime.ShellServices.GetOpenTabs());

    private WorkspaceRuntime Hold(WorkspaceRuntime runtime)
    {
        _byWorkspace[runtime.Workspace] = runtime;
        runtime.ShellServices.WindowsChanged += OnWindowsChanged;
        runtime.StateChanged += OnStateChanged;
        if (WindowFactory is { } windows) runtime.ShellServices.CreateWindowFactory = () => windows(runtime);
        StateChanged?.Invoke(this, runtime);
        return runtime;
    }

    private void OnStateChanged(object? sender, WorkspaceRuntimeState state)
    {
        if (sender is WorkspaceRuntime runtime) StateChanged?.Invoke(this, runtime);
    }

    private void OnWindowsChanged(object? sender, EventArgs e)
    {
        if (sender is not ShellServices shell || _byWorkspace.Values.FirstOrDefault(r => ReferenceEquals(r.ShellServices, shell)) is not { } runtime)
        {
            // A runtime no longer the workspace's — restarted or closed — ends with its last window.
            if (sender is ShellServices { HasWindows: false } orphan && _factory.Live.FirstOrDefault(r => ReferenceEquals(r.ShellServices, orphan)) is { } replaced)
                _ = End(replaced);
            return;
        }

        if (runtime.ShellServices.HasWindows)
        {
            StopEnding(runtime);
            runtime.MarkActive();
            return;
        }

        var standby = runtime.IsStandby;
        if (standby || !_isListed(runtime.Workspace)) _ = End(runtime);
        else Rest(runtime);

        if (!standby && !AnyWindowsOpen) LastWindowClosed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The last window of <paramref name="runtime"/> went: it turns dormant once its background work is done.</summary>
    private void Rest(WorkspaceRuntime runtime)
    {
        var running = _factory.RunningJobsOf(runtime);
        if (running.Count > 0)
        {
            _finishingWork[runtime] = running;
            foreach (var job in running) job.PropertyChanged += OnJobChanged;
            return;
        }

        runtime.Rest();
        var dormancy = _dormancy();
        if (dormancy <= TimeSpan.Zero) { _ = End(runtime); return; }

        _endings[runtime] = _time.CreateTimer(_ => _ui.Post(() => OnDormancyOver(runtime)), null, dormancy, Timeout.InfiniteTimeSpan);
    }

    /// <summary>A job a window-less runtime waits on moved on: when none of its work runs any more, it turns dormant.</summary>
    private void OnJobChanged(object? sender, PropertyChangedEventArgs e) => _ui.Post(() =>
    {
        foreach (var (runtime, jobs) in _finishingWork.ToList())
        {
            if (jobs.Any(job => job.Status == Models.TaskStatus.Running)) continue;

            foreach (var job in jobs) job.PropertyChanged -= OnJobChanged;
            _finishingWork.Remove(runtime);
            if (!HasWindows(runtime) && Find(runtime.Workspace) == runtime) Rest(runtime);
        }
    });

    private void OnDormancyOver(WorkspaceRuntime runtime)
    {
        if (runtime.State != WorkspaceRuntimeState.Dormant) return;
        _ = End(runtime);
    }

    private void StopEnding(WorkspaceRuntime runtime)
    {
        if (_endings.Remove(runtime, out var timer)) timer.Dispose();
        if (_finishingWork.Remove(runtime, out var jobs))
            foreach (var job in jobs) job.PropertyChanged -= OnJobChanged;
    }

    /// <summary>Ends <paramref name="runtime"/> and lets it go. A location that fails to close is logged rather than thrown into
    /// nobody's hands.</summary>
    private async Task End(WorkspaceRuntime runtime)
    {
        StopEnding(runtime);
        if (Find(runtime.Workspace) == runtime) _byWorkspace.Remove(runtime.Workspace);
        runtime.ShellServices.WindowsChanged -= OnWindowsChanged;

        try { await runtime.DisposeAsync(); }
        catch (Exception ex) { _log.LogWarning(ex, "ending the workspace runtime '{Workspace}'", runtime.Name); }
        finally
        {
            runtime.StateChanged -= OnStateChanged;
        }
    }

    private static bool HasWindows(WorkspaceRuntime runtime) => runtime.ShellServices.HasWindows;
}

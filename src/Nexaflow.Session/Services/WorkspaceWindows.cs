namespace Nexaflow.Session.Services;

/// <summary>
/// Which workspace each window shows: a window moved to another workspace, a workspace deleted from under the windows on it,
/// a copy of one, and a window's tabs kept as a workspace's default.
/// <para>
/// A workspace that leaves the saved list however it does (Options, say) takes its windows to the first one left, so no
/// window is left showing a workspace that is gone. That is followed here once for every window, so a window follows nothing
/// of the list itself.
/// </para>
/// </summary>
public sealed class WorkspaceWindows : IDisposable
{
    private readonly WorkspaceManager _saved;
    private readonly WorkspaceRuntimes _runtimes;
    private readonly IUiThread _ui;

    /// <param name="saved">The saved workspaces.</param>
    /// <param name="runtimes">The live ones, whose windows move.</param>
    /// <param name="ui">The thread the windows run on, where a removal from the list is acted on.</param>
    public WorkspaceWindows(WorkspaceManager saved, WorkspaceRuntimes runtimes, IUiThread ui)
    {
        _saved    = saved;
        _runtimes = runtimes;
        _ui       = ui;
        _saved.WorkspacesRefreshed += OnListChanged;
    }

    public void Dispose() => _saved.WorkspacesRefreshed -= OnListChanged;

    /// <summary>
    /// Moves <paramref name="window"/> from the runtime it shows, <paramref name="from"/>, to <paramref name="to"/>'s — opened if
    /// it is not live. The window joins the new runtime before it leaves the old, so the app is never without one; leaving closes
    /// its tabs, and it starts again on the workspace's own (<see cref="IWindowHost.Rejoin"/>). Returns the runtime it shows now.
    /// </summary>
    public WorkspaceRuntime Move(IWindowHost window, WorkspaceRuntime from, Workspace to)
    {
        var joined = _runtimes.Open(to);
        if (ReferenceEquals(from, joined)) return from;

        joined.ShellServices.RegisterWindow(window);
        from.ShellServices.UnregisterWindow(window);
        window.Rejoin(from, joined);

        joined.ShellServices.SetFocused(window);
        joined.ShellServices.OpenWorkspaceTabs();
        return joined;
    }

    /// <summary>Whether <paramref name="workspace"/> may be deleted: there is another to show instead.</summary>
    public bool CanDelete(Workspace workspace) => _saved.Workspaces.Any(other => !ReferenceEquals(other, workspace));

    /// <summary>
    /// Deletes <paramref name="workspace"/>. The window that asked, when it shows it, moves to the first other workspace before
    /// the delete; any other window on it closes as its runtime ends now, rather than resting for a workspace that is gone.
    /// False, and nothing done, for the last workspace.
    /// </summary>
    public bool Delete(Workspace workspace, IWindowHost asking)
    {
        var fallback = _saved.Workspaces.FirstOrDefault(other => !ReferenceEquals(other, workspace));
        if (fallback is null) return false;

        if (_runtimes.Find(workspace) is { } runtime && runtime.ShellServices.Windows.Contains(asking))
            Move(asking, runtime, fallback);

        _runtimes.Close(workspace);
        _saved.DeleteWorkspace(workspace);
        return true;
    }

    /// <summary>A copy of <paramref name="source"/>, under a name no workspace has, saved in the list at once.</summary>
    public Workspace CopyOf(Workspace source)
    {
        var copy = _saved.CloneWorkspace(source, _saved.UniqueWorkspaceName($"{source.Name} copy"));
        _saved.SaveWorkspaces();
        return copy;
    }

    /// <summary>Keeps the tabs <paramref name="window"/> has open, pane by pane, as the tabs <paramref name="workspace"/> opens
    /// with. Returns how many there are.</summary>
    public int UseAsDefaultTabs(Workspace workspace, IWindowHost window)
    {
        var tabs = window.CaptureTabLayout().ToList();
        workspace.DefaultTabs = tabs;
        _saved.SaveWorkspaces();
        return tabs.Count;
    }

    private void OnListChanged(object? sender, EventArgs e) => _ui.Run(() =>
    {
        foreach (var runtime in _runtimes.All.ToList())
        {
            if (_saved.Workspaces.Contains(runtime.Workspace)) continue;
            if (_saved.Workspaces.FirstOrDefault() is not { } fallback) return;

            foreach (var window in runtime.ShellServices.Windows.ToList())
                Move(window, runtime, fallback);
        }
    });
}

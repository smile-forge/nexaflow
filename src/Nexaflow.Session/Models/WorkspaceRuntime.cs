using Microsoft.Extensions.Logging;
using Nexaflow.IO.Common.Locations;

namespace Nexaflow.Session.Models;

/// <summary>
/// A live workspace: the one or more window frames (<see cref="MainWindow"/>) running one <see cref="Workspace"/>, and the
/// services they share — the window and tab registry (<see cref="ShellServices"/>), the assistant (<see cref="Assistant"/>),
/// the provider instances it acquired from the session's pool (deduplicated process-wide by <see cref="ProviderManager"/>),
/// its file system and locations, and the feature instances built for it (<see cref="Features"/>). Built and started by
/// <see cref="WorkspaceRuntimeFactory"/>; <see cref="DisposeAsync"/> ends it, letting go of everything it holds in order.
/// </summary>
public sealed class WorkspaceRuntime : IAsyncDisposable
{
    /// <summary>The session's side of this runtime: the pools it acquires from and returns to.</summary>
    private readonly WorkspaceRuntimeFactory _session;

    /// <summary>Where the runtime marks its startup milestones.</summary>
    private readonly ILogger<WorkspaceRuntime> _log;

    private bool _ended;

    public Workspace Workspace { get; private set; }

    public string Name  => Workspace.Name;
    public string Color => Workspace.Color;
    public string Icon  => Workspace.Icon;

    /// <summary>The workspace's shared ability grid (delegated for code that reads <c>ws.AiConfig</c>).</summary>
    public AiConfig AiConfig => Workspace.AiConfig;

    /// <summary>The workspace's assistant persona (name + system prompt).</summary>
    public AiPersonaConfig Persona => Workspace.Persona;

    /// <summary>Provider instances acquired from the session's pool for the workspace's configs and assigned columns; null
    /// before the runtime starts and after it ends.</summary>
    public ProviderSet? Providers { get; private set; }

    /// <summary>The workspace's assistant, made for it by the session's assistant factory: what features are given as their
    /// <see cref="IAIService"/>, and what this runtime hands its providers.</summary>
    public IWorkspaceAssistant Assistant { get; }

    /// <summary>The window and tab registry this runtime's windows share.</summary>
    public ShellServices ShellServices { get; }

    /// <summary>
    /// This runtime's file system: the shared <see cref="IO.Common.VirtualFileSystem"/> plus the remote locations this runtime
    /// has connected, which the process-wide instance cannot see. Features are given it as
    /// <see cref="IO.Common.IVirtualFileSystem"/>.
    /// </summary>
    public LocationFileSystem Vfs { get; }

    /// <summary>
    /// The workspace's connections and the ones found on this machine (each WSL distro) as this runtime has mounted them on
    /// <see cref="Vfs"/>, each with its live session from the runtime's own pool — signed in from what Credential Manager
    /// holds, with nobody to ask for anything more until the shell has a prompt.
    /// </summary>
    public RuntimeLocations Locations { get; }

    /// <summary>The feature instances built for this runtime, let go of when its services are renewed and when it ends.</summary>
    public FeatureScope Features { get; }

    /// <summary>The session's feature system, which builds this runtime's feature instances (<see cref="Features"/>) and opens its
    /// pages.</summary>
    public FeatureManager FeatureManager { get; }

    /// <summary>The session's provider pool, which <see cref="Providers"/> is acquired from and the workspace's provider configs are
    /// read through.</summary>
    internal ProviderManager ProviderPool => _session.Providers;

    /// <summary>
    /// True while this runtime stands ready for a launch that has not come yet (<see cref="StandbyWindow"/>): its window is
    /// built but not shown, it is not counted among the open workspaces, its models are not weighed for
    /// loading ahead, and its window closing records no session. Cleared when a launch adopts it.
    /// </summary>
    public bool IsStandby { get; internal set; }

    /// <summary>Where the runtime is in its life (<see cref="WorkspaceRuntimeState"/>).</summary>
    public WorkspaceRuntimeState State { get; private set; } = WorkspaceRuntimeState.Starting;

    /// <summary>Raised when the runtime turns active, dormant, or ends — what reacts to a person entering or leaving a workspace
    /// listens.</summary>
    public event EventHandler<WorkspaceRuntimeState>? StateChanged;

    /// <summary>The session the runtime left when it turned dormant, for the first window that resumes it; null otherwise.</summary>
    private IReadOnlyList<DefaultTabDescriptor>? _resumedSession;

    internal WorkspaceRuntime(Workspace workspace, WorkspaceRuntimeFactory session, bool standby)
    {
        Workspace     = workspace;
        _session      = session;
        _log          = session.Session.Logs.CreateLogger<WorkspaceRuntime>();
        IsStandby     = standby;
        Vfs           = new LocationFileSystem(session.Session.FileSystem);
        Locations     = new RuntimeLocations(Vfs, session.NewLocationSessions(), session.Session.Discovered);
        Features      = new FeatureScope(workspace.Name);
        FeatureManager = session.Session.Features;
        Assistant     = session.Session.NewAssistant(this);
        ShellServices = new ShellServices(this, session.Session);
    }

    /// <summary>Acquires the workspace's providers and mounts its connections: the runtime is ready for its first window.</summary>
    internal void Start()
    {
        AcquireProviders();
        _session.AwaitFirstDiscovery();
        Locations.Follow(Workspace.Connections);
    }

    /// <summary>
    /// Acquires a fresh provider set for the workspace's current configs and assigned grid columns and swaps it in —
    /// acquire before release, so unchanged instances stay and dropped ones are cooled — then registers its execution
    /// instances with the assistant, keyed by column id, and has the residency weigh each for loading ahead.
    /// </summary>
    internal void AcquireProviders()
    {
        var pool  = _session.Providers;
        var fresh = pool.AcquireProviderSet(Workspace.ProviderConfigs, WorkspaceManager.AssignedColumns(Workspace.AiConfig));

        _log.StartupMilestone("Workspace.ProvidersAcquired");
        if (WorkspaceManager.FoldInLocalColumns(Workspace, fresh, pool.Assets))
        {
            var again = pool.AcquireProviderSet(Workspace.ProviderConfigs, WorkspaceManager.AssignedColumns(Workspace.AiConfig));
            pool.ReleaseProviderSet(fresh);
            fresh = again;
        }
        _log.StartupMilestone("Workspace.LocalColumnsFolded");

        var old   = Providers;
        Providers = fresh;
        pool.ReleaseProviderSet(old);
        // Nothing is loaded here: the residency weighs each assigned model for loading ahead once the window is up. A standby
        // runtime's are weighed when a launch adopts it, so a window nobody has asked for yet loads nothing.
        if (!IsStandby) pool.Residency?.Preload(fresh.Execution.Values);

        Assistant.UseProviders(fresh.Execution);
        RaiseProvidersChanged();
    }

    /// <summary>Has a launch that adopted this standby runtime weigh its assigned models for loading ahead.</summary>
    internal void Adopted()
    {
        IsStandby = false;
        if (Providers is { } providers) _session.Providers.Residency?.Preload(providers.Execution.Values);
    }

    /// <summary>A window shows the runtime, or its background work runs.</summary>
    internal void MarkActive() => Enter(WorkspaceRuntimeState.Active);

    /// <summary>
    /// Turns the runtime dormant: no window shows it and none of its work runs. Its provider set goes back to the pool — a local
    /// model nothing else uses is let go after the residency's own idle time — and its assistant stops listening to them; its
    /// configuration, assistant, file system and feature instances stay, for <see cref="Resume"/>.
    /// </summary>
    internal void Rest()
    {
        Assistant.ReleaseProviders();
        _session.Providers.ReleaseProviderSet(Providers);
        Providers = null;
        RaiseProvidersChanged();
        Enter(WorkspaceRuntimeState.Dormant);
    }

    /// <summary>Takes a dormant runtime up again: its providers acquired afresh — at once where another workspace still holds them —
    /// and the session it left kept for the window that resumes it.</summary>
    internal void Resume()
    {
        _resumedSession = Workspace.LastSessionTabs;
        AcquireProviders();
        Enter(WorkspaceRuntimeState.Active);
    }

    /// <summary>The session the runtime left when it turned dormant, once: the first window that resumes it reopens it.</summary>
    internal IReadOnlyList<DefaultTabDescriptor>? TakeResumedSession()
    {
        var session = _resumedSession;
        _resumedSession = null;
        return session;
    }

    private void Enter(WorkspaceRuntimeState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, state);
    }

    /// <summary>
    /// Ends the runtime, letting go of what it holds in the order nothing still running can reach what is gone: its
    /// background jobs stop, its feature instances go, its assistant lets go of the providers it listened to, its provider
    /// set returns to the pool (cooling any model nothing else uses), its file watches close, and its locations unmount and
    /// close their sessions. All but the last happens before this returns, on the calling thread — the UI thread, where the
    /// watches are; the task ends when the sessions have closed, and needs no thread of the caller's for it. Ending it again
    /// does nothing.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_ended) return ValueTask.CompletedTask;
        _ended = true;

        _session.StopJobsOf(this);
        Features.Close();
        Assistant.Dispose();
        _session.Providers.ReleaseProviderSet(Providers);
        Providers = null;
        ShellServices.Dispose();
        _session.Ended(this);
        Enter(WorkspaceRuntimeState.Ended);
        return Locations.DisposeAsync();
    }

    /// <summary>
    /// Raised when the runtime's providers are registered anew, or one of them becomes available or stops being — a
    /// model finishing its download, say. What depends on a kind of model being there (the mic) listens. Raised on
    /// whichever thread made the change.
    /// </summary>
    public event EventHandler? ProvidersChanged;

    internal void RaiseProvidersChanged() => ProvidersChanged?.Invoke(this, EventArgs.Empty);
}

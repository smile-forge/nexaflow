using Nexaflow.Core.Models;
using Nexaflow.Core.Services.Agent;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services;

/// <summary>
/// Builds every <see cref="WorkspaceRuntime"/>, and is each one's link to what outlives it: the session's provider pool,
/// background tasks, file system and the locations found on this machine. A runtime comes out wired — its
/// <see cref="AIService"/>, <see cref="ShellServices"/>, file system and <see cref="FeatureScope"/> made with it — and, from
/// <see cref="Create"/>, started: its providers acquired and its connections mounted.
/// </summary>
public sealed class WorkspaceRuntimeFactory
{
    /// <summary>How long a runtime waits for the machine's first look for locations before mounting without it.</summary>
    private static readonly TimeSpan DiscoveryPatience = TimeSpan.FromSeconds(2);

    private readonly BackgroundActivityManager? _tasks;
    private readonly Action<NotificationItem> _notify;
    private readonly LocationProviderRegistry _locationProviders;
    private readonly ICredentialSource _credentials;
    private readonly Action _ensureLocationProviders;

    /// <param name="providers">The session's pool every runtime acquires its provider instances from.</param>
    /// <param name="tasks">Where each runtime's assistant runs its slow work; null where nothing runs in the background.</param>
    /// <param name="notify">How a runtime's background work tells the user it ended.</param>
    /// <param name="fileSystem">The session's file system, under each runtime's own.</param>
    /// <param name="locationProviders">The backends a runtime's connections are reached through.</param>
    /// <param name="discovered">The locations found on this machine, mounted in every runtime.</param>
    /// <param name="credentials">What signs a runtime's connections in.</param>
    /// <param name="ensureLocationProviders">Loads every backend that reaches a location, before the first look for them.</param>
    public WorkspaceRuntimeFactory(
        ProviderManager providers,
        BackgroundActivityManager? tasks,
        Action<NotificationItem> notify,
        IVirtualFileSystem fileSystem,
        LocationProviderRegistry locationProviders,
        DiscoveredLocations discovered,
        ICredentialSource credentials,
        Action ensureLocationProviders)
    {
        Providers                = providers;
        _tasks                   = tasks;
        _notify                  = notify;
        FileSystem               = fileSystem;
        _locationProviders       = locationProviders;
        Discovered               = discovered;
        _credentials             = credentials;
        _ensureLocationProviders = ensureLocationProviders;
    }

    internal ProviderManager Providers { get; }

    internal IVirtualFileSystem FileSystem { get; }

    internal DiscoveredLocations Discovered { get; }

    /// <summary>The background tasks a runtime's pages report their work to.</summary>
    internal IBackgroundActivityManager? Activities => _tasks;

    /// <summary>A started runtime for <paramref name="workspace"/>: its providers acquired, its connections mounted, its models
    /// weighed for loading ahead.</summary>
    public WorkspaceRuntime Create(Workspace workspace) => Started(workspace, standby: false);

    /// <summary>
    /// A started runtime for a launch that has not come yet (<see cref="StandbyWindow"/>): not live, so nothing that asks which
    /// workspaces are open counts it, and its models left unweighed until a launch adopts it.
    /// </summary>
    internal WorkspaceRuntime CreateStandby(Workspace workspace) => Started(workspace, standby: true);

    /// <summary>A wired runtime that has acquired nothing and mounted nothing — for what works with its services alone.</summary>
    internal WorkspaceRuntime CreateUnstarted(Workspace workspace) => new(workspace, this, standby: false);

    private WorkspaceRuntime Started(Workspace workspace, bool standby)
    {
        workspace.EnsureSharedServicesLoaded();
        var runtime = new WorkspaceRuntime(workspace, this, standby);
        runtime.Start();
        return runtime;
    }

    /// <summary>The assistant for <paramref name="runtime"/>'s workspace, its slow work run on the session's background tasks.</summary>
    internal AIService NewAiService(WorkspaceRuntime runtime)
    {
        var jobs    = _tasks is { } tasks ? new BackgroundJobs(tasks, runtime, _notify) : null;
        var service = new AIService(runtime, runtime.Workspace.ConversationsDir, jobs);
        service.LoadAbilityConfig(runtime.Workspace.AiConfig);
        return service;
    }

    /// <summary>A pool of sessions to the locations one runtime connects to, signed in from what the session holds.</summary>
    internal LocationSessionPool NewLocationSessions() => new(_locationProviders, _credentials);

    /// <summary>Stops <paramref name="runtime"/>'s background jobs: the models they run on are going.</summary>
    internal void StopJobsOf(WorkspaceRuntime runtime) => _tasks?.StopJobsOf(runtime);

    /// <summary>
    /// Lets the machine's first look for locations finish before a runtime mounts — a registry read for WSL — so the tabs it
    /// reopens find a saved distro's folder mounted. A look slower than <see cref="DiscoveryPatience"/> is mounted when it
    /// finishes instead, and every later look has already finished.
    /// </summary>
    internal void AwaitFirstDiscovery()
    {
        _ensureLocationProviders();
        Discovered.StartAsync().Wait(DiscoveryPatience);
    }
}

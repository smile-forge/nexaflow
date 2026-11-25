using Nexaflow.Core.Models;
using Nexaflow.Core.Services.Agent;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services;

/// <summary>
/// Builds every <see cref="WorkspaceRuntime"/>, and is each one's link to what outlives it: the session's provider pool,
/// background tasks, file system, watches and the locations found on this machine (<see cref="RuntimeSessionServices"/>). A
/// runtime comes out wired — its <see cref="AIService"/>, <see cref="ShellServices"/>, file system and
/// <see cref="FeatureScope"/> made with it — and, from <see cref="Create"/>, started: its providers acquired and its
/// connections mounted. The factory knows every runtime it built that has not ended, so the session can end them all.
/// </summary>
public sealed class WorkspaceRuntimeFactory(RuntimeSessionServices session)
{
    /// <summary>How long a runtime waits for the machine's first look for locations before mounting without it.</summary>
    private static readonly TimeSpan DiscoveryPatience = TimeSpan.FromSeconds(2);

    private readonly HashSet<WorkspaceRuntime> _live = [];
    private readonly Lock _liveGate = new();

    /// <summary>What the runtimes share with the session.</summary>
    internal RuntimeSessionServices Session { get; } = session;

    internal ProviderManager Providers => Session.Providers;

    /// <summary>A started runtime for <paramref name="workspace"/>: its providers acquired, its connections mounted, its models
    /// weighed for loading ahead.</summary>
    public WorkspaceRuntime Create(Workspace workspace) => Started(workspace, standby: false);

    /// <summary>
    /// A started runtime for a launch that has not come yet (<see cref="StandbyWindow"/>): not live, so nothing that asks which
    /// workspaces are open counts it, and its models left unweighed until a launch adopts it.
    /// </summary>
    internal WorkspaceRuntime CreateStandby(Workspace workspace) => Started(workspace, standby: true);

    /// <summary>A wired runtime that has acquired nothing and mounted nothing — for what works with its services alone.</summary>
    internal WorkspaceRuntime CreateUnstarted(Workspace workspace) => Built(workspace, standby: false);

    /// <summary>Every runtime built here that has not ended.</summary>
    internal IReadOnlyList<WorkspaceRuntime> Live
    {
        get { lock (_liveGate) return [.. _live]; }
    }

    /// <summary>Ends every runtime that has not ended: the session is ending.</summary>
    internal async Task EndAllAsync()
    {
        foreach (var runtime in Live) await runtime.DisposeAsync();
    }

    private WorkspaceRuntime Started(Workspace workspace, bool standby)
    {
        workspace.EnsureSharedServicesLoaded();
        var runtime = Built(workspace, standby);
        runtime.Start();
        return runtime;
    }

    private WorkspaceRuntime Built(Workspace workspace, bool standby)
    {
        var runtime = new WorkspaceRuntime(workspace, this, standby);
        lock (_liveGate) _live.Add(runtime);
        return runtime;
    }

    /// <summary><paramref name="runtime"/> has ended.</summary>
    internal void Ended(WorkspaceRuntime runtime)
    {
        lock (_liveGate) _live.Remove(runtime);
    }

    /// <summary>The assistant for <paramref name="runtime"/>'s workspace, its slow work run on the session's background tasks.</summary>
    internal AIService NewAiService(WorkspaceRuntime runtime)
    {
        var jobs    = Session.Tasks is { } tasks ? new BackgroundJobs(tasks, runtime, Session.Notify) : null;
        var service = new AIService(runtime, Session.Features, runtime.Workspace.ConversationsDir, Session.HelpTools, jobs);
        service.LoadAbilityConfig(runtime.Workspace.AiConfig);
        return service;
    }

    /// <summary>A pool of sessions to the locations one runtime connects to, signed in from what the session holds.</summary>
    internal LocationSessionPool NewLocationSessions() => new(Session.LocationProviders, Session.Credentials);

    /// <summary>Stops <paramref name="runtime"/>'s background jobs: the models they run on are going.</summary>
    internal void StopJobsOf(WorkspaceRuntime runtime) => Session.Tasks?.StopJobsOf(runtime);

    /// <summary><paramref name="runtime"/>'s background jobs still running.</summary>
    internal List<BackgroundTask> RunningJobsOf(WorkspaceRuntime runtime)
        => Session.Tasks?.Tasks.Where(task => ReferenceEquals(task.Workspace, runtime) && task.Status == Models.TaskStatus.Running).ToList() ?? [];

    /// <summary>
    /// Lets the session's first look for locations finish before a runtime mounts — a registry read for WSL — so the tabs it
    /// reopens find a saved distro's folder mounted. A look slower than <see cref="DiscoveryPatience"/> is mounted when it
    /// finishes instead, and every later runtime finds it finished.
    /// </summary>
    internal void AwaitFirstDiscovery() => Session.FirstDiscovery().Wait(DiscoveryPatience);
}

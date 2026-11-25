using Microsoft.Extensions.Logging;

namespace Nexaflow.Core.Services.Logging;

/// <summary>
/// The milestones of a launch, as every part of the shell logs them: one entry each, <c>startup: {name}</c>, which the session's
/// log keeps as the steps of its startup and the opt-in profiler times (<see cref="StartupTimings"/>), each knowing them by their
/// event id.
/// </summary>
public static class StartupMilestones
{
    /// <summary>A milestone of the launch: the event the profiler times.</summary>
    public static readonly EventId Milestone = new(1001, "StartupMilestone");

    /// <summary>A window asked of a warmed resident app — the profiler's click-to-window clock starts here.</summary>
    public static readonly EventId WindowRequested = new(1002, "StartupWindowRequested");

    /// <summary>The name a milestone's entry carries it under.</summary>
    public const string NameField = "Milestone";

    /// <summary>Logs the milestone <paramref name="name"/>.</summary>
    public static void StartupMilestone(this ILogger log, string name) =>
        log.LogInformation(Milestone, "startup: {Milestone}", name);

    /// <summary>Logs that a resident app was asked for a window — the IPC "open a window" signal.</summary>
    public static void StartupWindowRequested(this ILogger log) =>
        log.LogInformation(WindowRequested, "startup: {Milestone}", "WindowRequested");
}

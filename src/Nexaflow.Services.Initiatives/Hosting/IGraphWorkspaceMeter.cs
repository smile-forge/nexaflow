using System;

namespace Nexaflow.Services.Initiatives.Hosting;

/// <summary>
/// Told what a <see cref="GraphWorkspace"/> spent its time on, for a holder that accounts for it — the resident process's
/// log, so that a command that took long says whether it waited on the lock, loaded, caught up, or none of those.
/// <para>
/// Called on whichever thread did the work, a request's or the pool's, and some of it under the workspace's lock: an
/// implementation is quick and does not throw.
/// </para>
/// </summary>
public interface IGraphWorkspaceMeter
{
    /// <summary>A caller waited <paramref name="waited"/> for the lock before it could catch the workspace up or change it.</summary>
    void Waited(GraphWorkspace workspace, TimeSpan waited);

    /// <summary>The archive was read — or, when <paramref name="seeded"/>, another tree's graph was started from.</summary>
    void Loaded(GraphWorkspace workspace, bool seeded, TimeSpan took);

    /// <summary>A catch-up — <paramref name="full"/> when it judged every file, not only what the watcher reported — found
    /// <paramref name="caught"/>.</summary>
    void CaughtUp(GraphWorkspace workspace, GraphCatchUp caught, bool full, TimeSpan took);

    /// <summary>The archive was written.</summary>
    void Saved(GraphWorkspace workspace, TimeSpan took);
}

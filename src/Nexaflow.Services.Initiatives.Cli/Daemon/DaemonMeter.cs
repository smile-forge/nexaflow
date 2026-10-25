using System;
using System.Globalization;
using System.IO;
using Nexaflow.Services.Initiatives.Hosting;

namespace Nexaflow.Services.Initiatives.Cli.Daemon;

/// <summary>
/// What the graph spent a command's time on, in the log beside the command: the lock it waited for, the archive it
/// loaded, the catch-up and what that read, the archive written.
/// <para>
/// Without it the log held a command's wait for its turn and its run time, and nothing between. A read stuck behind a
/// catch-up and a read that was simply long both said "waited 0.00s", and the one wait that mattered was invisible.
/// What happens on the pool — a catch-up the watcher started, a save — is logged against no ticket.
/// </para>
/// </summary>
internal sealed class DaemonMeter : IGraphWorkspaceMeter
{
    /// <summary>A catch-up that found nothing is worth a line only when it took this long to find it.</summary>
    private static readonly TimeSpan Noticeable = TimeSpan.FromMilliseconds(50);

    public void Waited(GraphWorkspace workspace, TimeSpan waited) =>
        Say("lock", workspace, $"waited {Seconds(waited)} for the graph's lock");

    public void Loaded(GraphWorkspace workspace, bool seeded, TimeSpan took) =>
        Say("load", workspace, seeded ? $"started from the main checkout's graph in {Seconds(took)}"
                                      : $"read the archive in {Seconds(took)}");

    public void CaughtUp(GraphWorkspace workspace, GraphCatchUp caught, bool full, TimeSpan took)
    {
        if (!full && caught.Read.Count == 0 && caught.Dropped.Count == 0 && took < Noticeable) return;

        Say("caughtup", workspace, $"{(full ? "every file judged" : "reported paths judged")}, "
                                 + $"{caught.Read.Count} read, {caught.Dropped.Count} dropped, in {Seconds(took)}");
    }

    public void Saved(GraphWorkspace workspace, TimeSpan took) =>
        Say("saved", workspace, $"wrote the archive in {Seconds(took)}");

    /// <summary>A working tree as the log names it: its folder, or <c>main</c> for the main checkout.</summary>
    internal static string TreeName(string? codeRoot) =>
        codeRoot is { Length: > 0 } ? Path.GetFileName(Path.TrimEndingDirectorySeparator(codeRoot)) : "main";

    private static void Say(string what, GraphWorkspace workspace, string detail) =>
        DaemonLog.Say(RequestScope.Ticket ?? "-", what, $"{TreeName(workspace.CodeRoot)}  {detail}");

    private static string Seconds(TimeSpan span) =>
        string.Create(CultureInfo.InvariantCulture, $"{span.TotalSeconds:F2}s");
}

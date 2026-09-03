using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Collections.Generic;

namespace Nexaflow.Services.Initiatives.Hosting.Ipc;

/// <summary>
/// What the daemon has been asked to do and where each of those has got to.
/// <para>
/// It exists so that a caller waiting on a command can ask after it and be answered <i>now</i>, whatever the
/// work itself is doing. That is the entire requirement, and it is what shapes this: the state is a
/// dictionary and nothing else, so answering never blocks, never takes the lock the work holds, and cannot
/// itself be the thing that hangs. A status that queued behind the work it reports on would take exactly as
/// long as the question it was asked about, and arrive once the answer no longer mattered.
/// </para>
/// <para>
/// Kept apart from the pipe server on purpose: this is a small state machine with awkward edges — work that
/// is queued rather than running, work that finished a moment ago, work nobody has heard of — and those are
/// worth testing without a process, a pipe or a graph anywhere near them.
/// </para>
/// </summary>
/// <param name="remember">
/// How long a finished ticket stays answerable. A client's last status poll and the answer it is waiting for
/// cross on the wire routinely, and forgetting a ticket the instant it finishes would turn that ordinary race
/// into "no such command" — the one report that would send someone looking for a fault that is not there.
/// </param>
/// <param name="clock">UTC ticks. Injectable so the states either side of an interval can be asserted
/// without waiting for real time to pass through them.</param>
public sealed class WorkLedger(TimeSpan? remember = null, Func<long>? clock = null)
{
    private readonly ConcurrentDictionary<string, WorkItem> _work = new(StringComparer.Ordinal);
    private readonly TimeSpan _remember = remember ?? TimeSpan.FromMinutes(5);
    private readonly Func<long> _clock = clock ?? (() => DateTime.UtcNow.Ticks);

    /// <summary>The command line, short enough to sit in a one-line report.</summary>
    public static string Describe(string[] args)
    {
        var line = string.Join(' ', args);
        return line.Length <= 120 ? line : line[..117] + "...";
    }

    /// <summary>Takes a command in, and drops the ones nobody can still be asking about.</summary>
    public WorkItem Accept(string ticket, string[] args, string workspace)
    {
        var now = _clock();
        foreach (var (id, past) in _work)
        {
            var finished = Volatile.Read(ref past.Finished);
            if (finished > 0 && now - finished > _remember.Ticks) _work.TryRemove(id, out _);
        }

        var work = new WorkItem(ticket, Describe(args), workspace, now);
        _work[ticket] = work;
        return work;
    }

    /// <summary>It has its turn: queued becomes running.</summary>
    public void Running(WorkItem work) => Volatile.Write(ref work.Started, _clock());

    /// <summary>It is done, and stays answerable for a while longer.</summary>
    public void Done(WorkItem work) => Volatile.Write(ref work.Finished, _clock());

    /// <summary>What is known about a ticket, right now and without waiting for anything.</summary>
    public DaemonWorkStatus StatusOf(string ticket)
    {
        if (!_work.TryGetValue(ticket, out var work))
            return new DaemonWorkStatus(ticket, WorkState.Unknown, "", 0, 0, null);

        var now      = _clock();
        var started  = Volatile.Read(ref work.Started);
        var finished = Volatile.Read(ref work.Finished);

        var state = finished > 0 ? WorkState.Finished
                  : started  > 0 ? WorkState.Running
                                 : WorkState.Queued;

        return new DaemonWorkStatus(
            ticket, state, work.Command,
            WaitedSeconds: Seconds((started > 0 ? started : now) - work.Accepted),
            RanSeconds:    started == 0 ? 0 : Seconds((finished > 0 ? finished : now) - started),
            Behind:        state == WorkState.Queued ? Holder(work.Workspace, now) : null);
    }

    /// <summary>Everything still on the books, newest first. For someone asking after the process rather than
    /// after a command of their own — which, when a command appears to be stuck, is the question.</summary>
    public DaemonWorkStatus[] All()
    {
        var all = new List<DaemonWorkStatus>();
        foreach (var ticket in _work.Keys) all.Add(StatusOf(ticket));

        all.Sort((a, b) => (b.WaitedSeconds + b.RanSeconds).CompareTo(a.WaitedSeconds + a.RanSeconds));
        return [.. all];
    }

    /// <summary>
    /// What a queued command is waiting on. Worth carrying, because "behind a graph build that has run for
    /// ninety seconds" and "taking ninety seconds itself" are different situations with different answers,
    /// and a caller told only that it is slow cannot tell which one it is in.
    /// </summary>
    private string? Holder(string workspace, long now)
    {
        foreach (var other in _work.Values)
        {
            if (!string.Equals(other.Workspace, workspace, StringComparison.OrdinalIgnoreCase)) continue;

            var started = Volatile.Read(ref other.Started);
            if (started == 0 || Volatile.Read(ref other.Finished) > 0) continue;

            return $"{other.Command} ({Seconds(now - started):F0}s)";
        }
        return null;
    }

    private static double Seconds(long ticks) => Math.Max(0, ticks) / (double)TimeSpan.TicksPerSecond;
}

/// <summary>One accepted command: when it was taken, when it got its turn, and when it was done.</summary>
public sealed class WorkItem(string ticket, string command, string workspace, long accepted)
{
    public string Ticket { get; } = ticket;

    internal readonly string Command   = command;
    internal readonly string Workspace = workspace;
    internal readonly long   Accepted  = accepted;

    /// <summary>When the workspace lock was taken, or 0 while still queued.</summary>
    internal long Started;

    /// <summary>When the connection was finished with, or 0 while still running.</summary>
    internal long Finished;
}

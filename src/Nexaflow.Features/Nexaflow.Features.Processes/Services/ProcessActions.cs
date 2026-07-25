using System.ComponentModel;
using System.Diagnostics;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.Processes.Services;

/// <summary>
/// The two mutating process actions, shared by the list view-model, the details view-model, and the AI
/// tools. Each tries the action non-elevated first (succeeds for same-user, non-protected processes) and,
/// on an access denial, escalates through the privilege bridge — one UAC prompt. Returns a human-readable
/// outcome string; surfaces real failures via <see cref="IShellServices.ShowError"/> but never throws.
/// </summary>
internal static class ProcessActions
{
    public static async Task<string> KillAsync(IShellServices shell, int pid, string name, bool tree)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: tree);
            return $"Terminated {name} (PID {pid}){(tree ? " and its child processes" : "")}.";
        }
        catch (ArgumentException)
        {
            return $"Process {pid} is no longer running.";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return Outcome(shell, await shell.RunElevatedAsync(KillRequest(pid, tree)),
                           $"{name} was not terminated");
        }
    }

    /// <summary>
    /// The bridge payload for an elevated kill. Split out because the branch that sends it is only reached
    /// on a real access denial (a protected process) — the request <em>shape</em> is what silently breaks
    /// elevation if an op name or arg key drifts, so it is built here where it can be asserted.
    /// </summary>
    internal static ElevatedRequest KillRequest(int pid, bool tree) =>
        ElevatedRequest.Single(ElevatedOps.ProcessKill,
            (ElevatedArgs.ProcessId, pid.ToString()),
            (ElevatedArgs.ProcessKillTree, tree ? "true" : "false"));

    public static async Task<string> SetPriorityAsync(IShellServices shell, int pid, string name, string priority)
    {
        if (!Enum.TryParse<ProcessPriorityClass>(priority, ignoreCase: true, out var pc))
            return $"Unknown priority class '{priority}'.";

        try
        {
            using var p = Process.GetProcessById(pid);
            p.PriorityClass = pc;
            return $"Set {name} (PID {pid}) priority to {pc}.";
        }
        catch (ArgumentException)
        {
            return $"Process {pid} is no longer running.";
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return Outcome(shell, await shell.RunElevatedAsync(SetPriorityRequest(pid, pc)),
                           $"{name}'s priority was not changed");
        }
    }

    /// <summary>The bridge payload for an elevated priority change — see <see cref="KillRequest"/>.</summary>
    internal static ElevatedRequest SetPriorityRequest(int pid, ProcessPriorityClass priority) =>
        ElevatedRequest.Single(ElevatedOps.ProcessSetPriority,
            (ElevatedArgs.ProcessId, pid.ToString()),
            (ElevatedArgs.ProcessPriority, priority.ToString()));

    /// <summary>
    /// Turns an elevation result into what the user is told. The distinction that matters: a <b>declined</b>
    /// UAC prompt is the user's own answer, so it is reported back but never raised as an error toast; a
    /// genuine <b>failure</b> is something they didn't ask for and is surfaced. Internal so that rule is
    /// assertable — the access denial that triggers escalation needs a protected process, which a test
    /// must not go looking for.
    /// </summary>
    internal static string Outcome(IShellServices shell, ElevatedResult res, string declinedSubject)
    {
        if (res.WasDeclined) return $"Administrator approval was declined; {declinedSubject}.";
        if (!res.Success) { shell.ShowError(res.Message); return res.Message; }
        return res.Message;
    }
}

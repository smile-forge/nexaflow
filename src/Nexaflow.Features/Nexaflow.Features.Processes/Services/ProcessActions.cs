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
            var res = await shell.RunElevatedAsync(ElevatedRequest.Single(ElevatedOps.ProcessKill,
                (ElevatedArgs.ProcessId, pid.ToString()),
                (ElevatedArgs.ProcessKillTree, tree ? "true" : "false")));
            return Outcome(shell, res, $"{name} was not terminated");
        }
    }

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
            var res = await shell.RunElevatedAsync(ElevatedRequest.Single(ElevatedOps.ProcessSetPriority,
                (ElevatedArgs.ProcessId, pid.ToString()),
                (ElevatedArgs.ProcessPriority, pc.ToString())));
            return Outcome(shell, res, $"{name}'s priority was not changed");
        }
    }

    private static string Outcome(IShellServices shell, ElevatedResult res, string declinedSubject)
    {
        if (res.WasDeclined) return $"Administrator approval was declined; {declinedSubject}.";
        if (!res.Success) { shell.ShowError(res.Message); return res.Message; }
        return res.Message;
    }
}

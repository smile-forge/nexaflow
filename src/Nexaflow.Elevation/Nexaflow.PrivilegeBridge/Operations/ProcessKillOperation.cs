using System.Diagnostics;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>Terminates a process (optionally its whole child tree) with admin rights.</summary>
internal sealed class ProcessKillOperation : ProcessOperationBase
{
    public override string Id => ElevatedOps.ProcessKill;

    protected override ElevatedOperationResult Run(Process proc, int pid, IReadOnlyDictionary<string, string> args)
    {
        bool tree = args.TryGetValue(ElevatedArgs.ProcessKillTree, out var t)
                    && bool.TryParse(t, out var b) && b;
        var name = SafeName(proc);
        proc.Kill(entireProcessTree: tree);
        return Ok($"Terminated {name} (PID {pid}){(tree ? " and its child processes" : "")}.");
    }
}

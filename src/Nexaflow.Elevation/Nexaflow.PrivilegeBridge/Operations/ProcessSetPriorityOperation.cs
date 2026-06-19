using System.Diagnostics;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>Changes a process's priority class with admin rights.</summary>
internal sealed class ProcessSetPriorityOperation : ProcessOperationBase
{
    public override string Id => ElevatedOps.ProcessSetPriority;

    protected override ElevatedOperationResult Run(Process proc, int pid, IReadOnlyDictionary<string, string> args)
    {
        var raw = args.GetValueOrDefault(ElevatedArgs.ProcessPriority);
        if (string.IsNullOrWhiteSpace(raw) ||
            !Enum.TryParse<ProcessPriorityClass>(raw, ignoreCase: true, out var priority))
            return Fail($"Unknown priority class '{raw}'.");

        proc.PriorityClass = priority;
        return Ok($"Set {SafeName(proc)} (PID {pid}) priority to {priority}.");
    }
}

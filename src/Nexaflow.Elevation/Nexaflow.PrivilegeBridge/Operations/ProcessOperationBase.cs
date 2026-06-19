using System.Diagnostics;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>
/// Shared plumbing for process-control handlers: validates the PID, opens the process, and turns thrown
/// errors (gone, access denied) into a clean failed result. Mirrors <see cref="ServiceOperationBase"/>.
/// </summary>
internal abstract class ProcessOperationBase : IElevatedOperation
{
    public abstract string Id { get; }

    public ElevatedOperationResult Execute(IReadOnlyDictionary<string, string> args)
    {
        if (!args.TryGetValue(ElevatedArgs.ProcessId, out var raw) || !int.TryParse(raw, out var pid))
            return Fail("Missing or invalid process id.");

        try
        {
            using var proc = Process.GetProcessById(pid);
            return Run(proc, pid, args);
        }
        catch (ArgumentException)
        {
            return Fail($"Process {pid} is no longer running.");
        }
        catch (Exception ex)
        {
            return Fail($"'{pid}': {(ex.InnerException ?? ex).Message}");
        }
    }

    protected abstract ElevatedOperationResult Run(Process proc, int pid, IReadOnlyDictionary<string, string> args);

    protected ElevatedOperationResult Ok(string message) => ElevatedOperationResult.Ok(Id, message);

    protected ElevatedOperationResult Fail(string message) =>
        ElevatedOperationResult.Fail(Id, ElevatedErrorKind.OperationFailed, message);

    protected static string SafeName(Process p) { try { return p.ProcessName; } catch { return "process"; } }
}

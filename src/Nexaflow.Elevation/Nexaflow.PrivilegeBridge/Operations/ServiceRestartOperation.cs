using System.ServiceProcess;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

internal sealed class ServiceRestartOperation : ServiceOperationBase
{
    public override string Id => ElevatedOps.ServiceRestart;

    protected override ElevatedOperationResult Run(
        ServiceController sc, string name, IReadOnlyDictionary<string, string> args)
    {
        ServiceStopHelper.StopWithDependents(sc, WaitTimeout);
        sc.Refresh();
        if (sc.Status != ServiceControllerStatus.Stopped)
            return Fail($"{name} would not stop (now {sc.Status}); restart aborted.");

        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, WaitTimeout);
        sc.Refresh();
        return sc.Status == ServiceControllerStatus.Running
            ? Ok($"Restarted {name}.")
            : Fail($"{name} stopped but did not restart (now {sc.Status}).");
    }
}

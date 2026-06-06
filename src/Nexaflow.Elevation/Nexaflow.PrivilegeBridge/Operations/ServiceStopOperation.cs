using System.ServiceProcess;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

internal sealed class ServiceStopOperation : ServiceOperationBase
{
    public override string Id => ElevatedOps.ServiceStop;

    protected override ElevatedOperationResult Run(
        ServiceController sc, string name, IReadOnlyDictionary<string, string> args)
    {
        if (sc.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
            return Ok($"{name} is already stopped.");

        ServiceStopHelper.StopWithDependents(sc, WaitTimeout);
        sc.Refresh();
        return sc.Status == ServiceControllerStatus.Stopped
            ? Ok($"Stopped {name}.")
            : Fail($"{name} did not reach Stopped (now {sc.Status}).");
    }
}

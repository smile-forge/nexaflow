using System.ServiceProcess;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

internal sealed class ServiceStartOperation : ServiceOperationBase
{
    public override string Id => ElevatedOps.ServiceStart;

    protected override ElevatedOperationResult Run(
        ServiceController sc, string name, IReadOnlyDictionary<string, string> args)
    {
        if (sc.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
            return Ok($"{name} is already running.");

        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, WaitTimeout);
        sc.Refresh();
        return sc.Status == ServiceControllerStatus.Running
            ? Ok($"Started {name}.")
            : Fail($"{name} did not reach Running (now {sc.Status}).");
    }
}

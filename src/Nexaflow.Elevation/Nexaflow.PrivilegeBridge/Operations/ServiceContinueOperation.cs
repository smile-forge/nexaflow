using System.ServiceProcess;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

internal sealed class ServiceContinueOperation : ServiceOperationBase
{
    public override string Id => ElevatedOps.ServiceContinue;

    protected override ElevatedOperationResult Run(
        ServiceController sc, string name, IReadOnlyDictionary<string, string> args)
    {
        if (sc.Status == ServiceControllerStatus.Running)
            return Ok($"{name} is already running.");

        sc.Continue();
        sc.WaitForStatus(ServiceControllerStatus.Running, WaitTimeout);
        sc.Refresh();
        return sc.Status == ServiceControllerStatus.Running
            ? Ok($"Resumed {name}.")
            : Fail($"{name} did not resume (now {sc.Status}).");
    }
}

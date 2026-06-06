using System.ServiceProcess;
using Nexaflow.Elevation.Contracts;

namespace Nexaflow.PrivilegeBridge.Operations;

internal sealed class ServicePauseOperation : ServiceOperationBase
{
    public override string Id => ElevatedOps.ServicePause;

    protected override ElevatedOperationResult Run(
        ServiceController sc, string name, IReadOnlyDictionary<string, string> args)
    {
        if (!sc.CanPauseAndContinue)
            return Fail($"{name} does not support pause/continue.");
        if (sc.Status == ServiceControllerStatus.Paused)
            return Ok($"{name} is already paused.");

        sc.Pause();
        sc.WaitForStatus(ServiceControllerStatus.Paused, WaitTimeout);
        sc.Refresh();
        return sc.Status == ServiceControllerStatus.Paused
            ? Ok($"Paused {name}.")
            : Fail($"{name} did not reach Paused (now {sc.Status}).");
    }
}

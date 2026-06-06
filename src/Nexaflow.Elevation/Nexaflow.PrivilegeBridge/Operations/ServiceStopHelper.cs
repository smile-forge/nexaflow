using System.ServiceProcess;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>Stops a service and any running services that depend on it (Stop() throws otherwise).</summary>
internal static class ServiceStopHelper
{
    public static void StopWithDependents(ServiceController sc, TimeSpan timeout)
    {
        foreach (var dep in sc.DependentServices)
        {
            if (dep.Status != ServiceControllerStatus.Stopped)
            {
                dep.Stop();
                dep.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
            }
        }

        if (sc.Status != ServiceControllerStatus.Stopped)
        {
            sc.Stop();
            sc.WaitForStatus(ServiceControllerStatus.Stopped, timeout);
        }
    }
}

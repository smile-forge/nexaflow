using System.Runtime.Versioning;
using System.ServiceProcess;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Service;

/// <summary>
/// The host as Windows runs it (<see cref="AiHostProtocol.ServiceName"/>): started with the machine, whoever is signed in or not, its
/// supervisor running until the service manager stops it. A supervisor that cannot go on — its pipe's name taken — stops the
/// service with an exit code the service manager records.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class HostWindowsService : ServiceBase
{
    private readonly WorkerSupervisor _supervisor;
    private readonly HostLog _log;
    private readonly TimeSpan _stopWait;
    private readonly CancellationTokenSource _stop = new();
    private Task _running = Task.CompletedTask;

    public HostWindowsService(WorkerSupervisor supervisor, HostLog log, WorkerRestarts restarts)
    {
        _supervisor = supervisor;
        _log = log;
        _stopWait = restarts.StopGrace + TimeSpan.FromSeconds(5);
        ServiceName = AiHostProtocol.ServiceName;
        CanStop = true;
        CanShutdown = true;
        AutoLog = false;
    }

    protected override void OnStart(string[] args) => _running = Task.Run(SuperviseAsync);

    private async Task SuperviseAsync()
    {
        try
        {
            await _supervisor.RunAsync(_stop.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.Write($"fatal: {ex}");
            ExitCode = 1;
            // Stopped from another thread: stopping waits for this task, which ends here.
            _ = Task.Run(Stop);
        }
    }

    protected override void OnStop()
    {
        _stop.Cancel();
        _running.Wait(_stopWait);
        _log.Write("service stopped");
    }

    protected override void OnShutdown() => OnStop();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _stop.Dispose();
        base.Dispose(disposing);
    }
}

using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.Models;

namespace Nexaflow.Features.SystemInfo.Services;

/// <summary>Runs the (blocking) service WMI scan off the UI thread via the shell's activity queue.</summary>
public sealed class LoadServicesTask(ServicesCollector collector) : IBackgroundTask
{
    public string Description => "Gathering Windows services";

    public IReadOnlyList<ServiceRow>? Result { get; private set; }

    public Task RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        Result = collector.Collect();
    }, ct);
}

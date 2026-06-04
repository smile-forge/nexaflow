using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.Models;

namespace Nexaflow.Features.SystemInfo.Services;

/// <summary>
/// Runs the (blocking) WMI/registry probing off the UI thread via the shell's background-activity
/// queue. The built snapshot is read from <see cref="Result"/> in the completion callback.
/// </summary>
public sealed class LoadSystemInfoTask(SystemInfoCollector collector) : IBackgroundTask
{
    public string Description => "Gathering system information";

    public SystemInfoSnapshot? Result { get; private set; }

    public Task RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        Result = collector.Collect();
    }, ct);
}

using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.SystemInfo.Services;

/// <summary>
/// Runs the (blocking) WMI/registry probing off the UI thread via the shell's background-activity
/// queue. The built snapshot is read from <see cref="Result"/> in the completion callback.
/// </summary>
public sealed class LoadSystemInfoTask(ISystemInfoCollector collector, IAIService ai) : IBackgroundTask
{
    public string Description => Str.Get("SystemInfo.Task.GatheringSystem");

    public SystemInfoSnapshot? Result { get; private set; }

    /// <summary>Where the workspace's models of Nexaflow's own run; null when it uses none.</summary>
    public LocalAiSetup? LocalAi { get; private set; }

    public async Task RunAsync(CancellationToken ct)
    {
        LocalAi = await ai.DescribeLocalAiAsync(ct);
        Result = await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            return collector.Collect(LocalAi);
        }, ct);
    }
}

using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.SystemInfo.Services;

/// <summary>Reads all three environment scopes off the UI thread via the shell's activity queue.</summary>
public sealed class LoadEnvVarsTask(EnvVarsCollector collector) : IBackgroundTask
{
    public string Description => Str.Get("SystemInfo.Task.GatheringEnvVars");

    public EnvVarsBundle? Result { get; private set; }

    public Task RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        ct.ThrowIfCancellationRequested();
        Result = collector.CollectAll();
    }, ct);
}

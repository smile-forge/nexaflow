using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsApps.Models;
using Nexaflow.Features.WindowsApps.ViewModels;

namespace Nexaflow.Features.WindowsApps.Services;

/// <summary>
/// Scans installed apps off the UI thread (shown in the shell's background-activity area). The result
/// is read from <see cref="Result"/> in the <c>QueueBackgroundTask</c> completion callback.
/// </summary>
public sealed class LoadInstalledAppsTask(InstalledAppsService service) : IBackgroundTask
{
    public string Description => "Scanning installed apps";

    public IReadOnlyList<InstalledApp> Result { get; private set; } = [];

    public async Task RunAsync(CancellationToken ct) => Result = await service.LoadAsync(ct);
}

/// <summary>Uninstalls one app off the UI thread. <see cref="Result"/> carries success + any error text.</summary>
public sealed class UninstallAppTask(InstalledAppsService service, InstalledApp app) : IBackgroundTask
{
    public string Description => $"Uninstalling {app.Name}";

    public UninstallResult Result { get; private set; } = UninstallResult.Ok;

    public async Task RunAsync(CancellationToken ct) => Result = await service.UninstallAsync(app, ct);
}

/// <summary>Removes a stale/orphaned list record off the UI thread. <see cref="Succeeded"/> reports the outcome.</summary>
public sealed class DeleteAppRecordTask(InstalledAppsService service, InstalledApp app) : IBackgroundTask
{
    public string Description => $"Removing record for {app.Name}";

    public bool Succeeded { get; private set; }

    public async Task RunAsync(CancellationToken ct) => Succeeded = await service.DeleteRecordAsync(app, ct);
}

/// <summary>
/// Second load pass: measures the on-disk size of apps that didn't report one (the slow part, kept off
/// the initial discovery). Results land in <see cref="Measured"/> and are applied to the items on the UI
/// thread in the completion callback.
/// </summary>
public sealed class FillAppSizesTask(IReadOnlyList<InstalledAppItem> items) : IBackgroundTask
{
    public string Description => "Measuring app sizes";

    public Dictionary<InstalledAppItem, long> Measured { get; } = [];

    public Task RunAsync(CancellationToken ct) => Task.Run(() =>
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (item.SizeBytes is not null) continue;
            var loc = item.App.InstallLocation;
            if (string.IsNullOrWhiteSpace(loc)) continue;
            if (FolderSize.Measure(loc, ct) is { } size) Measured[item] = size;
        }
    }, ct);
}

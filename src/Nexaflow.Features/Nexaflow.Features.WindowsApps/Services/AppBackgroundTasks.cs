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

    public AppOperationResult Result { get; private set; } = AppOperationResult.Ok;

    public async Task RunAsync(CancellationToken ct) => Result = await service.UninstallAsync(app, ct);
}

/// <summary>
/// Reopens the program's installer in maintenance mode ("Modify"). The vendor's UI is interactive, so
/// this task stays alive for as long as the user is in it.
/// </summary>
public sealed class ModifyAppTask(InstalledAppsService service, InstalledApp app) : IBackgroundTask
{
    public string Description => $"Changing {app.Name}";

    public AppOperationResult Result { get; private set; } = AppOperationResult.Ok;

    public async Task RunAsync(CancellationToken ct) => Result = await service.ModifyAsync(app, ct);
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

// ── Advanced options (Store apps) ─────────────────────────────────────────────

/// <summary>Lists the drives a package could be moved to, for the Move dropdown.</summary>
public sealed class LoadAppVolumesTask(IStoreAppOperations ops) : IBackgroundTask
{
    public string Description => "Looking for available drives";

    public IReadOnlyList<AppVolume> Result { get; private set; } = [];

    public async Task RunAsync(CancellationToken ct) => Result = await ops.GetVolumesAsync(ct);
}

/// <summary>Relocates a package's files to another drive.</summary>
public sealed class MoveAppTask(IStoreAppOperations ops, InstalledApp app, AppVolume target)
    : IBackgroundTask
{
    public string Description => $"Moving {app.Name} to {target.MountPoint.TrimEnd('\\')}";

    public AppOperationResult Result { get; private set; } = AppOperationResult.Ok;

    public async Task RunAsync(CancellationToken ct) => Result = await ops.MoveAsync(app, target, ct);
}

/// <summary>Re-registers a package from its manifest, leaving its data alone ("Repair").</summary>
public sealed class RepairAppTask(IStoreAppOperations ops, InstalledApp app) : IBackgroundTask
{
    public string Description => $"Repairing {app.Name}";

    public AppOperationResult Result { get; private set; } = AppOperationResult.Ok;

    public async Task RunAsync(CancellationToken ct) => Result = await ops.RepairAsync(app, ct);
}

/// <summary>Stops a package, deletes its saved data, then re-registers it ("Reset").</summary>
public sealed class ResetAppTask(IStoreAppOperations ops, InstalledApp app) : IBackgroundTask
{
    public string Description => $"Resetting {app.Name}";

    public AppOperationResult Result { get; private set; } = AppOperationResult.Ok;

    public async Task RunAsync(CancellationToken ct) => Result = await ops.ResetAsync(app, ct);
}

/// <summary>Kills a package's running processes. <see cref="Killed"/> is how many were stopped.</summary>
public sealed class TerminateAppTask(IStoreAppOperations ops, InstalledApp app) : IBackgroundTask
{
    public string Description => $"Terminating {app.Name}";

    public int Killed { get; private set; }

    public async Task RunAsync(CancellationToken ct) => Killed = await ops.TerminateAsync(app, ct);
}

/// <summary>Finds the optional packages installed against an app — its add-ons / downloadable content.</summary>
public sealed class LoadAddOnsTask(IStoreAppOperations ops, InstalledApp app) : IBackgroundTask
{
    public string Description => $"Looking for {app.Name} add-ons";

    public IReadOnlyList<AppAddOn> Result { get; private set; } = [];

    public async Task RunAsync(CancellationToken ct) => Result = await ops.GetAddOnsAsync(app, ct);
}

/// <summary>Removes one add-on, leaving the app it extends installed.</summary>
public sealed class RemoveAddOnTask(IStoreAppOperations ops, AppAddOn addOn) : IBackgroundTask
{
    public string Description => $"Removing {addOn.Name}";

    public AppOperationResult Result { get; private set; } = AppOperationResult.Ok;

    public async Task RunAsync(CancellationToken ct) => Result = await ops.RemoveAddOnAsync(addOn, ct);
}

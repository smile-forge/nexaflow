using Nexaflow.Features.WindowsApps.Models;

namespace Nexaflow.Features.WindowsApps.Services;

/// <summary>
/// The package-model operations Windows exposes on a Store app's "Advanced options" page, plus Move.
/// Declared apart from <see cref="IInstalledAppSource"/> because none of them mean anything for a Win32
/// program: they all act on an MSIX package identity rather than a vendor-supplied command line.
/// Implemented by <see cref="StoreAppSource"/>; every method runs off the UI thread.
/// </summary>
public interface IStoreAppOperations
{
    /// <summary>The drives a package can be moved to (offline volumes are omitted).</summary>
    Task<IReadOnlyList<AppVolume>> GetVolumesAsync(CancellationToken ct);

    /// <summary>Relocates the package's files to another drive, keeping its identity and data.</summary>
    Task<AppOperationResult> MoveAsync(InstalledApp app, AppVolume target, CancellationToken ct);

    /// <summary>
    /// Re-registers the package from its own manifest — Windows' "Repair": it rebuilds the app's
    /// registration without touching the data the app has saved.
    /// </summary>
    Task<AppOperationResult> RepairAsync(InstalledApp app, CancellationToken ct);

    /// <summary>
    /// Windows' "Reset": stops the app, deletes its saved data, then re-registers it, so it starts as
    /// if freshly installed. Destructive — the caller must confirm first.
    /// </summary>
    Task<AppOperationResult> ResetAsync(InstalledApp app, CancellationToken ct);

    /// <summary>Kills the app's running processes. Returns how many were stopped.</summary>
    Task<int> TerminateAsync(InstalledApp app, CancellationToken ct);

    /// <summary>The optional packages installed against this app — its add-ons / downloadable content.</summary>
    Task<IReadOnlyList<AppAddOn>> GetAddOnsAsync(InstalledApp app, CancellationToken ct);

    /// <summary>Removes one add-on, leaving the app it extends installed.</summary>
    Task<AppOperationResult> RemoveAddOnAsync(AppAddOn addOn, CancellationToken ct);
}

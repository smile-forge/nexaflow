using Nexaflow.Features.WindowsApps.Models;

namespace Nexaflow.Features.WindowsApps.Services;

/// <summary>
/// A provider of installed applications for one discovery mechanism (registry Uninstall keys, or
/// the Store/MSIX package manager). Implementations run off the UI thread.
///
/// Only the operations every source can offer live here; the package-model-specific ones (move,
/// repair, reset, terminate, add-ons) are declared by <see cref="IStoreAppOperations"/> instead, so
/// the registry source doesn't carry a row of stubs it can never honour.
/// </summary>
public interface IInstalledAppSource
{
    /// <summary>Which source this provider serves — used to route an <see cref="UninstallAsync"/> call.</summary>
    AppSource Source { get; }

    /// <summary>Enumerates the apps this source knows about. Never throws for an empty/locked hive —
    /// returns what it could read.</summary>
    Task<IReadOnlyList<InstalledApp>> EnumerateAsync(CancellationToken ct);

    /// <summary>Uninstalls <paramref name="app"/>, reporting the failure reason when it doesn't succeed.</summary>
    Task<AppOperationResult> UninstallAsync(InstalledApp app, CancellationToken ct);

    /// <summary>
    /// Reopens the program's own installer so the user can change which features are installed
    /// ("Modify" in Add/remove programs). Default: not supported — only the registry source has a
    /// vendor command to run.
    /// </summary>
    Task<AppOperationResult> ModifyAsync(InstalledApp app, CancellationToken ct) =>
        Task.FromResult(AppOperationResult.Unsupported("Modify"));

    /// <summary>
    /// Removes a stale list entry whose program is already gone (e.g. an orphaned registry record).
    /// Default: not supported (returns false) — only the registry source implements it.
    /// </summary>
    Task<bool> DeleteRecordAsync(InstalledApp app, CancellationToken ct) => Task.FromResult(false);
}

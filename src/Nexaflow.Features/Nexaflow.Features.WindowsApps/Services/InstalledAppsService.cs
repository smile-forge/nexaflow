using Nexaflow.Features.WindowsApps.Models;

namespace Nexaflow.Features.WindowsApps.Services;

/// <summary>
/// Aggregates the installed-app sources (Win32 registry + Store/MSIX) into one list and routes an
/// operation back to the source that produced the app. All work runs off the UI thread (the callers
/// are background tasks); this type never touches the dispatcher.
/// </summary>
public sealed class InstalledAppsService
{
    private readonly IReadOnlyList<IInstalledAppSource> _sources;

    public InstalledAppsService()
        : this([new RegistryAppSource(), new StoreAppSource()], new RegistryBackgroundAppAccess()) { }

    public InstalledAppsService(IReadOnlyList<IInstalledAppSource> sources,
                                IBackgroundAppAccess? backgroundAccess = null)
    {
        _sources         = sources;
        BackgroundAccess = backgroundAccess ?? new RegistryBackgroundAppAccess();
    }

    /// <summary>The "Let this app run in background" policy store (Store apps only).</summary>
    public IBackgroundAppAccess BackgroundAccess { get; }

    /// <summary>
    /// The package-model operations behind Advanced options, or null when no configured source offers
    /// them — the pane is only ever opened for a Store app, but a test harness may have no Store source.
    /// </summary>
    public IStoreAppOperations? StoreOperations =>
        _sources.OfType<IStoreAppOperations>().FirstOrDefault();

    public async Task<IReadOnlyList<InstalledApp>> LoadAsync(CancellationToken ct)
    {
        var results = await Task.WhenAll(_sources.Select(s => s.EnumerateAsync(ct)));
        return results.SelectMany(r => r)
                      .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                      .ToList();
    }

    public Task<AppOperationResult> UninstallAsync(InstalledApp app, CancellationToken ct) =>
        Route(app, s => s.UninstallAsync(app, ct), "Uninstall");

    public Task<AppOperationResult> ModifyAsync(InstalledApp app, CancellationToken ct) =>
        Route(app, s => s.ModifyAsync(app, ct), "Modify");

    public Task<bool> DeleteRecordAsync(InstalledApp app, CancellationToken ct)
    {
        var source = SourceFor(app);
        return source is null ? Task.FromResult(false) : source.DeleteRecordAsync(app, ct);
    }

    private Task<AppOperationResult> Route(InstalledApp app,
                                           Func<IInstalledAppSource, Task<AppOperationResult>> run,
                                           string operation)
    {
        var source = SourceFor(app);
        return source is null
            ? Task.FromResult(AppOperationResult.Unsupported(operation))
            : run(source);
    }

    private IInstalledAppSource? SourceFor(InstalledApp app) =>
        _sources.FirstOrDefault(s => s.Source == app.Source);
}

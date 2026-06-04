using Nexaflow.Features.WindowsApps.Models;

namespace Nexaflow.Features.WindowsApps.Services;

/// <summary>
/// Aggregates the installed-app sources (Win32 registry + Store/MSIX) into one list and routes an
/// uninstall back to the source that produced the app. All work runs off the UI thread (the callers
/// are background tasks); this type never touches the dispatcher.
/// </summary>
public sealed class InstalledAppsService
{
    private readonly IReadOnlyList<IInstalledAppSource> _sources;

    public InstalledAppsService()
        : this([new RegistryAppSource(), new StoreAppSource()]) { }

    public InstalledAppsService(IReadOnlyList<IInstalledAppSource> sources) => _sources = sources;

    public async Task<IReadOnlyList<InstalledApp>> LoadAsync(CancellationToken ct)
    {
        var results = await Task.WhenAll(_sources.Select(s => s.EnumerateAsync(ct)));
        return results.SelectMany(r => r)
                      .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                      .ToList();
    }

    public Task<UninstallResult> UninstallAsync(InstalledApp app, CancellationToken ct)
    {
        var source = _sources.FirstOrDefault(s => s.Source == app.Source);
        return source is null
            ? Task.FromResult(UninstallResult.Fail("No installer source can handle this app."))
            : source.UninstallAsync(app, ct);
    }

    public Task<bool> DeleteRecordAsync(InstalledApp app, CancellationToken ct)
    {
        var source = _sources.FirstOrDefault(s => s.Source == app.Source);
        return source is null ? Task.FromResult(false) : source.DeleteRecordAsync(app, ct);
    }
}

using Nexaflow.Features.WindowsApps.Models;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace Nexaflow.Features.WindowsApps.Services;

/// <summary>
/// Enumerates Microsoft Store / UWP (MSIX) packages installed for the current user via the WinRT
/// <see cref="PackageManager"/>. Framework, resource and OS-system packages are hidden (they are not
/// user-facing apps). Uninstall is awaited through <c>RemovePackageAsync</c>.
/// </summary>
public sealed class StoreAppSource : IInstalledAppSource
{
    public AppSource Source => AppSource.Store;

    public Task<IReadOnlyList<InstalledApp>> EnumerateAsync(CancellationToken ct) =>
        Task.Run<IReadOnlyList<InstalledApp>>(() =>
        {
            var apps = new List<InstalledApp>();
            PackageManager pm;
            try { pm = new PackageManager(); }
            catch { return apps; }

            IEnumerable<Package> packages;
            try { packages = pm.FindPackagesForUser(string.Empty); }
            catch { return apps; }

            foreach (var package in packages)
            {
                ct.ThrowIfCancellationRequested();
                if (package.IsFramework || package.IsResourcePackage) continue;
                if (package.SignatureKind == PackageSignatureKind.System) continue;

                var app = TryMap(package);
                if (app is not null) apps.Add(app);
            }
            return apps;
        }, ct);

    private static InstalledApp? TryMap(Package package)
    {
        try
        {
            var id = package.Id;

            var name = package.DisplayName;
            if (string.IsNullOrWhiteSpace(name) ||
                name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                name = id.Name;
            if (string.IsNullOrWhiteSpace(name)) return null;

            var v = id.Version;
            var version = $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";

            string? location = TryGetPath(package);

            return new InstalledApp
            {
                Name            = name,
                Publisher       = string.IsNullOrWhiteSpace(package.PublisherDisplayName)
                                      ? id.Publisher : package.PublisherDisplayName,
                Version         = version,
                InstallDate     = TryGetInstalledDate(package),
                SizeBytes       = null, // measured in the second pass from InstalledPath
                Source          = AppSource.Store,
                InstallLocation = location,
                PackageFullName = id.FullName,
            };
        }
        catch { return null; }
    }

    private static string? TryGetPath(Package package)
    {
        try { return package.InstalledPath; }
        catch { return null; }
    }

    private static DateTime? TryGetInstalledDate(Package package)
    {
        try { return package.InstalledDate.LocalDateTime; }
        catch { return null; }
    }

    public async Task<UninstallResult> UninstallAsync(InstalledApp app, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(app.PackageFullName))
            return UninstallResult.Fail("This Store app has no package identity to remove.");
        try
        {
            var pm = new PackageManager();
            var result = await pm.RemovePackageAsync(app.PackageFullName).AsTask(ct);
            if (result.ExtendedErrorCode is null) return UninstallResult.Ok;

            var detail = string.IsNullOrWhiteSpace(result.ErrorText)
                ? result.ExtendedErrorCode.Message
                : result.ErrorText;
            return UninstallResult.Fail(detail);
        }
        catch (Exception ex) { return UninstallResult.Fail(ex.Message); }
    }
}

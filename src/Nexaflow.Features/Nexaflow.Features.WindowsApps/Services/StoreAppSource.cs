using System.Diagnostics;
using System.IO;
using System.Xml.Linq;
using Nexaflow.Features.WindowsApps.Models;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace Nexaflow.Features.WindowsApps.Services;

/// <summary>
/// Enumerates Microsoft Store / UWP (MSIX) packages installed for the current user via the WinRT
/// <see cref="PackageManager"/>. Framework, resource and OS-system packages are hidden (they are not
/// user-facing apps). Uninstall is awaited through <c>RemovePackageAsync</c>.
///
/// Also the home of the package-model operations behind Windows' "Advanced options" page — move,
/// repair, reset, terminate and add-ons (<see cref="IStoreAppOperations"/>).
/// </summary>
public sealed class StoreAppSource : IInstalledAppSource, IStoreAppOperations
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
                if (IsOptional(package)) continue;   // an add-on is listed under its app, not beside it

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
                Name              = name,
                Publisher         = string.IsNullOrWhiteSpace(package.PublisherDisplayName)
                                        ? id.Publisher : package.PublisherDisplayName,
                Version           = version,
                InstallDate       = TryGetInstalledDate(package),
                SizeBytes         = null, // measured in the second pass from InstalledPath
                Source            = AppSource.Store,
                InstallLocation   = location,
                PackageFullName   = id.FullName,
                PackageFamilyName = id.FamilyName,
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

    /// <summary><c>IsOptional</c> throws on some partially-staged packages — treat that as "not optional".</summary>
    private static bool IsOptional(Package package)
    {
        try { return package.IsOptional; }
        catch { return false; }
    }

    public async Task<AppOperationResult> UninstallAsync(InstalledApp app, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(app.PackageFullName))
            return AppOperationResult.Fail("This Store app has no package identity to remove.");
        return await RemovePackageAsync(app.PackageFullName, ct);
    }

    // ── IStoreAppOperations ───────────────────────────────────────────────────

    public Task<IReadOnlyList<AppVolume>> GetVolumesAsync(CancellationToken ct) =>
        Task.Run<IReadOnlyList<AppVolume>>(() =>
        {
            var volumes = new List<AppVolume>();
            try
            {
                foreach (var v in new PackageManager().FindPackageVolumes())
                {
                    ct.ThrowIfCancellationRequested();
                    if (v.IsOffline) continue;   // nothing can be moved onto a volume that isn't there

                    long? free = null;
                    try { free = new DriveInfo(v.MountPoint).AvailableFreeSpace; } catch { }

                    volumes.Add(new AppVolume(v.Name, v.MountPoint, v.PackageStorePath,
                                              v.IsSystemVolume, free));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* no deployment volumes readable ⇒ Move has nothing to offer */ }

            return volumes.OrderBy(v => v.MountPoint, StringComparer.OrdinalIgnoreCase).ToList();
        }, ct);

    public async Task<AppOperationResult> MoveAsync(InstalledApp app, AppVolume target,
                                                    CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(app.PackageFullName))
            return AppOperationResult.Fail("This Store app has no package identity to move.");
        try
        {
            var pm = new PackageManager();

            // The API wants the live PackageVolume, not our flattened copy — re-locate it by id so a
            // drive that was removed since the dropdown was filled fails cleanly instead of silently.
            var volume = pm.FindPackageVolumes().FirstOrDefault(v => v.Name == target.Name);
            if (volume is null)
                return AppOperationResult.Fail("That drive is no longer available.");

            var result = await pm.MovePackageToVolumeAsync(app.PackageFullName,
                                                           DeploymentOptions.None, volume).AsTask(ct);
            return Interpret(result);
        }
        catch (Exception ex) { return AppOperationResult.Fail(ex.Message); }
    }

    public async Task<AppOperationResult> RepairAsync(InstalledApp app, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(app.PackageFullName))
            return AppOperationResult.Fail("This Store app has no package identity to repair.");
        try
        {
            var result = await new PackageManager()
                .RegisterPackageByFullNameAsync(app.PackageFullName, null, DeploymentOptions.None)
                .AsTask(ct);
            return Interpret(result);
        }
        catch (Exception ex) { return AppOperationResult.Fail(ex.Message); }
    }

    /// <summary>
    /// Reset = stop it, wipe the data it saved, then re-register so Windows recreates a clean profile.
    /// The state folders under <c>%LOCALAPPDATA%\Packages\{family}</c> are the app's own data; the
    /// system-managed <c>AC</c> and <c>SystemAppData</c> siblings are left alone.
    /// </summary>
    public async Task<AppOperationResult> ResetAsync(InstalledApp app, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(app.PackageFamilyName))
            return AppOperationResult.Fail("This Store app has no package identity to reset.");

        await TerminateAsync(app, ct);                       // locked files would survive the wipe

        var stubborn = ClearAppData(app.PackageFamilyName, ct);

        var registered = await RepairAsync(app, ct);
        if (!registered.Success) return registered;

        return stubborn.Count == 0
            ? AppOperationResult.Ok
            : AppOperationResult.Fail(
                $"The app was re-registered, but {string.Join(", ", stubborn)} could not be deleted — " +
                "close the app and reset again.");
    }

    private static readonly string[] StateFolders =
        ["LocalState", "LocalCache", "RoamingState", "TempState", "Settings"];

    /// <summary>Deletes the app's own state folders; returns the names of any that resisted.</summary>
    private static List<string> ClearAppData(string packageFamilyName, CancellationToken ct)
    {
        var stubborn = new List<string>();
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Packages", packageFamilyName);
        if (!Directory.Exists(root)) return stubborn;

        foreach (var folder in StateFolders)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(root, folder);
            if (!Directory.Exists(path)) continue;
            try { Directory.Delete(path, recursive: true); }
            catch { stubborn.Add(folder); }
        }
        return stubborn;
    }

    public Task<int> TerminateAsync(InstalledApp app, CancellationToken ct) =>
        Task.Run(() =>
        {
            var root = app.InstallLocation;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return 0;

            var killed = 0;
            foreach (var process in Process.GetProcesses())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // A packaged app's processes run from inside its install folder — that path is the
                    // identity check. Protected processes deny MainModule; they were never ours to kill.
                    var image = process.MainModule?.FileName;
                    if (image is null ||
                        !image.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;

                    process.Kill(entireProcessTree: true);
                    killed++;
                }
                catch { /* exited, or not ours to touch */ }
                finally { process.Dispose(); }
            }
            return killed;
        }, ct);

    public Task<IReadOnlyList<AppAddOn>> GetAddOnsAsync(InstalledApp app, CancellationToken ct) =>
        Task.Run<IReadOnlyList<AppAddOn>>(() =>
        {
            var family = app.PackageFamilyName;
            if (string.IsNullOrWhiteSpace(family)) return [];

            // A family name is "{Name}_{PublisherId}" — an add-on names that first half in its manifest.
            var split = family.LastIndexOf('_');
            if (split <= 0) return [];
            var mainName = family[..split];
            var publisherId = family[(split + 1)..];

            var addOns = new List<AppAddOn>();
            try
            {
                foreach (var package in new PackageManager().FindPackagesForUser(string.Empty))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!IsOptional(package)) continue;
                    if (!ExtendsApp(package, mainName, publisherId)) continue;

                    var path = TryGetPath(package);
                    var v = package.Id.Version;
                    addOns.Add(new AppAddOn(
                        Name: DisplayNameOf(package),
                        Publisher: package.PublisherDisplayName,
                        Version: $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}",
                        PackageFullName: package.Id.FullName,
                        SizeBytes: path is null ? null : FolderSize.Measure(path, ct)));
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* the package manager went away — report what we gathered */ }

            return addOns.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }, ct);

    public async Task<AppOperationResult> RemoveAddOnAsync(AppAddOn addOn, CancellationToken ct) =>
        await RemovePackageAsync(addOn.PackageFullName, ct);

    // ── Shared plumbing ───────────────────────────────────────────────────────

    private static async Task<AppOperationResult> RemovePackageAsync(string fullName,
                                                                     CancellationToken ct)
    {
        try
        {
            var result = await new PackageManager().RemovePackageAsync(fullName).AsTask(ct);
            return Interpret(result);
        }
        catch (Exception ex) { return AppOperationResult.Fail(ex.Message); }
    }

    private static AppOperationResult Interpret(DeploymentResult result)
    {
        if (result.ExtendedErrorCode is null) return AppOperationResult.Ok;
        return AppOperationResult.Fail(string.IsNullOrWhiteSpace(result.ErrorText)
            ? result.ExtendedErrorCode.Message
            : result.ErrorText);
    }

    private static string DisplayNameOf(Package package)
    {
        try
        {
            var name = package.DisplayName;
            return string.IsNullOrWhiteSpace(name) ||
                   name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)
                ? package.Id.Name : name;
        }
        catch { return package.Id.Name; }
    }

    /// <summary>
    /// Does this optional package belong to the app in question? The authoritative answer is the
    /// <c>MainPackageDependency</c> its manifest declares; when the manifest can't be read we fall back
    /// to the publisher id, which at least keeps one publisher's add-ons off another's app.
    /// </summary>
    private static bool ExtendsApp(Package package, string mainName, string publisherId)
    {
        var path = TryGetPath(package);
        if (path is not null)
        {
            var manifest = Path.Combine(path, "AppxManifest.xml");
            try
            {
                if (File.Exists(manifest))
                    return XDocument.Load(manifest)
                                    .Descendants()
                                    .Where(e => e.Name.LocalName == "MainPackageDependency")
                                    .Any(e => string.Equals((string?)e.Attribute("Name"), mainName,
                                                            StringComparison.OrdinalIgnoreCase));
            }
            catch { /* unreadable manifest ⇒ fall through to the weaker test */ }
        }

        try
        {
            var family = package.Id.FamilyName;
            var split = family.LastIndexOf('_');
            return split > 0
                   && family[(split + 1)..].Equals(publisherId, StringComparison.OrdinalIgnoreCase)
                   && family[..split].StartsWith(mainName, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

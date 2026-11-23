using System;
using System.Threading;
using System.Threading.Tasks;
using StageKit.Runtime;
using StageKit.Updatum;

namespace Nexaflow.Core.Services;

/// <summary>
/// Nexaflow's releases on GitHub, read and installed through Updatum. Finding one has no side effect. Installing one
/// downloads it while the app stays usable, then hands the process over to the installer, which needs every file this
/// process holds unlocked: the app's own part of that — closing its windows, letting go of the single-instance guard —
/// is <paramref name="handOver"/>, and what follows an installer that then fails to start, with no window left to
/// report it, is <paramref name="stranded"/>.
/// </summary>
internal sealed class UpdatumReleaseSource(Action handOver, Action<Exception> stranded) : IAppReleaseSource
{
    private const string RepositoryOwner = "smile-forge";
    private const string RepositoryName  = "nexaflow";

    private readonly UpdatumManager _updater = new(RepositoryOwner, RepositoryName)
    {
        AssetRegexPattern = $"^{RepositoryName}_{EntryApplication.GenericRuntimeIdentifier}_v",
        InstallUpdateWindowsExeType = UpdatumWindowsExeType.Installer,
        InstallUpdateWindowsInstallerArguments = "/qb",
        InstallUpdateSingleFileExecutableNameStrategy = UpdatumSingleFileExecutableNameStrategy.EntryApplicationName,
        InstallUpdateSingleFileExecutableName = RepositoryName,
        InstallUpdateCodesignMacOSApp = true,
    };

    public async Task<AppUpdateOffer?> FindNewerAsync(CancellationToken ct)
    {
        if (!await _updater.CheckForUpdatesAsync(ct)) return null;

        // A newer release with nothing built for this machine is not an update this build can take.
        if (_updater.LatestRelease is not { } release || _updater.GetCompatibleReleaseAsset(release) is null) return null;

        return new AppUpdateOffer(release.TagName ?? release.Name ?? "unknown", _updater.GetChangelog(true) ?? string.Empty);
    }

    public async Task InstallAsync(CancellationToken ct)
    {
        // 1. Download while the app stays fully usable. Only the install step needs it gone.
        var asset = await _updater.DownloadUpdateAsync(ct)
                    ?? throw new InvalidOperationException("The update could not be downloaded.");

        // 2. Commit to exiting: from here the updater ends this process itself, once the script that runs the
        //    installer has started.
        handOver();

        // 3. Start the installer's script and terminate (forceTerminate), so msiexec can replace the now unlocked
        //    files, then relaunch. It returns only when the script could not be started.
        try
        {
            if (!await _updater.InstallUpdateAsync(asset, forceTerminate: true, runArguments: null))
                throw new InvalidOperationException($"The installer for {asset.TagVersionStr} could not be started.");
        }
        catch (Exception ex)
        {
            stranded(ex);
        }
    }
}

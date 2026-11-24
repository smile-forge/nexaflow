using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.Wsl.FileActions;

/// <summary>
/// Opens the WSL shell page at the selected or current folder of a WSL distro, in the distro's own shell. Offered only on a
/// distro the runtime has mounted with a shell — never on this computer's folders, whose prompt is the Console's.
/// </summary>
public sealed class ShellHereAction(IShellServices shell, ILocationDirectory locations, IVirtualFileSystem files)
    : IFolderAction, ICacheable
{


    public bool     IsDestructive         => false;
    public bool     SupportsMultipleFiles => false;
    public IconRef  Icon                  => IconRef.Emoji("🐧");
    public string   DisplayName           => Str.Get("Wsl.Action.ShellHere");
    public bool     RequiresRefresh       => false;
    public bool     CanPerformAction      => true;
    public bool     AppliesToRoot         => true;
    public bool     AppliesToDrives       => true;
    public string?  Tooltip               => Str.Get("Wsl.Action.ShellHereTip");

    /// <summary>The folder is on a mounted WSL distro with a shell. Read from what the runtime knows, without the network.</summary>
    public bool AppliesToFolder(string folderPath)
        => (files.GetCapabilities(folderPath) & LocationCapabilities.Shell) != 0
           && DistroPlaces.Holds(locations, folderPath);

    public Task<bool> PerformActionAsync(string folderPath, CancellationToken ct = default)
    {
        shell.OpenTab(WslShellTabRegistration.StaticPageKind, new Dictionary<string, string> { ["path"] = folderPath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> folderPaths, CancellationToken ct = default)
        => folderPaths.FirstOrDefault() is { } first ? PerformActionAsync(first, ct) : Task.FromResult(false);
}

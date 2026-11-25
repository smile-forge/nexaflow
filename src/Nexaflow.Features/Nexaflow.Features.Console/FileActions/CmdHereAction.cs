using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Localization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Console.FileActions;

/// <summary>
/// Opens a Console tab — the Windows command prompt — rooted at the selected or current folder, wherever this disk holds it
/// whole: a drive's folder, or a mounted one. Not offered on a location.
/// </summary>
public sealed class CmdHereAction : IFolderAction, ICacheable
{
    private readonly IShellServices     _shellServices;
    private readonly IVirtualFileSystem _files;

    public CmdHereAction(IShellServices shellServices, IVirtualFileSystem files)
    {
        _shellServices = shellServices;
        _files         = files;
    }

    public bool     IsDestructive         => false;
    public bool     SupportsMultipleFiles => false;
    public IconRef  Icon                  => IconRef.Emoji("⌨");
    public string   DisplayName           => Str.Get("Console.Action.CmdHere");
    public bool     RequiresRefresh       => false;
    public bool     CanPerformAction      => true;
    public bool     AppliesToRoot         => true;
    public bool     AppliesToDrives       => true;
    public string?  Tooltip               => Str.Get("Console.Action.CmdHereTip");

    /// <summary>cmd reaches the folder: this disk holds it whole. A location's folder is not cmd's — a WSL distro's, though
    /// Windows shows it as a folder, has its own shell's action. Read from what the file system knows, without the network.</summary>
    public bool AppliesToFolder(string folderPath)
        => !_files.IsOnLocation(folderPath) && _files.ResolveOnDisk(folderPath) is not null;

    public Task<bool> PerformActionAsync(string folderPath, CancellationToken ct = default)
    {
        _shellServices.OpenTab("Console",
            new Dictionary<string, string> { ["path"] = folderPath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> folderPaths, CancellationToken ct = default)
    {
        foreach (var path in folderPaths)
            return PerformActionAsync(path, ct);
        return Task.FromResult(false);
    }
}

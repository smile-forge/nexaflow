using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Console.FileActions;

/// <summary>
/// Opens a Console tab rooted at the selected or current folder — in the location's own shell on a location that has one.
/// Offered wherever a shell reaches the folder: on this disk, on a location with a shell, or on one this disk holds whole.
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

    /// <summary>A shell reaches the folder: the location's own (<see cref="LocationCapabilities.Shell"/>), or cmd where this
    /// disk holds the folder whole. Read from what the file system knows, without the network.</summary>
    public bool AppliesToFolder(string folderPath)
        => (_files.GetCapabilities(folderPath) & LocationCapabilities.Shell) != 0
           || _files.ResolveOnDisk(folderPath) is not null;

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

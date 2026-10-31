using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Console.FileActions;

/// <summary>
/// Opens a Console tab rooted at the selected or current folder.
/// </summary>
public sealed class CmdHereAction : IFolderAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public CmdHereAction(IShellServices shellServices)
        => _shellServices = shellServices;

    public bool     IsDestructive         => false;
    public bool     SupportsMultipleFiles => false;
    public IconRef  Icon                  => IconRef.Emoji("⌨");
    public string   DisplayName           => Str.Get("Console.Action.CmdHere");
    public bool     RequiresRefresh       => false;
    public bool     CanPerformAction      => true;
    public bool     AppliesToRoot         => true;
    public bool     AppliesToDrives       => true;
    public string?  Tooltip               => Str.Get("Console.Action.CmdHereTip");

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

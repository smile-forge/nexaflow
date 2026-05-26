using Nexaflow.Features.Common;
using System.Collections.Generic;
using System.Windows.Media;

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
    public string   Icon                  => "⌨";
    public string   DisplayName           => "Cmd Here";
    public bool     RequiresRefresh       => false;
    public bool     CanPerformAction      => true;
    public bool     AppliesToRoot         => true;
    public bool     AppliesToDrives       => true;
    public string?  Tooltip               => "Open a Console tab in this folder";
    public ImageSource? IconImage         => null;

    public bool PerformAction(string folderPath)
    {
        _shellServices.OpenTab("Console",
            new Dictionary<string, string> { ["path"] = folderPath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> folderPaths)
    {
        foreach (var path in folderPaths)
            return PerformAction(path);
        return false;
    }
}

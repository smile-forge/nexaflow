using Nexaflow.Features.Common;

namespace Nexaflow.Features.Logs.FileActions;

/// <summary>
/// File-tree context action that opens a log file in a new Logs tab.
/// </summary>
public sealed class ShowLogAction : IFileAction
{
    private readonly ITabOpener _tabOpener;

    public ShowLogAction(ITabOpener tabOpener) => _tabOpener = tabOpener;

    public string DisplayName          => "Open Log";
    public string Icon                 => "📋";
    public string SupportedFileTypes   => "*.log";
    public string SupportedFolderNames => string.Empty;
    public bool   AppliesToFolders     => false;
    public bool   AppliesToRoot        => false;
    public bool   AppliesToDrives      => false;
    public bool   IsDestructive        => false;
    public bool   SupportsMultipleFiles => false;
    public bool   RequiresRefresh      => false;
    public bool   CanPerformAction     => true;

    public bool PerformAction(string filePath)
    {
        _tabOpener.OpenTab("Logs", new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _tabOpener.OpenTab("Logs", new Dictionary<string, string> { ["path"] = path });
            return true; // open first file only (SupportsMultipleFiles = false)
        }
        return false;
    }
}

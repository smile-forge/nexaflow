using Nexaflow.Features.Common;
using System.Collections.Generic;

namespace Nexaflow.Features.Text.FileActions;

/// <summary>
/// Opens a text (.txt) file in a new TextView tab.
/// </summary>
public sealed class ShowTextAction : IFileAction
{
    private readonly ITabOpener _tabOpener;

    public ShowTextAction(ITabOpener tabOpener) => _tabOpener = tabOpener;

    public bool   IsDestructive          => false;
    public bool   SupportsMultipleFiles  => false;
    public string Icon                   => "📄";
    public string DisplayName            => "Show";
    public string ExperienceId           => "/text";
    public string ExperienceDescription  => "Lightweight Text editor";
    public bool   RequiresRefresh        => false;
    public bool   CanPerformAction       => true;

    public bool PerformAction(string filePath)
    {
        _tabOpener.OpenTab("Text", new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _tabOpener.OpenTab("Text", new Dictionary<string, string> { ["path"] = path });
            return true;
        }
        return false;
    }
}

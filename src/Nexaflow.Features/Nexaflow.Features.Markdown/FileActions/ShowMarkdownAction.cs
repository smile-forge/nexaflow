using Nexaflow.Features.Common;
using System.Collections.Generic;

namespace Nexaflow.Features.Markdown.FileActions;

/// <summary>
/// Opens a Markdown (.md / .markdown) file in a new live-edit MarkdownView tab.
/// </summary>
public class ShowMarkdownAction : IFileAction
{
    private readonly ITabOpener _tabOpener;

    public ShowMarkdownAction(ITabOpener tabOpener) => _tabOpener = tabOpener;

    public bool   IsDestructive          => false;
    public bool   SupportsMultipleFiles  => false;
    public string Icon                   => "📝";
    public string DisplayName            => "Show";
    public string ExperienceId           => "/text/markdown";
    public string ExperienceDescription  => "Markdown editor";
    public bool   RequiresRefresh        => false;
    public bool   CanPerformAction       => true;

    public bool PerformAction(string filePath)
    {
        _tabOpener.OpenTab("Markdown", new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _tabOpener.OpenTab("Markdown", new Dictionary<string, string> { ["path"] = path });
            return true;
        }
        return false;
    }
}

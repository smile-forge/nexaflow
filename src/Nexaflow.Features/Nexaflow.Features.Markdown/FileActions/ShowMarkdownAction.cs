using Nexaflow.Features.Common;
using System.Collections.Generic;

namespace Nexaflow.Features.Markdown.FileActions;

/// <summary>
/// Opens a Markdown (.md / .markdown) file in a new live-edit MarkdownView tab.
/// </summary>
public class ShowMarkdownAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public ShowMarkdownAction(IShellServices shellServices) => _shellServices = shellServices;

    public bool   IsDestructive          => false;
    public bool   SupportsMultipleFiles  => false;
    public string Icon                   => "📝";
    public string DisplayName            => "Show";
    public static string? StaticExperienceId => "/text/markdown";
    public string ExperienceId           => "/text/markdown";
    public string ExperienceDescription  => "Markdown editor";
    public bool   RequiresRefresh        => false;
    public bool   CanPerformAction       => true;

    public bool PerformAction(string filePath)
    {
        _shellServices.OpenTab("Markdown", new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Markdown", new Dictionary<string, string> { ["path"] = path });
            return true;
        }
        return false;
    }
}

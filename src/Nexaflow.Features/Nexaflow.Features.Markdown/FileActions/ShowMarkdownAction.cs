using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    public string DisplayName            => "Markdown";
    public static string? StaticExperienceId => "/text/markdown";
    public string ExperienceId           => "/text/markdown";
    public string ExperienceDescription  => Str.Get("Markdown.Action.EditorDescription");
    public bool   RequiresRefresh        => false;
    public bool   CanPerformAction       => true;
    public bool   OpensViewer            => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        _shellServices.OpenTab("Markdown", new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Markdown", new Dictionary<string, string> { ["path"] = path });
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}

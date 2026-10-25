using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Localization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.Notebook.FileActions;

/// <summary>
/// Opens an <c>.ipynb</c> in the Notebook viewer: cells rendered in order — markdown as markdown, code
/// syntax-highlighted per the kernel language — with a code-structure outline. The right home for a
/// notebook, which is a structured cell document rather than a flat source file.
/// </summary>
public sealed class ShowNotebookAction : IFileAction, ICacheable
{
    private readonly IShellServices _shellServices;

    public ShowNotebookAction(IShellServices shellServices) => _shellServices = shellServices;

    public bool   IsDestructive          => false;
    public bool   SupportsMultipleFiles  => false;
    public string Icon                   => "📓";
    public string DisplayName            => Str.Get("Notebook.Action.AsNotebook");
    public static string? StaticExperienceId => "/notebook";
    public string ExperienceId           => "/notebook";
    public string ExperienceDescription  => Str.Get("Notebook.Action.ExperienceDescription");
    public bool   RequiresRefresh        => false;
    public bool   CanPerformAction       => true;
    public bool   OpensViewer            => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        _shellServices.OpenTab("Notebook", new Dictionary<string, string> { ["path"] = filePath });
        return Task.FromResult(true);
    }

    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Notebook", new Dictionary<string, string> { ["path"] = path });
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }
}

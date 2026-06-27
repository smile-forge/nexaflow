using Nexaflow.Features.Common;
using System.Collections.Generic;

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
    public string DisplayName            => "As Notebook";
    public static string? StaticExperienceId => "/notebook";
    public string ExperienceId           => "/notebook";
    public string ExperienceDescription  => "Jupyter notebook viewer: cells, rendered markdown, highlighted code, and an outline";
    public bool   RequiresRefresh        => false;
    public bool   CanPerformAction       => true;
    public bool   OpensViewer            => true;

    public bool PerformAction(string filePath)
    {
        _shellServices.OpenTab("Notebook", new Dictionary<string, string> { ["path"] = filePath });
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths)
        {
            _shellServices.OpenTab("Notebook", new Dictionary<string, string> { ["path"] = path });
            return true;
        }
        return false;
    }
}

using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.Model;

namespace Nexaflow.Features.Projects.ViewModels;

/// <summary>The backlog viewlet over a project folder: how many backlog items stand in each status, and the way into the project.</summary>
public sealed class BacklogStatusViewletViewModel
{
    /// <summary>One status and how many items stand in it; <see cref="SwatchKey"/> names its colour in the theme's swatch bank.</summary>
    public sealed record StatusTally(string Label, string SwatchKey, int Count);

    private readonly IShellServices _shell;
    private readonly string _folderPath;

    public BacklogStatusViewletViewModel(ProjectsConfig config, IShellServices shell, string folderPath)
    {
        _shell      = shell;
        _folderPath = folderPath;
        try
        {
            var info = ProjectOperations.LoadProjectFile(folderPath);
            Tallies = [.. new ProjectOperations(config).BacklogCountsByStatus(info)
                .Select(count => new StatusTally(count.Def.Label, count.Def.SwatchKey, count.Count))];
        }
        catch
        {
            Tallies = [];   // no project file, or one that cannot be read: nothing to count
        }
    }

    /// <summary>Each status with items in it; empty when the folder's project cannot be read.</summary>
    public IReadOnlyList<StatusTally> Tallies { get; }

    public void OpenProject() => _shell.OpenTab("ProjectDetail", new() { ["path"] = _folderPath });
}

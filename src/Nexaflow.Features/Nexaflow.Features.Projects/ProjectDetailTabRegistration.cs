using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Features.Projects.ViewModels;
using Nexaflow.Features.Projects.Views;

namespace Nexaflow.Features.Projects;

/// <summary>
/// Registers the Project Detail page. Opened by an <b>absolute folder path</b> so a project in any bucket
/// (active / shelf / archive) or surfaced by the folder viewlet can be opened; a legacy <c>folder</c>
/// param (a name under the active project directory) is still accepted for back-compat.
/// FeatureManager injects the shared config + shell + AI service via the constructor.
/// </summary>
public sealed class ProjectDetailTabRegistration : IPageRegistration
{
    private readonly ProjectsConfig  _config;
    private readonly IShellServices  _shellServices;
    private readonly IAIService      _aiService;

    public ProjectDetailTabRegistration(ProjectsConfig config, IShellServices shellServices, IAIService aiService)
    {
        _config        = config;
        _shellServices = shellServices;
        _aiService     = aiService;
    }

    public static string StaticPageKind => "ProjectDetail";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Absolute path of the project folder to open.", Required: false),
        new("folder", "Project folder name under the active project directory (legacy alternative to path).", Required: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var path = pageParams?.GetValueOrDefault("path");
        if (string.IsNullOrWhiteSpace(path))
        {
            var folderParam = pageParams?.GetValueOrDefault("folder") ?? string.Empty;
            path = Path.Combine(_config.ProjectDirectory, folderParam);
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root    = Path.GetDirectoryName(trimmed) ?? _config.ProjectDirectory;
        var folder  = Path.GetFileName(trimmed);

        var tab = new Page
        {
            Title = $"Project {folder}",
            Icon  = "📋",
            Breadcrumbs =
            {
                new BreadcrumbSegment { Label = "Projects", TargetPageKind = "Projects" },
                new BreadcrumbSegment { Label = folder },
            },
        };

        tab.ContentFactory = () =>
        {
            var ops  = new ProjectOperations(_config, root);
            var vm   = new ProjectDetailViewModel(ops, _config, folder, _shellServices, _aiService);
            var page = new ProjectDetailView(vm);
            vm.OpenFilesRequested += p =>
                _shellServices.OpenTab("FileSystem", new() { ["mode"] = "path", ["path"] = p }, page);
            return page;
        };

        return tab;
    }
}

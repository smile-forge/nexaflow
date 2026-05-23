using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Features.Projects.ViewModels;
using Nexaflow.Features.Projects.Views;

namespace Nexaflow.Features.Projects;

/// <summary>
/// Registers the Project Detail page with <see cref="FeatureManager"/>.
/// FeatureManager injects the shared <see cref="ProjectsConfig"/> instance via the constructor.
/// </summary>
public sealed class ProjectDetailTabRegistration : IPageRegistration
{
    private readonly ProjectsConfig  _config;
    private readonly IShellServices  _shellServices;

    public ProjectDetailTabRegistration(ProjectsConfig config, IShellServices shellServices)
    {
        _config        = config;
        _shellServices = shellServices;
    }

    public string PageKind => "ProjectDetail";

    public Page CreatePage(Dictionary<string, string>? pageParams = null)
    {
        var folderName = pageParams?.GetValueOrDefault("folder") ?? string.Empty;
        var ops = new ProjectOperations(_config);
        var vm  = new ProjectDetailViewModel(ops, folderName);
        var tab = new Page
        {
            Title = $"Project {folderName}",
            Icon  = "📋",
            Breadcrumbs =
            {
                new BreadcrumbSegment
                {
                    Label          = "Projects",
                    TargetPageKind = "Projects"
                },
                new BreadcrumbSegment { Label = folderName }
            }
        };

        tab.ContentFactory = () =>
        {
            var page = new ProjectDetailView(vm);
            vm.OpenFilesRequested += path =>
                _shellServices.OpenTab("FileSystem", new() { ["mode"] = "path", ["path"] = path }, page);
            return page;
        };

        return tab;
    }
}

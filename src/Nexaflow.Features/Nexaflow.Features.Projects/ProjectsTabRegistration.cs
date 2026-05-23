using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Features.Projects.ViewModels;
using Nexaflow.Features.Projects.Views;

namespace Nexaflow.Features.Projects;

/// <summary>
/// Registers the Projects list page with <see cref="FeatureManager"/>.
/// FeatureManager injects the shared <see cref="ProjectsConfig"/> instance via the constructor.
/// </summary>
public sealed class ProjectsTabRegistration : ITabRegistration
{
    private readonly ProjectsConfig  _config;
    private readonly IShellServices  _shellServices;

    public ProjectsTabRegistration(ProjectsConfig config, IShellServices shellServices)
    {
        _config        = config;
        _shellServices = shellServices;
    }

    public string PageKind => "Projects";

    public Page CreateTab(Dictionary<string, string>? pageParams = null)
    {
        var ops = new ProjectOperations(_config);
        var vm  = new ProjectsViewModel(ops);
        var tab = new Page
        {
            Title       = "Projects",
            Icon        = "🗂",
            Breadcrumbs = {new BreadcrumbSegment { Label = "Projects" }}
        };

        tab.ContentFactory = () =>
        {
            var page = new ProjectsView(vm);
            vm.OpenProjectRequested += folder =>
                _shellServices.OpenTab("ProjectDetail", new() { ["folder"] = folder }, page);
            vm.OpenFilesRequested += path =>
                _shellServices.OpenTab("FileSystem", new() { ["mode"] = "path", ["path"] = path }, page);
            return page;
        };

        return tab;
    }
}

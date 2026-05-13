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
    private readonly ProjectsConfig _config;
    private readonly ITabOpener     _tabOpener;

    public ProjectsTabRegistration(ProjectsConfig config, ITabOpener tabOpener)
    {
        _config    = config;
        _tabOpener = tabOpener;
    }

    public string PageKind => "Projects";

    public TabEntry CreateTab(Dictionary<string, string>? pageParams = null)
    {
        var ops = new ProjectOperations(_config);
        var vm  = new ProjectsViewModel(ops);
        var tab = new TabEntry
        {
            Title       = "Projects",
            Icon        = "🗂",
            Breadcrumbs = [new BreadcrumbSegment { Label = "Projects" }]
        };

        tab.PageFactory = () =>
        {
            var page = new ProjectsView(vm);
            vm.OpenProjectRequested += folder =>
                _tabOpener.OpenTab("ProjectDetail", new() { ["folder"] = folder });
            vm.OpenFilesRequested += path =>
                _tabOpener.OpenTab("FileSystem", new() { ["mode"] = "path", ["path"] = path });
            return page;
        };

        return tab;
    }
}

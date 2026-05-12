using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.ViewModels;
using Nexaflow.Features.Projects.Views;

namespace Nexaflow.Features.Projects;

/// <summary>
/// Registers the Projects list page with <see cref="FeatureManager"/>.
/// </summary>
public sealed class ProjectsTabRegistration : ITabRegistration
{
    public string PageKind => "Projects";

    public TabEntry CreateTab(Dictionary<string, string>? pageParams = null)
    {
        var vm  = new ProjectsViewModel();
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
                FeatureManager.Instance.RequestTab("ProjectDetail", new() { ["folder"] = folder });
            vm.OpenFilesRequested += path =>
                FeatureManager.Instance.RequestTab("FileSystem", new() { ["mode"] = "path", ["path"] = path });
            return page;
        };

        return tab;
    }
}

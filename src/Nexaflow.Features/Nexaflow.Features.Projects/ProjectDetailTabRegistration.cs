using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.ViewModels;
using Nexaflow.Features.Projects.Views;

namespace Nexaflow.Features.Projects;

/// <summary>
/// Registers the Project Detail page with <see cref="FeatureManager"/>.
/// </summary>
public sealed class ProjectDetailTabRegistration : ITabRegistration
{
    public string PageKind => "ProjectDetail";

    public TabEntry CreateTab(Dictionary<string, string>? pageParams = null)
    {
        var folderName = pageParams?.GetValueOrDefault("folder") ?? string.Empty;
        var vm  = new ProjectDetailViewModel(folderName);
        var tab = new TabEntry
        {
            Title = $"Project {folderName}",
            Icon  = "📋",
            Breadcrumbs =
            [
                new BreadcrumbSegment
                {
                    Label          = "Projects",
                    TargetPageKind = "Projects"
                },
                new BreadcrumbSegment { Label = folderName }
            ]
        };

        tab.PageFactory = () =>
        {
            var page = new ProjectDetailView(vm);
            vm.OpenFilesRequested += path =>
                FeatureManager.Instance.RequestTab("FileSystem", new() { ["mode"] = "path", ["path"] = path });
            return page;
        };

        return tab;
    }
}

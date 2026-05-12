using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Features.Projects.ViewModels;
using Nexaflow.Features.Projects.Views;

namespace Nexaflow.Features.Projects;

/// <summary>
/// Registers the Project Detail page with <see cref="FeatureManager"/>.
/// FeatureManager injects the shared <see cref="ProjectsConfig"/> instance via the constructor.
/// </summary>
public sealed class ProjectDetailTabRegistration : ITabRegistration
{
    private readonly ProjectsConfig _config;

    public ProjectDetailTabRegistration(ProjectsConfig config) => _config = config;

    public string PageKind => "ProjectDetail";

    public TabEntry CreateTab(Dictionary<string, string>? pageParams = null)
    {
        var folderName = pageParams?.GetValueOrDefault("folder") ?? string.Empty;
        var ops = new ProjectOperations(_config);
        var vm  = new ProjectDetailViewModel(ops, folderName);
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

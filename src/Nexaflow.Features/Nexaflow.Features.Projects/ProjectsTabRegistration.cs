using Nexaflow.Features.Common;
using Nexaflow.Features.Projects.Model;
using Nexaflow.Features.Projects.ViewModels;
using Nexaflow.Features.Projects.Views;

namespace Nexaflow.Features.Projects;

/// <summary>
/// Registers the Projects list page with <see cref="FeatureManager"/>.
/// FeatureManager injects the shared <see cref="ProjectsConfig"/> instance via the constructor.
/// </summary>
public sealed class ProjectsTabRegistration : IPageRegistration
{
    private readonly ProjectsConfig  _config;
    private readonly IShellServices  _shellServices;

    public ProjectsTabRegistration(ProjectsConfig config, IShellServices shellServices)
    {
        _config        = config;
        _shellServices = shellServices;
    }

    public static string StaticPageKind => "Projects";
    public string PageKind => StaticPageKind;

    /// <summary>The Projects landing page needs no params; offer it as an AI context item when enabled.</summary>
    public bool CanBeContextItem => _config.EnableProjects;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var tab = new Page
        {
            Title       = "Projects",
            Icon        = "🗂",
            Breadcrumbs = {new BreadcrumbSegment { Label = "Projects" }}
        };

        // Build the operations + VM lazily so CreatePageDefinition stays side-effect-free (the VM's ctor loads
        // projects) — callers may peek a page's Title/Icon without realizing its content.
        tab.ContentFactory = () =>
        {
            var vm   = new ProjectsViewModel(_config, _shellServices);
            var page = new ProjectsView(vm);
            vm.OpenProjectRequested += path =>
                _shellServices.OpenTab("ProjectDetail", new() { ["path"] = path }, page);
            vm.OpenFilesRequested += path =>
                _shellServices.OpenTab("FileSystem", new() { ["mode"] = "path", ["path"] = path }, page);
            return page;
        };

        return tab;
    }
}

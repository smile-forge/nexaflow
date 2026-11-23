using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.ViewModels;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.SystemInfo;

public sealed class EnvironmentVariablesPageRegistration(IShellServices shellServices) : IPageRegistration
{
    public static string StaticPageKind => "SystemEnvVars";
    public string PageKind => StaticPageKind;

    /// <summary>Standalone page (no params) — offer it to the AI "add context" menu.</summary>
    public bool CanBeContextItem => true;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var page = new Page
        {
            Title       = Str.Get("SystemInfo.Page.EnvVars"),
            Icon        = "🧬",
            Breadcrumbs = { new BreadcrumbSegment { Label = Str.Get("SystemInfo.Page.EnvVars") } },
        };
        page.ViewModelFactory = () =>
        {
            var vm = new EnvironmentVariablesViewModel(shellServices);
            return vm;
        };
        return page;
    }
}

using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsApps.ViewModels;
using Nexaflow.Features.WindowsApps.Views;

namespace Nexaflow.Features.WindowsApps;

public sealed class WindowsAppsTabRegistration(IShellServices shellServices) : IPageRegistration
{
    public static string StaticPageKind => "WindowsApps";
    public string PageKind => StaticPageKind;

    /// <summary>Standalone landing page (no params) — offer it as an AI context item.</summary>
    public bool CanBeContextItem => true;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null) => new()
    {
        Title       = "Installed Apps",
        Icon        = "📦",
        Breadcrumbs = { new BreadcrumbSegment { Label = "Installed Apps" } },
        ContentFactory = () => new WindowsAppsView(new WindowsAppsViewModel(shellServices))
    };
}

using Nexaflow.Features.Common;
using Nexaflow.Features.SystemInfo.ViewModels;
using Nexaflow.Features.SystemInfo.Views;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.SystemInfo;

public sealed class ServicesPageRegistration(IShellServices shellServices) : IPageRegistration
{
    public static string StaticPageKind => "SystemServices";
    public string PageKind => StaticPageKind;

    /// <summary>Standalone page (no params) — offer it to the AI "add context" menu.</summary>
    public bool CanBeContextItem => true;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var page = new Page
        {
            Title       = Str.Get("SystemInfo.Page.Services"),
            Icon        = "⚙️",
            Breadcrumbs = { new BreadcrumbSegment { Label = Str.Get("SystemInfo.Page.Services") } },
        };
        // Capture the page so background work is cancelled when the tab is permanently closed.
        page.ContentFactory = () =>
        {
            var vm = new ServicesViewModel(shellServices);
            page.Closed += (_, _) => vm.Dispose();
            return new ServicesView(vm);
        };
        return page;
    }
}

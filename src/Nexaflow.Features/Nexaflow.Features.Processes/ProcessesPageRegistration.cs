using Nexaflow.Features.Common;
using Nexaflow.Features.Processes.ViewModels;
using Nexaflow.Features.Processes.Views;

namespace Nexaflow.Features.Processes;

/// <summary>Advertises the "Processes" page — the live process tree. Standalone (no params), so it's
/// offered as an AI context item and in the ribbon editor.</summary>
public sealed class ProcessesPageRegistration(IShellServices shellServices) : IPageRegistration
{
    public static string StaticPageKind => "Processes";
    public string PageKind => StaticPageKind;
    public bool CanBeContextItem => true;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var page = new Page
        {
            Title       = "Processes",
            Icon        = "⚙",
            Breadcrumbs = { new BreadcrumbSegment { Label = "Processes" } },
        };
        page.ContentFactory = () =>
        {
            var vm = new ProcessesViewModel(shellServices);
            page.Closed += (_, _) => vm.Dispose();
            return new ProcessesView(vm);
        };
        return page;
    }
}

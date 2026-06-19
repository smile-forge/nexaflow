using Nexaflow.Features.Common;
using Nexaflow.Features.Processes.ViewModels;
using Nexaflow.Features.Processes.Views;

namespace Nexaflow.Features.Processes;

/// <summary>Advertises the "ProcessDetail" page — the per-process inspector opened by the list's
/// "View details". Requires a <c>pid</c> param, so it's not a standalone context item.</summary>
public sealed class ProcessDetailPageRegistration(IShellServices shellServices) : IPageRegistration
{
    public static string StaticPageKind => "ProcessDetail";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
        [new PageParameter("pid", "The process id to inspect.")];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        int pid = ParsePid(pageParams);
        var page = new Page
        {
            Title       = $"Process {pid}",
            Icon        = "🔬",
            Breadcrumbs = { new BreadcrumbSegment { Label = $"Process {pid}" } },
            PageParams  = pageParams,
        };
        page.ContentFactory = () =>
        {
            var vm = new ProcessDetailViewModel(shellServices, pid);
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ProcessDetailViewModel.Title)) page.Title = vm.Title;
            };
            page.Closed += (_, _) => vm.Dispose();
            return new ProcessDetailView(vm);
        };
        return page;
    }

    private static int ParsePid(Dictionary<string, string>? p)
        => p is not null && p.TryGetValue("pid", out var s) && int.TryParse(s, out var pid) ? pid : 0;
}

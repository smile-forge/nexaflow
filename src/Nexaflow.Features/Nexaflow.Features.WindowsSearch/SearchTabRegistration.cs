using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsSearch.ViewModels;
using Nexaflow.Features.WindowsSearch.Views;

namespace Nexaflow.Features.WindowsSearch;

public sealed class SearchTabRegistration(IShellServices shellServices) : ITabRegistration
{
    public string PageKind => "Search";

    public TabEntry CreateTab(Dictionary<string, string>? pageParams = null)
    {
        var query = pageParams?.GetValueOrDefault("query") ?? string.Empty;
        var root  = pageParams?.GetValueOrDefault("root")  ?? string.Empty;

        var rootLabel  = string.IsNullOrEmpty(root)
            ? "Search"
            : Path.GetFileName(root.TrimEnd('\\', '/'));

        var queryShort = query.Length > 12 ? query[..12] + "…" : query;

        var tabTitle = string.IsNullOrWhiteSpace(query) ? "Search" : queryShort;

        var vm = new SearchViewModel(query, root, shellServices);

        var tab = new TabEntry
        {
            Title      = tabTitle,
            Icon       = "🔍",
            PageParams = new() { ["query"] = query, ["root"] = root },
            Breadcrumbs =
            [
                new BreadcrumbSegment { Label = rootLabel },
                new BreadcrumbSegment { Label = $"Query : {queryShort}" }
            ]
        };
        vm.Tab = tab;
        tab.PageFactory = () => new SearchView(vm);
        return tab;
    }
}

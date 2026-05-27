using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsSearch.ViewModels;
using Nexaflow.Features.WindowsSearch.Views;

namespace Nexaflow.Features.WindowsSearch;

public sealed class SearchTabRegistration(IShellServices shellServices) : IPageRegistration
{
    public static string StaticPageKind => "Search";
    public string PageKind => StaticPageKind;

    public Page CreatePage(Dictionary<string, string>? pageParams = null)
    {
        var query  = pageParams?.GetValueOrDefault("query")  ?? string.Empty;
        var root   = pageParams?.GetValueOrDefault("root")   ?? string.Empty;
        var drives = pageParams?.GetValueOrDefault("drives") ?? string.Empty;

        var driveList = string.IsNullOrEmpty(drives)
            ? (IReadOnlyList<string>)[]
            : drives.Split(';', StringSplitOptions.RemoveEmptyEntries);

        var rootLabel  = string.IsNullOrEmpty(root)
            ? (driveList.Count > 0 ? "This PC" : "Search")
            : Path.GetFileName(root.TrimEnd('\\', '/'));

        var queryShort = query.Length > 12 ? query[..12] + "…" : query;

        var tabTitle = string.IsNullOrWhiteSpace(query) ? "Search" : queryShort;

        var vm = new SearchViewModel(query, root, driveList, shellServices);

        var tab = new Page
        {
            Title      = tabTitle,
            Icon       = "🔍",
            PageParams = new() { ["query"] = query, ["root"] = root, ["drives"] = drives },
            Breadcrumbs =
            {
                new BreadcrumbSegment { Label = rootLabel },
                new BreadcrumbSegment { Label = $"Query : {queryShort}" }
            }
        };
        vm.Tab = tab;
        tab.ContentFactory = () => new SearchView(vm);
        return tab;
    }
}

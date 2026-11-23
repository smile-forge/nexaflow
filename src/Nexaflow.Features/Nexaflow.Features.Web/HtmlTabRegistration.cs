using Nexaflow.Features.Common;
using Nexaflow.Features.Web.ViewModels;
using Nexaflow.IO.Common;
using System.Collections.Generic;

namespace Nexaflow.Features.Web;

/// <summary>
/// Registers the HTML viewer page with <see cref="FeatureManager"/>.
/// Accepts a "path" page parameter containing the file path or URL to display.
/// The tab title and breadcrumb track the current page (see <see cref="WebPageChrome"/>).
/// </summary>
public sealed class HtmlTabRegistration(IShellServices shell, IVirtualFileSystem vfs) : IPageRegistration
{
    public static string StaticPageKind => "Html";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Full path of an HTML file, or a web address (http or https).", OnDisk: true),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var url  = pageParams?.GetValueOrDefault("path") ?? "";
        var page = new Page
        {
            Icon  = "🌐",
            Title = WebPageChrome.Title(url, null),
        };
        // Initial crumbs are static (nothing navigated yet); they're rebuilt with live navigation once the
        // first navigation fires.
        foreach (var crumb in WebPageChrome.Crumbs(url, _ => { }))
            page.Breadcrumbs.Add(crumb);

        page.ViewModelFactory = () =>
        {
            var vm = new HtmlViewModel(url, vfs, shell);
            vm.PageChanged += () => UpdateChrome(page, vm);
            return vm;
        };
        return page;
    }

    /// <summary>Refreshes the tab title and breadcrumb from the page's current URL + page title.</summary>
    private static void UpdateChrome(Page page, HtmlViewModel vm)
    {
        var url = vm.CurrentUrl;
        if (string.IsNullOrEmpty(url)) return;

        page.Title = WebPageChrome.Title(url, vm.PageTitle);

        page.Breadcrumbs.Clear();
        foreach (var crumb in WebPageChrome.Crumbs(url, vm.NavigateTo))
            page.Breadcrumbs.Add(crumb);
    }
}

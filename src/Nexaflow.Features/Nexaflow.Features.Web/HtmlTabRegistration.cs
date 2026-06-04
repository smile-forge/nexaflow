using Nexaflow.Features.Common;
using Nexaflow.Features.Web.ViewModels;
using Nexaflow.Features.Web.Views;
using System.Collections.Generic;

namespace Nexaflow.Features.Web;

/// <summary>
/// Registers the HTML viewer page with <see cref="FeatureManager"/>.
/// Accepts a "path" page parameter containing the file path or URL to display.
/// The tab title and breadcrumb track the current page (see <see cref="WebPageChrome"/>).
/// </summary>
public sealed class HtmlTabRegistration : IPageRegistration
{
    public static string StaticPageKind => "Html";
    public string PageKind => StaticPageKind;

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var url  = pageParams?.GetValueOrDefault("path") ?? "";
        var page = new Page
        {
            Icon  = "🌐",
            Title = WebPageChrome.Title(url, null),
        };
        // Initial crumbs are static (no view yet); they're rebuilt with live navigation once the
        // view exists and the first navigation fires.
        foreach (var crumb in WebPageChrome.Crumbs(url, _ => { }))
            page.Breadcrumbs.Add(crumb);

        page.ContentFactory = () =>
        {
            var view = new HtmlView(new HtmlViewModel(url));
            view.PageChanged += () => UpdateChrome(page, view);
            return view;
        };
        return page;
    }

    /// <summary>Refreshes the tab title and breadcrumb from the view's current URL + page title.</summary>
    private static void UpdateChrome(Page page, HtmlView view)
    {
        var url = view.ViewModel.CurrentUrl;
        if (string.IsNullOrEmpty(url)) return;

        page.Title = WebPageChrome.Title(url, view.ViewModel.PageTitle);

        page.Breadcrumbs.Clear();
        foreach (var crumb in WebPageChrome.Crumbs(url, view.NavigateTo))
            page.Breadcrumbs.Add(crumb);
    }
}

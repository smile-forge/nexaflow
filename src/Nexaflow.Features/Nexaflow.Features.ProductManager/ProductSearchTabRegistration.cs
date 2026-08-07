using System.Collections.Generic;
using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.ProductManager.ViewModels;
using Nexaflow.Features.ProductManager.Views;
using Nexaflow.Services.Initiatives.Product.Services;

namespace Nexaflow.Features.ProductManager;

/// <summary>
/// Registers the graph search results page (kind <c>ProductSearch</c>) — where a "?" typed on the Product
/// tab lands.
/// <para>
/// A page of its own because a graph search answers with things the sunburst cannot draw: a type, a file, a
/// line of code. The tab is identified by <c>path</c> alone (not the query), so searching twice re-points
/// the one results tab instead of stacking a tab per query.
/// </para>
/// </summary>
public sealed class ProductSearchTabRegistration(IShellServices shell) : IPageRegistration
{
    public static string StaticPageKind => "ProductSearch";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Folder containing the .product/ directory whose graph is searched.", Required: true),
        new("query", "What to search the graph for.", Required: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var root = ProductRootLocator.Resolve(pageParams) ?? string.Empty;

        // The query is a one-shot navigation hint, not part of the tab's identity — same rule as the Product
        // tab's `node`. A tab keyed on {path,query} would stop matching the next query and open a second
        // results tab for every search.
        string query = string.Empty;
        if (pageParams is not null && pageParams.Remove("query", out var requested)) query = requested;

        var name  = root.Length == 0 ? "Product" : new DirectoryInfo(root).Name;
        var title = $"Search: {name}";

        return new Page
        {
            Title       = title,
            Icon        = "🔎",
            Breadcrumbs = { new BreadcrumbSegment { Label = title } },
            ContentFactory = () => new ProductSearchView(new ProductSearchViewModel(root, query, shell)),
        };
    }
}

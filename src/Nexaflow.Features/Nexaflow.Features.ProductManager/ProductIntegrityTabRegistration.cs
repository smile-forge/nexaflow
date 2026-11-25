using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.ProductManager.ViewModels;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Localization;

namespace Nexaflow.Features.ProductManager;

/// <summary>
/// Registers the Integrity page (kind <c>ProductIntegrity</c>): the broken snaplinks of the product whose
/// <c>.product/</c> lives at <c>path</c>. Opened from the Product tab's ⋮ menu or its root integrity tile.
/// A page rather than an overlay, because a whole-tree scan is slow enough to want its own tab.
/// </summary>
public sealed class ProductIntegrityTabRegistration : IPageRegistration
{
    private readonly IShellServices _shell;

    public ProductIntegrityTabRegistration(IShellServices shell) => _shell = shell;

    public static string StaticPageKind => "ProductIntegrity";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Folder containing the .product/ directory to validate.", Required: false, OnDisk: true),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var root  = ProductRootLocator.Resolve(pageParams);
        var title = root is not null
            ? Str.Format("ProductManager.Integrity.Page.TitleFormat", ProductStore.NameOfRoot(root))
            : Str.Get("ProductManager.Integrity.Page.Title");

        var tab = new Page
        {
            Title       = title,
            Icon        = "🔗",
            Breadcrumbs = { new BreadcrumbSegment { Label = title } }
        };

        tab.ViewModelFactory = () =>
        {
            if (string.IsNullOrWhiteSpace(root) || !ProductStore.Exists(root))
                return new PlaceholderViewModel(Str.Get("ProductManager.Integrity.Page.NoProduct"));
            return new ProductIntegrityViewModel(new ProductStore(root), root, _shell, tab);
        };

        return tab;
    }
}

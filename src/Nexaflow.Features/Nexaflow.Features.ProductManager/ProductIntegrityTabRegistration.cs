using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.ProductManager.ViewModels;
using Nexaflow.Features.ProductManager.Views;
using Nexaflow.Services.Initiatives.Product.Services;
using Page = Nexaflow.Features.Common.Page;

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
        new("path", "Folder containing the .product/ directory to validate.", Required: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var root  = ProductRootLocator.Resolve(pageParams);
        var title = root is not null ? $"Integrity: {new DirectoryInfo(root).Name}" : "Integrity";

        var tab = new Page
        {
            Title       = title,
            Icon        = "🔗",
            Breadcrumbs = { new BreadcrumbSegment { Label = title } }
        };

        tab.ContentFactory = () =>
        {
            if (string.IsNullOrWhiteSpace(root) || !ProductStore.Exists(root)) return Placeholder();
            return new ProductIntegrityView(new ProductIntegrityViewModel(new ProductStore(root), root, _shell));
        };

        return tab;
    }

    private static UserControl Placeholder() => new()
    {
        Content = new TextBlock
        {
            Text = "Open Integrity from a folder that contains a .product directory.",
            Margin = new Thickness(24),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Application.Current?.Resources["TextMutedBrush"] as System.Windows.Media.Brush
        }
    };
}

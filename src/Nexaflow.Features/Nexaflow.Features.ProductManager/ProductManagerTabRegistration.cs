using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.ProductManager.Services;
using Nexaflow.Features.ProductManager.ViewModels;
using Nexaflow.Features.ProductManager.Views;
using Page = Nexaflow.Features.Common.Page;

namespace Nexaflow.Features.ProductManager;

/// <summary>
/// Registers the single Product page (kind <c>ProductManager</c>). A product is the folder its
/// <c>.product/</c> sits in, so there is no config — the folder arrives via the <c>path</c> param (from the
/// folder viewlet). FeatureManager injects only <see cref="IShellServices"/>.
/// </summary>
public sealed class ProductManagerTabRegistration : IPageRegistration
{
    private readonly IShellServices _shell;

    public ProductManagerTabRegistration(IShellServices shell) => _shell = shell;

    public static string StaticPageKind => "ProductManager";
    public string PageKind => StaticPageKind;

    public IReadOnlyList<PageParameter> Parameters =>
    [
        new("path", "Folder containing (or to initialise) the .product/ directory.", Required: false),
    ];

    public Page CreatePageDefinition(Dictionary<string, string>? pageParams = null)
    {
        var root  = ProductRootLocator.Resolve(pageParams);
        var title = root is not null ? $"Product: {new DirectoryInfo(root).Name}" : "Product";

        var tab = new Page
        {
            Title       = title,
            Icon        = "🧭",
            Breadcrumbs = { new BreadcrumbSegment { Label = title } }
        };

        tab.ContentFactory = () =>
        {
            if (string.IsNullOrWhiteSpace(root)) return Placeholder();

            var store = new ProductStore(root);
            if (!ProductStore.Exists(root)) store.Initialize(new DirectoryInfo(root).Name);

            var vm = new ProductViewModel(store, new ProductGit(root), root, _shell);
            vm.NavigationChanged += segments => ApplyBreadcrumbs(tab, vm, segments);
            ApplyBreadcrumbs(tab, vm, vm.CurrentPath);    // initial path (built during the VM ctor)
            return new ProductView(vm);
        };

        return tab;
    }

    // The tab's breadcrumb bar reflects the sunburst focus path; each crumb focuses that node (last is current).
    private static void ApplyBreadcrumbs(Page tab, ProductViewModel vm,
        IReadOnlyList<(string Label, string? NodeId)> segments)
    {
        tab.Breadcrumbs.Clear();
        for (var i = 0; i < segments.Count; i++)
        {
            var (label, nodeId) = segments[i];
            var isLast = i == segments.Count - 1;
            tab.Breadcrumbs.Add(new BreadcrumbSegment
            {
                Label    = label,
                Navigate = isLast ? null : () => vm.NavigateTo(nodeId)
            });
        }
    }

    private static UserControl Placeholder() => new()
    {
        Content = new TextBlock
        {
            Text = "Open the Product Manager from a folder that contains a .product directory.",
            Margin = new Thickness(24),
            TextWrapping = TextWrapping.Wrap,
            Foreground = Application.Current?.Resources["TextMutedBrush"] as System.Windows.Media.Brush
        }
    };
}

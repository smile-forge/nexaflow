using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.ProductManager.ClientTools;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.ProductManager.Views;

public partial class ProductViewletView : UserControl, IViewletAiSurface
{
    private readonly IShellServices _shell;
    private readonly string _folderPath;

    public ProductViewletView(IShellServices shell, string folderPath)
    {
        InitializeComponent();
        _shell      = shell;
        _folderPath = folderPath;
        Loaded += (_, _) => Refresh();
    }

    private void Refresh()
    {
        try
        {
            var state = new ProductStore(_folderPath).Load();
            TitleText.Text = string.IsNullOrWhiteSpace(state.Product.Product)
                ? Str.Get("ProductManager.Viewlet.Name") : Str.Format("ProductManager.Viewlet.TitleFormat", state.Product.Product);

            var leaves  = state.Nodes.Values.Where(n => n.Children.All(c => !state.Nodes.ContainsKey(c))).ToList();
            var done    = leaves.Count(n => n.Status == Status.Done);
            var faulted = state.Nodes.Values.Count(n => n.Status == Status.Faulted);
            SummaryText.Text = faulted > 0
                ? Str.Format("ProductManager.Viewlet.SummaryFaultedFormat", state.Nodes.Count, done, leaves.Count, faulted)
                : Str.Format("ProductManager.Viewlet.SummaryFormat", state.Nodes.Count, done, leaves.Count);
        }
        catch
        {
            SummaryText.Text = Str.Get("ProductManager.Viewlet.ReadFailed");
        }
    }

    private void OpenButton_Click(object sender, System.Windows.RoutedEventArgs e)
        => _shell.OpenTab("ProductManager", new() { ["path"] = _folderPath });

    // ── IViewletAiSurface ────────────────────────────────────────────────────

    string? IViewletAiSurface.GetContext()
    {
        try { return ProductTools.Describe(new ProductStore(_folderPath).Load(), null); }
        catch { return null; }
    }

    IReadOnlyList<IClientTool> IViewletAiSurface.GetClientTools() => ProductTools.ForRoot(_folderPath);
}

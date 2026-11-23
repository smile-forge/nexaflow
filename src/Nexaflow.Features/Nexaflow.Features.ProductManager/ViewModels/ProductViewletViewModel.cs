using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.ProductManager.ClientTools;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.ProductManager.ViewModels;

/// <summary>
/// The product viewlet over a folder that holds a product: its name, how far along it is, and the way into it. Gives the
/// assistant the product's outline and the product tools for that folder.
/// </summary>
public sealed partial class ProductViewletViewModel(IShellServices shell, string folderPath) : ObservableObject, IViewletAiSurface
{
    [ObservableProperty] private string _title = Str.Get("ProductManager.Viewlet.Name");
    [ObservableProperty] private string _summary = "…";

    /// <summary>Reads the product again, as it is now on disk.</summary>
    public void Refresh()
    {
        try
        {
            var state = new ProductStore(folderPath).Load();
            Title = string.IsNullOrWhiteSpace(state.Product.Product)
                ? Str.Get("ProductManager.Viewlet.Name") : Str.Format("ProductManager.Viewlet.TitleFormat", state.Product.Product);

            var leaves  = state.Nodes.Values.Where(n => n.Children.All(c => !state.Nodes.ContainsKey(c))).ToList();
            var done    = leaves.Count(n => n.Status == Status.Done);
            var faulted = state.Nodes.Values.Count(n => n.Status == Status.Faulted);
            Summary = faulted > 0
                ? Str.Format("ProductManager.Viewlet.SummaryFaultedFormat", state.Nodes.Count, done, leaves.Count, faulted)
                : Str.Format("ProductManager.Viewlet.SummaryFormat", state.Nodes.Count, done, leaves.Count);
        }
        catch
        {
            Summary = Str.Get("ProductManager.Viewlet.ReadFailed");
        }
    }

    [RelayCommand]
    private void Open() => shell.OpenTab("ProductManager", new() { ["path"] = folderPath });

    public string? GetContext()
    {
        try { return ProductTools.Describe(new ProductStore(folderPath).Load(), null); }
        catch { return null; }
    }

    public IReadOnlyList<IClientTool> GetClientTools() => ProductTools.ForRoot(folderPath);
}

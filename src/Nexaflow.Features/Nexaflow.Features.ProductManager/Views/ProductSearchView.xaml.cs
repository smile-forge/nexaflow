using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.ProductManager.ViewModels;

namespace Nexaflow.Features.ProductManager.Views;

/// <summary>The graph search results page: a query box, a status line, and the results with their two ways
/// in. Runs the query it was opened with once the page is shown.</summary>
public partial class ProductSearchView : UserControl, IPageView
{
    private readonly ProductSearchViewModel _vm;

    IPageViewModel? IPageView.ViewModel => _vm;

    public ProductSearchView(ProductSearchViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (_vm.Query.Length > 0) await _vm.SearchAsync();
    }

    /// <summary>A second search re-points this tab rather than opening another: the tab is identified by the
    /// product root, so the shell hands the new query here.</summary>
    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        if (!pageParams.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query)) return;
        _vm.Query = query;
        _ = _vm.SearchAsync();
    }
}

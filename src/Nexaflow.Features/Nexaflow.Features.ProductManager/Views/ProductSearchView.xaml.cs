using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.ProductManager.ViewModels;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.ProductManager.Views;

/// <summary>The graph search results page: a query box, a status line, and the results with their two ways
/// in. Runs the query it was opened with once the page is shown.</summary>
[ViewFor(typeof(ProductSearchViewModel))]
public partial class ProductSearchView : UserControl
{
    private readonly ProductSearchViewModel _vm;

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
}

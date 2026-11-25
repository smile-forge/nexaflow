using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Common.Viewlets;
using Nexaflow.Features.ProductManager.ClientTools;
using Nexaflow.Features.ProductManager.ViewModels;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Localization;
using Nexaflow.Visuals.Common.Viewing;

namespace Nexaflow.Features.ProductManager.Views;

/// <summary>Draws a <see cref="ProductViewletViewModel"/>, reading the product again each time it is shown.</summary>
[ViewFor(typeof(ProductViewletViewModel))]
public partial class ProductViewletView : UserControl
{
    public ProductViewletView(ProductViewletViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += (_, _) => vm.Refresh();
    }
}

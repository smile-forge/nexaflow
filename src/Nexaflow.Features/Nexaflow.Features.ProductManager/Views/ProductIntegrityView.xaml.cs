using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Nexaflow.Features.Common;
using Nexaflow.Features.ProductManager.ViewModels;

namespace Nexaflow.Features.ProductManager.Views;

/// <summary>The Integrity page — broken snaplinks, selectable and repairable in place.</summary>
public partial class ProductIntegrityView : UserControl, IPageView
{
    public ProductIntegrityViewModel ViewModel { get; }

    public ProductIntegrityView(ProductIntegrityViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;
        ViewModel.Host = this;   // so a right-pane open splits the window that owns this page
    }

    IPageViewModel? IPageView.ViewModel => ViewModel;

    // Picking a target (its two-way SelectedItem binding already filled the row's fields) or a moved-file
    // suggestion closes the dropdown it lives in — a Popup stays open until dismissed otherwise.
    private void Target_Picked(object sender, SelectionChangedEventArgs e) => CloseParentPopup(sender);

    private void FileSuggestion_Picked(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: string rel }) ViewModel.UseSuggestionCommand.Execute(rel);
        CloseParentPopup(sender);
    }

    private static void CloseParentPopup(object sender)
    {
        DependencyObject? d = sender as DependencyObject;
        while (d is not null and not Popup) d = LogicalTreeHelper.GetParent(d) ?? VisualTreeHelper.GetParent(d);
        if (d is Popup popup) popup.IsOpen = false;
    }
}

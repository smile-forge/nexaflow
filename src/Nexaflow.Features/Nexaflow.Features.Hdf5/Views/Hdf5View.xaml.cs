using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Features.Hdf5.ViewModels;

namespace Nexaflow.Features.Hdf5.Views;

/// <summary>
/// The HDF5 viewer tab: the object tree on the left, the selected object's content in the middle, the details
/// drawer on the right. The table is the shared windowed grid, fed one window of rows at a time.
/// </summary>
public partial class Hdf5View : UserControl, IPageView
{
    private const double DefaultDrawerWidth = 320;

    private Hdf5ViewModel ViewModel { get; }
    private Hdf5TableViewModel? _table;
    private double _drawerWidth = DefaultDrawerWidth;

    public Hdf5View(Hdf5ViewModel viewModel)
    {
        InitializeComponent();
        ViewModel   = viewModel;
        DataContext = viewModel;

        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        TableGrid.FocalRowChanged += OnFocalRowChanged;
        ApplyDrawer();
        Watch(viewModel.Table);
    }

    IPageViewModel IPageView.ViewModel => ViewModel;

    /// <summary>A re-open that names another object in this file selects it here.</summary>
    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        if (pageParams.GetValueOrDefault("node") is { Length: > 0 } node) _ = NavigateWhenOpenAsync(node);
    }

    private async Task NavigateWhenOpenAsync(string node)
    {
        await ViewModel.Loaded;
        await ViewModel.NavigateToAsync(node);
    }

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is Hdf5NodeViewModel node) ViewModel.SelectedNode = node;
    }

    private void OnTreeItemSelected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem item) item.BringIntoView();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Hdf5ViewModel.DetailsOpen): ApplyDrawer(); break;
            case nameof(Hdf5ViewModel.Table):       Watch(ViewModel.Table); break;
        }
    }

    private void OnFocalRowChanged(int focal)
    {
        if (ViewModel.Table is not { } table || focal == table.FocalRow) return;
        table.FocalRow = focal;
        _ = table.RefreshWindowAsync();
    }

    private void Watch(Hdf5TableViewModel? table)
    {
        if (_table is not null) _table.ScrollRequested -= OnScrollRequested;
        _table = table;
        if (table is not null) table.ScrollRequested += OnScrollRequested;
    }

    private void OnScrollRequested(int row) => TableGrid.ScrollToRow(row);

    /// <summary>Closing the drawer remembers how wide the user had dragged it.</summary>
    private void ApplyDrawer()
    {
        if (!ViewModel.DetailsOpen && DrawerCol.ActualWidth > 0) _drawerWidth = DrawerCol.ActualWidth;
        DrawerCol.Width = new GridLength(ViewModel.DetailsOpen ? _drawerWidth : 0);
    }

    private static Hdf5AttributeRow? AttributeOf(object sender) =>
        (sender as FrameworkElement)?.DataContext as Hdf5AttributeRow;

    private void OnCopyAttributeValue(object sender, RoutedEventArgs e)
    {
        if (AttributeOf(sender) is { } a) Clipboard.SetText(a.Value);
    }

    private void OnCopyAttributeName(object sender, RoutedEventArgs e)
    {
        if (AttributeOf(sender) is { } a) Clipboard.SetText(a.Name);
    }

    private void OnCopyAttributeRow(object sender, RoutedEventArgs e)
    {
        if (AttributeOf(sender) is { } a) Clipboard.SetText($"{a.Name}\t{a.TypeText}\t{a.Value}");
    }
}

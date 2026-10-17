using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.UsbProbe.ViewModels;
using Nexaflow.Visuals.Common.Behaviors;

namespace Nexaflow.Features.UsbProbe.Views;

public partial class UsbProbeView : UserControl, IPageView
{
    readonly UsbProbeViewModel _vm;

    public UsbProbeView(UsbProbeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = _vm = viewModel;
        // The machine is read once the tab is first shown, not when the tab is merely defined.
        Loaded += async (_, _) => await _vm.StartAsync();
    }

    public IPageViewModel? ViewModel => _vm;

    /// <summary>The shell re-pointing this tab: a new <c>location</c> opens there; none leaves the user where they are.</summary>
    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        if (pageParams.TryGetValue(UsbProbePageRegistration.LocationParam, out var location)) _vm.OpenWhenReady(location);
    }

    void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is UsbTreeNode node) _vm.TreeSelected(node);
    }

    void Listing_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject hit && ItemsControl.ContainerFromElement(Listing, hit) is ListViewItem { DataContext: UsbListRow row })
            _vm.OpenCommand.Execute(row);
    }

    void Listing_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm.SelectedRow is { } row)
        {
            _vm.OpenCommand.Execute(row);
            e.Handled = true;
        }
    }

    /// <summary>On a node, that place's menu — without opening it, as the file browser's tree does.</summary>
    void Tree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        if (e.OriginalSource is DependencyObject hit && PointerMenus.ItemUnder<TreeViewItem>(hit, Tree) is { DataContext: UsbTreeNode node })
            PointerMenus.OpenAt((ContextMenu)FindResource("PlaceMenu"), Tree, node);
    }

    /// <summary>On a row, that row's menu, the row selected first; on the blank space, the place open's.</summary>
    void Listing_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        e.Handled = true;
        if (e.OriginalSource is not DependencyObject hit || PointerMenus.ItemUnder<GridViewColumnHeader>(hit, Listing) is not null) return;
        if (ItemsControl.ContainerFromElement(Listing, hit) is ListViewItem { DataContext: UsbListRow row })
        {
            _vm.SelectedRow = row;
            PointerMenus.OpenAt((ContextMenu)FindResource("PlaceMenu"), Listing, row);
        }
        else
        {
            _vm.SelectedRow = null;
            PointerMenus.OpenAt((ContextMenu)FindResource("BlankMenu"), Listing, null);
        }
    }
}

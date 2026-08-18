using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Features.Network.ViewModels;

namespace Nexaflow.Features.Network.Views;

public partial class NetworkView : UserControl
{
    public NetworkView(NetworkViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>
    /// Clicking the row that is already chosen lets it go, and the panel with it.
    /// </summary>
    /// <remarks>
    /// A ListView will not do this on its own: once something is selected, clicking it again is a no-op,
    /// so the only way back to nothing selected is a keyboard gesture nobody tries. Handled on preview and
    /// left unhandled, so the ordinary select still happens for every other row.
    /// </remarks>
    private void Devices_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListView list || DataContext is not NetworkViewModel vm) return;
        if (vm.Selected is null) return;
        if (e.OriginalSource is not DependencyObject hit) return;

        if (Row(hit) is { } row && ReferenceEquals(row.DataContext, vm.Selected))
        {
            vm.Selected = null;
            e.Handled = true;
        }
    }

    private static ListViewItem? Row(DependencyObject from)
    {
        for (var at = from; at is not null; at = VisualTreeHelper.GetParent(at))
            if (at is ListViewItem row) return row;

        return null;
    }
}

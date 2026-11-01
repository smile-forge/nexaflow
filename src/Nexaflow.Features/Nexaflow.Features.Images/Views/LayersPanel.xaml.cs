using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Features.Images.ViewModels;

namespace Nexaflow.Features.Images.Views;

/// <summary>
/// The layers panel, bound to a <see cref="LayersViewModel"/>: a click on a row's thumbnail makes its layer active, one on
/// its mask's makes the mask active — Alt shows the mask alone, Ctrl loads it as the selection, Shift turns it off — and a row
/// dragged onto another moves its layer above it, below it, or into it where it is a group.
/// </summary>
public partial class LayersPanel : UserControl
{
    private const string DragFormat = "Nexaflow.ImageLayer";

    private Point _pressedAt;
    private LayerItem? _pressed;

    public LayersPanel() => InitializeComponent();

    private LayersViewModel? Layers => DataContext as LayersViewModel;

    private void Thumbnail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LayerItem item) Layers?.ActivateCommand.Execute(item);
    }

    private void MaskThumbnail_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not LayerItem item || Layers is not { } layers) return;
        var held = Keyboard.Modifiers;
        if ((held & ModifierKeys.Alt) != 0) layers.ShowMaskAloneCommand.Execute(item);
        else if ((held & ModifierKeys.Control) != 0) layers.LoadMaskAsSelectionCommand.Execute(item);
        else if ((held & ModifierKeys.Shift) != 0) layers.ToggleMaskEnabledCommand.Execute(item);
        else layers.ActivateMaskCommand.Execute(item);
    }

    private void Canvas_Expanded(object sender, RoutedEventArgs e) => Layers?.BeginCanvasSizeCommand.Execute(null);

    // ── Dragging a row onto another ─────────────────────────────────────────

    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedAt = e.GetPosition(LayerList);
        _pressed = ItemAt(e.OriginalSource as DependencyObject);
        // A press on the row itself, not on a control in it, makes its layer active.
        if (_pressed is not null && e.OriginalSource is not null && FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is null)
            Layers?.ActivateCommand.Execute(_pressed);
    }

    private void List_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _pressed is not { } dragged) return;
        var moved = e.GetPosition(LayerList) - _pressedAt;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _pressed = null;
        DragDrop.DoDragDrop(LayerList, new DataObject(DragFormat, dragged.Id), DragDropEffects.Move);
    }

    private void List_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DragFormat) is not Guid dragged || Layers is not { } layers) return;
        if (ContainerAt(e.OriginalSource as DependencyObject) is not { DataContext: LayerItem target } row) return;
        double y = e.GetPosition(row).Y / Math.Max(1, row.ActualHeight);
        var drop = target.Kind == LayerKind.Group && y is > 0.3 and < 0.7 ? LayerDrop.Into : y < 0.5 ? LayerDrop.Above : LayerDrop.Below;
        layers.Move(dragged, target.Id, drop);
        e.Handled = true;
    }

    private static LayerItem? ItemAt(DependencyObject? source) => ContainerAt(source)?.DataContext as LayerItem;

    private static ListBoxItem? ContainerAt(DependencyObject? source) => FindAncestor<ListBoxItem>(source);

    private static T? FindAncestor<T>(DependencyObject? from) where T : DependencyObject
    {
        for (var d = from; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is T found) return found;
        return null;
    }
}

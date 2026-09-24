using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Behaviors;

/// <summary>Asks the owner of a list to move <see cref="Item"/> so it lands at <see cref="ToIndex"/>, counted in the
/// list as it stands before the move.</summary>
public sealed record ReorderRequest(object Item, int ToIndex);

/// <summary>
/// Drag-to-reorder for any <see cref="ItemsControl"/>: set <c>DragReorder.Command</c> and dragging an item past the
/// system drag threshold, then dropping it on another, executes the command with a <see cref="ReorderRequest"/>. The
/// view-model does the move, so the list's order stays its to decide. Dropping on the far half of an item lands after
/// it; the orientation is read from the panel.
/// </summary>
public static class DragReorder
{
    private const string Format = "Nexaflow.DragReorder.Item";

    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(DragReorder), new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject d) => (ICommand?)d.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject d, ICommand? value) => d.SetValue(CommandProperty, value);

    private static readonly DependencyProperty DragOriginProperty = DependencyProperty.RegisterAttached(
        "DragOrigin", typeof(Point?), typeof(DragReorder));

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl list) return;

        list.PreviewMouseLeftButtonDown -= OnMouseDown;
        list.PreviewMouseMove           -= OnMouseMove;
        list.DragOver                   -= OnDragOver;
        list.Drop                       -= OnDrop;
        if (e.NewValue is null) return;

        list.AllowDrop = true;
        list.PreviewMouseLeftButtonDown += OnMouseDown;
        list.PreviewMouseMove           += OnMouseMove;
        list.DragOver                   += OnDragOver;
        list.Drop                       += OnDrop;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var list = (ItemsControl)sender;
        list.SetValue(DragOriginProperty, ContainerAt(list, e.OriginalSource) is null ? null : e.GetPosition(list));
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        var list = (ItemsControl)sender;
        if (e.LeftButton != MouseButtonState.Pressed || list.GetValue(DragOriginProperty) is not Point origin) return;

        var delta = e.GetPosition(list) - origin;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        list.ClearValue(DragOriginProperty);
        if (ContainerAt(list, e.OriginalSource) is not { } container) return;

        var item = list.ItemContainerGenerator.ItemFromContainer(container);
        if (item == DependencyProperty.UnsetValue) return;
        DragDrop.DoDragDrop(container, new DataObject(Format, item), DragDropEffects.Move);
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(Format) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        var list = (ItemsControl)sender;
        if (!e.Data.GetDataPresent(Format) || GetCommand(list) is not { } command) return;

        var item  = e.Data.GetData(Format)!;
        var index = DropIndex(list, e);
        var request = new ReorderRequest(item, index);
        if (command.CanExecute(request)) command.Execute(request);
        e.Handled = true;
    }

    private static int DropIndex(ItemsControl list, DragEventArgs e)
    {
        if (ContainerAt(list, e.OriginalSource) is not { } target)
            return list.Items.Count;

        var index = list.ItemContainerGenerator.IndexFromContainer(target);
        var p     = e.GetPosition(target);
        var horizontal = ItemsPanelOrientation(list) == Orientation.Horizontal;
        var farHalf = horizontal ? p.X > target.ActualWidth / 2 : p.Y > target.ActualHeight / 2;
        return farHalf ? index + 1 : index;
    }

    private static Orientation ItemsPanelOrientation(ItemsControl list)
    {
        var panel = FindPanel(list);
        return panel switch
        {
            StackPanel s               => s.Orientation,
            VirtualizingStackPanel v   => v.Orientation,
            WrapPanel w                => w.Orientation,
            _                          => Orientation.Vertical,
        };
    }

    private static Panel? FindPanel(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Panel { IsItemsHost: true } panel) return panel;
            if (FindPanel(child) is { } found) return found;
        }
        return null;
    }

    /// <summary>The item container the event came from — its own, not a nested list's.</summary>
    private static FrameworkElement? ContainerAt(ItemsControl list, object source)
    {
        var node = source as DependencyObject;
        while (node is not null && node != list)
        {
            if (node is FrameworkElement fe && ItemsControl.ItemsControlFromItemContainer(fe) == list) return fe;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }
}

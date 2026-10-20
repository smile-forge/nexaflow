using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Behaviors;

/// <summary>Asks the owner of a list to move <see cref="Items"/> so they land at <see cref="ToIndex"/>, a gap counted
/// in the list as it stands before the move (0 before the first item, <c>Count</c> after the last).</summary>
/// <param name="Item">The item the pointer carried.</param>
/// <param name="ToIndex">The gap the drop landed in.</param>
public sealed record ReorderRequest(object Item, int ToIndex)
{
    /// <summary>Everything dragged, in list order: the selection when the list moves it, otherwise just
    /// <see cref="Item"/>.</summary>
    public IReadOnlyList<object> Items { get; init; } = [Item];
}

/// <summary>
/// Drag-to-reorder for any <see cref="ItemsControl"/>: set <c>DragReorder.Command</c> and dragging an item past the
/// system drag threshold, then dropping it in the list, executes the command with a <see cref="ReorderRequest"/>. The
/// view-model does the move, so the list's order stays its to decide. The drop lands in the gap nearest the pointer
/// (<see cref="ItemGaps"/>), margins and the space after the last item included.
/// <para>
/// Opt-in, each its own attached property: <c>ShowsInsertion</c> draws an <see cref="InsertionMarker"/> in that gap
/// while the drag is over the list; <c>AutoScrolls</c> scrolls the list's own <see cref="ScrollViewer"/> while the
/// pointer is near one of its edges; <c>MovesSelection</c> drags the whole selection, in list order, when the grabbed
/// item is part of it.
/// </para>
/// </summary>
public static class DragReorder
{
    private const string Format = "Nexaflow.DragReorder.Item";

    /// <summary>How near an edge of the list's viewport, in DIPs, the pointer has to be to scroll it.</summary>
    private const double AutoScrollEdge = 24;

    /// <summary>The fewest milliseconds between two scroll steps, so the speed does not follow how often
    /// <c>DragOver</c> fires.</summary>
    private const long AutoScrollInterval = 50;

    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(DragReorder), new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject d) => (ICommand?)d.GetValue(CommandProperty);
    public static void SetCommand(DependencyObject d, ICommand? value) => d.SetValue(CommandProperty, value);

    /// <summary>Draws the gap a drop would land in while a drag is over the list.</summary>
    public static readonly DependencyProperty ShowsInsertionProperty = DependencyProperty.RegisterAttached(
        "ShowsInsertion", typeof(bool), typeof(DragReorder), new PropertyMetadata(false));

    public static bool GetShowsInsertion(DependencyObject d) => (bool)d.GetValue(ShowsInsertionProperty);
    public static void SetShowsInsertion(DependencyObject d, bool value) => d.SetValue(ShowsInsertionProperty, value);

    /// <summary>Scrolls the list while a drag holds the pointer near an edge of its viewport, so a drop can reach
    /// items that are off screen.</summary>
    public static readonly DependencyProperty AutoScrollsProperty = DependencyProperty.RegisterAttached(
        "AutoScrolls", typeof(bool), typeof(DragReorder), new PropertyMetadata(false));

    public static bool GetAutoScrolls(DependencyObject d) => (bool)d.GetValue(AutoScrollsProperty);
    public static void SetAutoScrolls(DependencyObject d, bool value) => d.SetValue(AutoScrollsProperty, value);

    /// <summary>
    /// Drags every selected item, in list order, when the grabbed one is selected. In an extended-selection
    /// <see cref="ListBox"/> a plain press on a selected item leaves the selection alone until the button comes up, so
    /// a drag can carry it; released without a drag, it is the ordinary click and selects just that item.
    /// </summary>
    public static readonly DependencyProperty MovesSelectionProperty = DependencyProperty.RegisterAttached(
        "MovesSelection", typeof(bool), typeof(DragReorder), new PropertyMetadata(false));

    public static bool GetMovesSelection(DependencyObject d) => (bool)d.GetValue(MovesSelectionProperty);
    public static void SetMovesSelection(DependencyObject d, bool value) => d.SetValue(MovesSelectionProperty, value);

    private static readonly DependencyProperty PressProperty = DependencyProperty.RegisterAttached(
        "Press", typeof(Press), typeof(DragReorder));

    private static readonly DependencyProperty MarkerProperty = DependencyProperty.RegisterAttached(
        "Marker", typeof(InsertionMarker), typeof(DragReorder));

    private static readonly DependencyProperty LastScrollProperty = DependencyProperty.RegisterAttached(
        "LastScroll", typeof(long), typeof(DragReorder), new PropertyMetadata(0L));

    /// <summary>A press on an item that may become a drag. <paramref name="OwesClick"/>: the list's own handling of
    /// it was held back, and is due if the button comes up without one.</summary>
    private sealed record Press(Point Origin, FrameworkElement Container, bool OwesClick);

    /// <summary>What a drag carries: the grabbed item and everything moving with it.</summary>
    private sealed record Payload(object Item, IReadOnlyList<object> Items);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl list) return;

        list.PreviewMouseLeftButtonDown -= OnMouseDown;
        list.PreviewMouseLeftButtonUp   -= OnMouseUp;
        list.PreviewMouseMove           -= OnMouseMove;
        list.DragOver                   -= OnDragOver;
        list.DragLeave                  -= OnDragLeave;
        list.Drop                       -= OnDrop;
        if (e.NewValue is null) return;

        list.AllowDrop = true;
        list.PreviewMouseLeftButtonDown += OnMouseDown;
        list.PreviewMouseLeftButtonUp   += OnMouseUp;
        list.PreviewMouseMove           += OnMouseMove;
        list.DragOver                   += OnDragOver;
        list.DragLeave                  += OnDragLeave;
        list.Drop                       += OnDrop;
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        var list = (ItemsControl)sender;
        list.ClearValue(PressProperty);
        if (ContainerAt(list, e.OriginalSource) is not { } container) return;

        // A plain press on an item of an extended selection would collapse the selection to that item before a drag
        // could carry the rest, so the list does not see it yet.
        var owesClick = GetMovesSelection(list)
                     && list is ListBox { SelectionMode: SelectionMode.Extended }
                     && container is ListBoxItem { IsSelected: true }
                     && Keyboard.Modifiers == ModifierKeys.None;
        list.SetValue(PressProperty, new Press(e.GetPosition(list), container, owesClick));
        if (owesClick) e.Handled = true;
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        var list = (ItemsControl)sender;
        if (list.GetValue(PressProperty) is not Press press) return;

        list.ClearValue(PressProperty);
        if (press.OwesClick) ReplayPress(list, press.Container, e);
    }

    /// <summary>
    /// Hands the item the press its list held back, through the item's own handling, so the selection, the anchor a
    /// later Shift+click extends from and the focus all come out as an undeferred click leaves them. The list takes
    /// the mouse on a press, for drag-selecting; the button is already up, so it is given straight back.
    /// </summary>
    private static void ReplayPress(ItemsControl list, FrameworkElement container, MouseButtonEventArgs up)
    {
        container.RaiseEvent(new MouseButtonEventArgs(up.MouseDevice, up.Timestamp, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
        });
        if (list.IsMouseCaptureWithin) Mouse.Capture(null);
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        var list = (ItemsControl)sender;
        if (e.LeftButton != MouseButtonState.Pressed || list.GetValue(PressProperty) is not Press press) return;

        var delta = e.GetPosition(list) - press.Origin;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        list.ClearValue(PressProperty);
        var item = list.ItemContainerGenerator.ItemFromContainer(press.Container);
        if (item == DependencyProperty.UnsetValue) return;

        var payload = new Payload(item, GetMovesSelection(list) ? DraggedItems(list, item) : [item]);
        DragDrop.DoDragDrop(press.Container, new DataObject(Format, payload), DragDropEffects.Move);
    }

    /// <summary>The selected items in list order when <paramref name="grabbed"/> is one of them; otherwise just
    /// it.</summary>
    private static IReadOnlyList<object> DraggedItems(ItemsControl list, object grabbed)
    {
        var selected = list switch
        {
            ListBox l       => l.SelectedItems,
            MultiSelector m => m.SelectedItems,
            _               => null,
        };
        if (selected is null || selected.Count < 2 || !selected.Contains(grabbed)) return [grabbed];

        var chosen  = new HashSet<object>(selected.Cast<object>());
        var ordered = new List<object>(chosen.Count);
        foreach (var item in list.Items)
            if (chosen.Contains(item)) ordered.Add(item);
        return ordered;
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        var list = (ItemsControl)sender;
        var ours = e.Data.GetDataPresent(Format);
        e.Effects = ours ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
        if (!ours) return;

        if (GetAutoScrolls(list)) AutoScroll(list, e);
        if (GetShowsInsertion(list)) MarkerOf(list).Show(list, DropIndex(list, e));
    }

    private static void OnDragLeave(object sender, DragEventArgs e) =>
        ((sender as DependencyObject)?.GetValue(MarkerProperty) as InsertionMarker)?.Clear();

    private static void OnDrop(object sender, DragEventArgs e)
    {
        var list = (ItemsControl)sender;
        (list.GetValue(MarkerProperty) as InsertionMarker)?.Clear();
        if (e.Data.GetData(Format) is not Payload payload || GetCommand(list) is not { } command) return;

        var request = new ReorderRequest(payload.Item, DropIndex(list, e)) { Items = payload.Items };
        if (command.CanExecute(request)) command.Execute(request);
        e.Handled = true;
    }

    private static int DropIndex(ItemsControl list, DragEventArgs e) => ItemGaps.GapAt(list, e.GetPosition(list));

    private static InsertionMarker MarkerOf(ItemsControl list)
    {
        if (list.GetValue(MarkerProperty) is InsertionMarker marker) return marker;
        marker = new InsertionMarker();
        list.SetValue(MarkerProperty, marker);
        return marker;
    }

    /// <summary>One line's step toward the edge the pointer is near, at most every <see cref="AutoScrollInterval"/>.
    /// A line is the viewer's own: a few DIPs when it scrolls by pixel, an item when it scrolls by item.</summary>
    private static void AutoScroll(ItemsControl list, DragEventArgs e)
    {
        if (FindScrollViewer(list) is not { } viewer) return;

        var now = Environment.TickCount64;
        if (now - (long)list.GetValue(LastScrollProperty) < AutoScrollInterval) return;

        var p = e.GetPosition(viewer);
        var scrolled = false;
        if (viewer.ScrollableHeight > 0)
        {
            if (p.Y < AutoScrollEdge && viewer.VerticalOffset > 0) { viewer.LineUp(); scrolled = true; }
            else if (p.Y > viewer.ActualHeight - AutoScrollEdge && viewer.VerticalOffset < viewer.ScrollableHeight)
            { viewer.LineDown(); scrolled = true; }
        }
        if (viewer.ScrollableWidth > 0)
        {
            if (p.X < AutoScrollEdge && viewer.HorizontalOffset > 0) { viewer.LineLeft(); scrolled = true; }
            else if (p.X > viewer.ActualWidth - AutoScrollEdge && viewer.HorizontalOffset < viewer.ScrollableWidth)
            { viewer.LineRight(); scrolled = true; }
        }
        if (scrolled) list.SetValue(LastScrollProperty, now);
    }

    /// <summary>The list's own scroll viewer — its template's, never one inside an item.</summary>
    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer) return viewer;
            if (child is Panel { IsItemsHost: true }) continue;
            if (FindScrollViewer(child) is { } found) return found;
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

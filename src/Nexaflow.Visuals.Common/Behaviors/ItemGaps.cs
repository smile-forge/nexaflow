using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Behaviors;

/// <summary>
/// Where a drop lands in a list: the gap between two items, numbered 0 (before the first) to <c>count</c> (after the
/// last). The pointer picks the gap nearest it along the items' axis, so the margin between two items, the space after
/// the last and the near half of an item all answer without a container having to be hit.
/// <para>
/// <see cref="GapAt(IReadOnlyList{ValueTuple{int, double, double}}, double, int)"/> is the arithmetic, WPF-free;
/// <see cref="GapAt(ItemsControl, Point)"/> feeds it the realized containers along the panel's orientation. A panel
/// that wraps is read a line at a time — the line under the pointer, or the nearest — so a wrap panel of thumbnails
/// answers like a stack.
/// </para>
/// </summary>
public static class ItemGaps
{
    /// <summary>
    /// The gap nearest <paramref name="at"/>, given the realized items' extents along the item axis. Items need not be
    /// contiguous or sorted; past every realized item the gap is the one after the last of them, and with nothing
    /// realized it is <paramref name="count"/>. The answer is always within 0..<paramref name="count"/>.
    /// </summary>
    public static int GapAt(IReadOnlyList<(int Index, double Start, double End)> realized, double at, int count)
    {
        if (count <= 0) return 0;
        if (realized.Count == 0) return count;

        // The nearest gap to a point inside an item is its start up to the item's middle and its end past it, so the
        // gap is the first item whose middle lies beyond the pointer.
        var gap = -1;
        var nearestMiddle = double.PositiveInfinity;
        var last = -1;
        for (var i = 0; i < realized.Count; i++)
        {
            var (index, start, end) = realized[i];
            last = Math.Max(last, index);
            var middle = (start + end) / 2;
            if (middle > at && middle < nearestMiddle)
            {
                nearestMiddle = middle;
                gap = index;
            }
        }
        return Math.Clamp(gap >= 0 ? gap : last + 1, 0, count);
    }

    /// <summary>The gap nearest <paramref name="at"/>, a point in <paramref name="list"/>'s own coordinates.</summary>
    public static int GapAt(ItemsControl list, Point at)
    {
        var count = list.Items.Count;
        if (FindPanel(list) is not { } panel) return count;

        var horizontal = PanelOrientation(panel) == Orientation.Horizontal;
        var line = LineAt(Lines(Realized(list, panel), horizontal), horizontal ? at.Y : at.X, horizontal);

        var extents = new List<(int Index, double Start, double End)>(line.Count);
        foreach (var item in line)
            extents.Add((item.Index, Start(item.Bounds, horizontal), End(item.Bounds, horizontal)));
        return GapAt(extents, horizontal ? at.X : at.Y, count);
    }

    /// <summary>
    /// The segment, in <paramref name="list"/>'s coordinates, that crosses <paramref name="gap"/>: midway between the
    /// two items when both are realized on one line, otherwise along the edge of the one that is. False when neither
    /// neighbour is realized, so there is nowhere on screen to point at.
    /// </summary>
    internal static bool TryGapSegment(ItemsControl list, int gap, out Point from, out Point to, out bool horizontal)
    {
        from = to = default;
        horizontal = false;
        if (FindPanel(list) is not { } panel) return false;

        horizontal = PanelOrientation(panel) == Orientation.Horizontal;
        RealizedItem? before = null, after = null;
        foreach (var item in Realized(list, panel))
        {
            if (item.Index == gap - 1) before = item;
            else if (item.Index == gap) after = item;
        }

        double along, crossStart, crossEnd;
        if (before is { } b && after is { } a && Start(a.Bounds, horizontal) >= Start(b.Bounds, horizontal))
        {
            along      = (End(b.Bounds, horizontal) + Start(a.Bounds, horizontal)) / 2;
            crossStart = Math.Min(CrossStart(b.Bounds, horizontal), CrossStart(a.Bounds, horizontal));
            crossEnd   = Math.Max(CrossEnd(b.Bounds, horizontal), CrossEnd(a.Bounds, horizontal));
        }
        else if (after is { } next)
        {
            along      = Start(next.Bounds, horizontal);
            crossStart = CrossStart(next.Bounds, horizontal);
            crossEnd   = CrossEnd(next.Bounds, horizontal);
        }
        else if (before is { } previous)
        {
            along      = End(previous.Bounds, horizontal);
            crossStart = CrossStart(previous.Bounds, horizontal);
            crossEnd   = CrossEnd(previous.Bounds, horizontal);
        }
        else return false;

        from = horizontal ? new Point(along, crossStart) : new Point(crossStart, along);
        to   = horizontal ? new Point(along, crossEnd)   : new Point(crossEnd, along);
        return true;
    }

    internal static Orientation PanelOrientation(Panel? panel) => panel switch
    {
        StackPanel s             => s.Orientation,
        VirtualizingStackPanel v => v.Orientation,
        WrapPanel w              => w.Orientation,
        _                        => Orientation.Vertical,
    };

    internal static Panel? FindPanel(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Panel { IsItemsHost: true } panel) return panel;
            if (FindPanel(child) is { } found) return found;
        }
        return null;
    }

    private readonly record struct RealizedItem(int Index, Rect Bounds);

    /// <summary>The containers the panel holds now, with their bounds in the list's coordinates, in item order.</summary>
    private static List<RealizedItem> Realized(ItemsControl list, Panel panel)
    {
        var realized = new List<RealizedItem>(panel.Children.Count);
        foreach (UIElement child in panel.Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var index = list.ItemContainerGenerator.IndexFromContainer(child);
            if (index < 0) continue;
            realized.Add(new(index, child.TransformToAncestor(list).TransformBounds(new Rect(child.RenderSize))));
        }
        realized.Sort((x, y) => x.Index.CompareTo(y.Index));
        return realized;
    }

    /// <summary>Splits items in order into the lines a wrapping panel laid them on: a line ends where the next item
    /// starts back along the item axis. A panel that never wraps is one line.</summary>
    private static List<List<RealizedItem>> Lines(List<RealizedItem> realized, bool horizontal)
    {
        var lines = new List<List<RealizedItem>>();
        List<RealizedItem>? line = null;
        foreach (var item in realized)
        {
            if (line is null || Start(item.Bounds, horizontal) < Start(line[^1].Bounds, horizontal))
                lines.Add(line = []);
            line.Add(item);
        }
        return lines;
    }

    /// <summary>The line whose cross-axis span holds <paramref name="cross"/>, or the nearest one.</summary>
    private static List<RealizedItem> LineAt(List<List<RealizedItem>> lines, double cross, bool horizontal)
    {
        if (lines.Count == 0) return [];

        var nearest = lines[0];
        var nearestDistance = double.PositiveInfinity;
        foreach (var line in lines)
        {
            var start = double.PositiveInfinity;
            var end   = double.NegativeInfinity;
            foreach (var item in line)
            {
                start = Math.Min(start, CrossStart(item.Bounds, horizontal));
                end   = Math.Max(end, CrossEnd(item.Bounds, horizontal));
            }
            var distance = cross < start ? start - cross : cross > end ? cross - end : 0;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = line;
            }
        }
        return nearest;
    }

    private static double Start(Rect r, bool horizontal)      => horizontal ? r.Left : r.Top;
    private static double End(Rect r, bool horizontal)        => horizontal ? r.Right : r.Bottom;
    private static double CrossStart(Rect r, bool horizontal) => horizontal ? r.Top : r.Left;
    private static double CrossEnd(Rect r, bool horizontal)   => horizontal ? r.Bottom : r.Right;
}

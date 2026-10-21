using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Behaviors;

/// <summary>
/// The line a drag draws across the gap a drop would land in, so a reorder or an insert says where before it happens.
/// <para>
/// One marker exists at a time — moving it is what the caller does on every <c>DragOver</c>, and <see cref="Clear"/>
/// takes it down when the drag leaves or lands. The gap is the one <see cref="ItemGaps"/> numbers, and the line follows
/// the panel's orientation. Drawn as an adorner, like <see cref="DropTargetHighlight"/>, so no item template has to
/// leave room for it.
/// </para>
/// </summary>
public sealed class InsertionMarker
{
    private LineAdorner?  _adorner;
    private AdornerLayer? _layer;

    /// <summary>The gap the marker stands in, or null while it is cleared.</summary>
    public int? Gap { get; private set; }

    /// <summary>Moves the line onto <paramref name="gap"/> of <paramref name="list"/>, or clears it when neither item
    /// beside that gap is realized.</summary>
    public void Show(ItemsControl list, int gap)
    {
        if (!ItemGaps.TryGapSegment(list, gap, out var from, out var to, out var horizontal)) { Clear(); return; }

        if (_adorner?.AdornedElement != list)
        {
            Clear();
            _layer = AdornerLayer.GetAdornerLayer(list);
            if (_layer is null) return;

            _adorner = new LineAdorner(list);
            _layer.Add(_adorner);
        }

        _adorner!.MoveTo(from, to, horizontal);
        Gap = gap;
    }

    public void Clear()
    {
        if (_adorner is not null) _layer?.Remove(_adorner);
        _adorner = null;
        _layer   = null;
        Gap      = null;
    }

    private sealed class LineAdorner : Adorner
    {
        private const double Thickness = 2;

        private Point _from, _to;
        private bool  _horizontal;

        public LineAdorner(UIElement adorned) : base(adorned) => IsHitTestVisible = false;

        public void MoveTo(Point from, Point to, bool horizontal)
        {
            if (from == _from && to == _to && horizontal == _horizontal) return;
            (_from, _to, _horizontal) = (from, to, horizontal);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = AdornedElement.RenderSize;
            if (size.Width <= 0 || size.Height <= 0) return;

            // The accent is what the app already uses for "this is where it goes" — the selected row's edge.
            var brush = (AdornedElement as FrameworkElement)?.TryFindResource("AccentBrush") as Brush
                     ?? SystemColors.HighlightBrush;
            var pen = new Pen(brush, Thickness);
            pen.Freeze();

            // Kept whole inside the list: the gap before the first item sits on its edge, where half a line would be
            // clipped away.
            var (from, to) = (_from, _to);
            if (_horizontal)
            {
                var x = Math.Clamp(from.X, Thickness / 2, size.Width - Thickness / 2);
                (from.X, to.X) = (x, x);
            }
            else
            {
                var y = Math.Clamp(from.Y, Thickness / 2, size.Height - Thickness / 2);
                (from.Y, to.Y) = (y, y);
            }

            dc.PushClip(new RectangleGeometry(new Rect(size)));
            dc.DrawLine(pen, from, to);
            dc.Pop();
        }
    }
}

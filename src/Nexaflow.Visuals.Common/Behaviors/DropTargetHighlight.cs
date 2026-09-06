using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Behaviors;

/// <summary>
/// Washes the row a drag is currently over, so a drop says where it will land by pointing at it
/// rather than by spelling the path out.
/// <para>
/// One highlight exists at a time — moving it is what the caller does on every <c>DragOver</c>, and
/// <see cref="Clear"/> takes it down when the drag leaves or lands. Drawn as an adorner so no host
/// has to leave room for it and no shared row style has to learn about dragging.
/// </para>
/// </summary>
public sealed class DropTargetHighlight
{
    private Adorner?      _adorner;
    private AdornerLayer? _layer;

    /// <summary>Moves the wash onto <paramref name="element"/>, or clears it when that is null.</summary>
    public void Show(UIElement? element)
    {
        if (element is null) { Clear(); return; }
        if (_adorner?.AdornedElement == element) return;

        Clear();

        _layer = AdornerLayer.GetAdornerLayer(element);
        if (_layer is null) return;

        _adorner = new WashAdorner(element);
        _layer.Add(_adorner);
    }

    public void Clear()
    {
        if (_adorner is not null) _layer?.Remove(_adorner);
        _adorner = null;
        _layer   = null;
    }

    private sealed class WashAdorner : Adorner
    {
        public WashAdorner(UIElement adorned) : base(adorned) => IsHitTestVisible = false;

        protected override void OnRender(DrawingContext dc)
        {
            var app = Application.Current;

            // The same pair the app already uses to say "this is the thing being acted on" — the
            // subtle accent is the text-selection wash, and the accent edge is the selected row's.
            var fill = app?.TryFindResource("AccentSubtleBrush") as Brush ?? SystemColors.HighlightBrush;
            var edge = app?.TryFindResource("AccentBrush")       as Brush ?? SystemColors.HighlightBrush;

            var pen = new Pen(edge, 1);
            pen.Freeze();

            var bounds = new Rect(AdornedElement.RenderSize);
            var crisp  = Rect.Inflate(bounds, -0.5, -0.5);
            if (crisp.IsEmpty || crisp.Width <= 0 || crisp.Height <= 0) return;

            dc.DrawRoundedRectangle(fill, pen, crisp, 4, 4);
        }
    }
}

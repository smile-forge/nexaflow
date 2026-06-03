using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Nexaflow.Core.Controls;

/// <summary>
/// A translucent "ghost frame" of a dragged element that follows the cursor. Created at drag start,
/// positioned via <see cref="SetOffset"/> during drag-over, and removed when the drag ends.
/// </summary>
internal sealed class DragAdorner : Adorner
{
    private readonly Brush _fill;
    private readonly Pen   _frame;
    private readonly Size  _size;
    private Point _offset;

    public DragAdorner(UIElement adorned, Visual ghost, Size size) : base(adorned)
    {
        IsHitTestVisible = false;
        _size = size;
        _fill = new VisualBrush(ghost) { Opacity = 0.65 };

        var stroke = (Application.Current?.TryFindResource("AccentBrush") as Brush) ?? Brushes.DeepSkyBlue;
        _frame = new Pen(stroke, 1.5);
        if (_frame.CanFreeze) _frame.Freeze();
    }

    /// <summary>Top-left of the ghost, in coordinates of the adorned element.</summary>
    public void SetOffset(Point offset)
    {
        _offset = offset;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(_offset, _size);
        dc.DrawRoundedRectangle(_fill, _frame, rect, 6, 6);
    }
}

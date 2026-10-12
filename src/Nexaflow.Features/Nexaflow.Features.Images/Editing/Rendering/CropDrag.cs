using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Rendering;

/// <summary>The part of the crop frame a press landed on.</summary>
public enum CropHandle
{
    None,
    Move,
    Left,
    Top,
    Right,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// Dragging the crop frame, worked out on the frame as it is displayed — where the handles are drawn and where the
/// aspect preset is read — before it is mapped back to the straightened frame and stopped at the picture's edge.
/// </summary>
public static class CropDrag
{
    /// <summary>
    /// The handle under <paramref name="point"/>: a corner within <paramref name="grab"/> of it, then an edge, then
    /// the inside (which moves the frame); <see cref="CropHandle.None"/> outside.
    /// </summary>
    public static CropHandle HitTest(SKRect frame, SKPoint point, float grab)
    {
        bool nearLeft = Math.Abs(point.X - frame.Left) <= grab, nearRight = Math.Abs(point.X - frame.Right) <= grab;
        bool nearTop = Math.Abs(point.Y - frame.Top) <= grab, nearBottom = Math.Abs(point.Y - frame.Bottom) <= grab;
        bool withinX = point.X >= frame.Left - grab && point.X <= frame.Right + grab;
        bool withinY = point.Y >= frame.Top - grab && point.Y <= frame.Bottom + grab;

        if (nearLeft && nearTop) return CropHandle.TopLeft;
        if (nearRight && nearTop) return CropHandle.TopRight;
        if (nearLeft && nearBottom) return CropHandle.BottomLeft;
        if (nearRight && nearBottom) return CropHandle.BottomRight;
        if (nearLeft && withinY) return CropHandle.Left;
        if (nearRight && withinY) return CropHandle.Right;
        if (nearTop && withinX) return CropHandle.Top;
        if (nearBottom && withinX) return CropHandle.Bottom;
        return frame.Contains(point) ? CropHandle.Move : CropHandle.None;
    }

    /// <summary>
    /// <paramref name="start"/> after <paramref name="handle"/> is dragged by <paramref name="delta"/>. With an
    /// <paramref name="aspect"/> the frame keeps it: a corner drag grows to the larger of the two sizes it was dragged
    /// to, anchored at the opposite corner; an edge drag carries the other side along, centred.
    /// </summary>
    public static SKRect Drag(SKRect start, CropHandle handle, SKPoint delta, double? aspect)
    {
        if (handle == CropHandle.Move)
        {
            var moved = start;
            moved.Offset(delta);
            return moved;
        }

        float l = start.Left, t = start.Top, r = start.Right, b = start.Bottom;
        if (handle is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft) l += delta.X;
        if (handle is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight) r += delta.X;
        if (handle is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight) t += delta.Y;
        if (handle is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight) b += delta.Y;

        // Dragged through the opposite side: the frame turns inside out rather than vanishing.
        var free = new SKRect(Math.Min(l, r), Math.Min(t, b), Math.Max(l, r), Math.Max(t, b));
        if (aspect is not { } a || handle == CropHandle.None) return free;

        float w = free.Width, h = free.Height;
        switch (handle)
        {
            case CropHandle.Left or CropHandle.Right:
            {
                float height = (float)(w / a), cy = start.MidY;
                return new SKRect(free.Left, cy - height / 2, free.Right, cy + height / 2);
            }
            case CropHandle.Top or CropHandle.Bottom:
            {
                float width = (float)(h * a), cx = start.MidX;
                return new SKRect(cx - width / 2, free.Top, cx + width / 2, free.Bottom);
            }
            default:
            {
                if (w / a > h) h = (float)(w / a); else w = (float)(h * a);
                // Anchored at the corner opposite the handle, wherever the drag left it.
                float ax = handle is CropHandle.TopLeft or CropHandle.BottomLeft ? r : l;
                float ay = handle is CropHandle.TopLeft or CropHandle.TopRight ? b : t;
                float x = (handle is CropHandle.TopLeft or CropHandle.BottomLeft) == (l <= r) ? ax - w : ax;
                float y = (handle is CropHandle.TopLeft or CropHandle.TopRight) == (t <= b) ? ay - h : ay;
                return SKRect.Create(x, y, w, h);
            }
        }
    }
}

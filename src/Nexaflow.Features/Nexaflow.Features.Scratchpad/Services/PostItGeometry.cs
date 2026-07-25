using System;

namespace Nexaflow.Features.Scratchpad.Services;

/// <summary>A note's placement on the canvas: its top-left corner and its size.</summary>
public readonly record struct NoteRect(double X, double Y, double Width, double Height);

/// <summary>
/// The geometry behind dragging a note's edge grips.
/// <para>
/// A note rotates about its own centre, so resizing it in screen axes would both skew the drag and slide
/// the note as the pivot moves. The drag is projected onto the note's local axes and the corner that is
/// <i>not</i> being dragged is pinned in canvas space — which is what makes a rotated note resize the way
/// the pointer expects. Pure so that stays true; the control keeps only the mouse capture.
/// </para>
/// </summary>
public static class PostItGeometry
{
    public const double MinSize = 80;

    /// <summary>
    /// The note's new placement after dragging <paramref name="edge"/> by (<paramref name="dragDx"/>,
    /// <paramref name="dragDy"/>) canvas pixels. <paramref name="edge"/> is a compass string such as
    /// "E", "SW" or "NE"; the sides it names are the ones that move.
    /// </summary>
    public static NoteRect Resize(string edge, double rotationDegrees, NoteRect start,
                                  double dragDx, double dragDy, double minSize = MinSize)
    {
        var theta = rotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(theta);
        var sin = Math.Sin(theta);

        // Project the screen drag onto the note's local axes (rotate by -θ).
        var localDx =  dragDx * cos + dragDy * sin;
        var localDy = -dragDx * sin + dragDy * cos;

        var w = start.Width;
        var h = start.Height;
        if (edge.Contains('E')) w = Math.Max(minSize, start.Width + localDx);
        if (edge.Contains('W')) w = Math.Max(minSize, start.Width - localDx);
        if (edge.Contains('S')) h = Math.Max(minSize, start.Height + localDy);
        if (edge.Contains('N')) h = Math.Max(minSize, start.Height - localDy);

        // Anchor = the corner of the side(s) NOT being dragged, in canvas space from the start geometry…
        var ax0 = edge.Contains('W') ?  start.Width  / 2 : -start.Width  / 2;
        var ay0 = edge.Contains('N') ?  start.Height / 2 : -start.Height / 2;
        var cx0 = start.X + start.Width  / 2;
        var cy0 = start.Y + start.Height / 2;
        var anchorX = cx0 + (ax0 * cos - ay0 * sin);
        var anchorY = cy0 + (ax0 * sin + ay0 * cos);

        // …then solve for the new centre that keeps that same corner pinned.
        var ax1 = edge.Contains('W') ?  w / 2 : -w / 2;
        var ay1 = edge.Contains('N') ?  h / 2 : -h / 2;
        var cx1 = anchorX - (ax1 * cos - ay1 * sin);
        var cy1 = anchorY - (ax1 * sin + ay1 * cos);

        return new NoteRect(cx1 - w / 2, cy1 - h / 2, w, h);
    }

    /// <summary>
    /// Where the corner named by <paramref name="edge"/> sits in canvas space, for a note at
    /// <paramref name="rect"/> rotated by <paramref name="rotationDegrees"/> about its centre. The anchor
    /// used by <see cref="Resize"/> is this corner for the <i>opposite</i> sides.
    /// </summary>
    public static (double X, double Y) Corner(string edge, double rotationDegrees, NoteRect rect)
    {
        var theta = rotationDegrees * Math.PI / 180.0;
        var cos = Math.Cos(theta);
        var sin = Math.Sin(theta);

        var ox = edge.Contains('W') ? -rect.Width  / 2 : rect.Width  / 2;
        var oy = edge.Contains('N') ? -rect.Height / 2 : rect.Height / 2;
        var cx = rect.X + rect.Width  / 2;
        var cy = rect.Y + rect.Height / 2;

        return (cx + (ox * cos - oy * sin), cy + (ox * sin + oy * cos));
    }
}

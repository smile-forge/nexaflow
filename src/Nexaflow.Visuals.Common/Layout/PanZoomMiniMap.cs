using System;
using System.Collections.Generic;

namespace Nexaflow.Visuals.Common.Layout;

/// <summary>The extent of everything laid out on a pan/zoom canvas, in canvas coordinates.</summary>
public readonly record struct CanvasBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width  => MaxX - MinX;
    public double Height => MaxY - MinY;
}

/// <summary>
/// The frozen canvas→minimap mapping for one paint. <see cref="PanZoomMiniMap.Compute"/> produces it and
/// <see cref="PanZoomMiniMap.TranslateForPoint"/> inverts it, so a drag reads the same mapping the boxes
/// were drawn with rather than one that shifts under the pointer.
/// </summary>
public readonly record struct MiniMapMapping(
    double Scale, double OffX, double OffY, double MinX, double MinY,
    double ViewLeft, double ViewTop, double ViewWidth, double ViewHeight);

/// <summary>
/// The arithmetic behind an infinite pan/zoom canvas with an overview minimap — content bounds,
/// cursor-anchored zoom, centring, and the minimap mapping and its inverse.
/// <para>
/// Shared because two features grew the same surface independently: the scratchpad's corkboard of post-it
/// notes and the image viewer's collage. Both need the same four rules — zooming keeps the point under the
/// cursor still, the minimap appears only when something is off-screen, every box drawn on it lands inside
/// it, and clicking it centres the viewport on that point — and none of them need a rendered canvas to be
/// true, so they live here rather than twice over in two code-behinds.
/// </para>
/// </summary>
public static class PanZoomMiniMap
{
    /// <summary>The union of every item's footprint, or null when there is nothing laid out.</summary>
    public static CanvasBounds? Bounds(IEnumerable<(double X, double Y, double Width, double Height)> items)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var any = false;

        foreach (var (x, y, w, h) in items)
        {
            any = true;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + w);
            maxY = Math.Max(maxY, y + h);
        }

        return any ? new CanvasBounds(minX, minY, maxX, maxY) : null;
    }

    /// <summary>
    /// Zoom by <paramref name="factor"/> anchored at (<paramref name="px"/>, <paramref name="py"/>) in
    /// viewport pixels: the canvas point under the pointer stays under the pointer. Scale is clamped, and
    /// the translation follows whatever clamping actually applied — so a refused zoom does not still pan.
    /// </summary>
    public static (double Scale, double X, double Y) ZoomAt(
        double scale, double tx, double ty, double px, double py,
        double factor, double minScale, double maxScale)
    {
        if (scale <= 0 || factor <= 0) return (scale, tx, ty);

        var newScale = Math.Clamp(scale * factor, minScale, maxScale);
        var applied  = newScale / scale;
        return (newScale, px - (px - tx) * applied, py - (py - ty) * applied);
    }

    /// <summary>The translation that centres the whole content in a viewport, at scale 1.</summary>
    public static (double X, double Y) CentreOn(CanvasBounds content, double viewW, double viewH) =>
        ((viewW - content.Width) / 2 - content.MinX, (viewH - content.Height) / 2 - content.MinY);

    /// <summary>
    /// The minimap mapping for the current pan/zoom, or null when the whole canvas already fits on screen
    /// (the minimap hides) or the viewport isn't laid out yet.
    /// </summary>
    public static MiniMapMapping? Compute(CanvasBounds content, double scale, double tx, double ty,
                                          double viewW, double viewH, double mmW, double mmH)
    {
        if (scale <= 0 || viewW <= 0 || viewH <= 0 || mmW <= 0 || mmH <= 0) return null;

        // The viewport rectangle, expressed in canvas coordinates.
        double viewLeft = -tx / scale, viewTop = -ty / scale;
        double viewRight = viewLeft + viewW / scale, viewBottom = viewTop + viewH / scale;

        // Everything already visible → nothing to navigate to.
        if (content.MinX >= viewLeft && content.MinY >= viewTop &&
            content.MaxX <= viewRight && content.MaxY <= viewBottom) return null;

        double minX = Math.Min(content.MinX, viewLeft), minY = Math.Min(content.MinY, viewTop);
        double maxX = Math.Max(content.MaxX, viewRight), maxY = Math.Max(content.MaxY, viewBottom);
        double w = maxX - minX, h = maxY - minY;
        if (w <= 0 || h <= 0) return null;

        var mmScale = Math.Min(mmW / w, mmH / h);
        return new MiniMapMapping(
            mmScale, (mmW - w * mmScale) / 2, (mmH - h * mmScale) / 2, minX, minY,
            viewLeft, viewTop, viewRight - viewLeft, viewBottom - viewTop);
    }

    /// <summary>
    /// Where a canvas rectangle sits on the minimap. <paramref name="minSize"/> keeps a box that would
    /// scale to nothing still visible (and, for the viewport box, still grabbable).
    /// </summary>
    public static (double X, double Y, double W, double H) Box(
        MiniMapMapping m, double x, double y, double w, double h, double minSize) =>
        (m.OffX + (x - m.MinX) * m.Scale, m.OffY + (y - m.MinY) * m.Scale,
         Math.Max(minSize, w * m.Scale), Math.Max(minSize, h * m.Scale));

    /// <summary>Where the viewport box sits on the minimap, for a viewport at (left, top) in canvas space.</summary>
    public static (double X, double Y, double W, double H) ViewportBox(
        MiniMapMapping m, double viewLeft, double viewTop, double minSize = 4) =>
        Box(m, viewLeft, viewTop, m.ViewWidth, m.ViewHeight, minSize);

    /// <summary>
    /// Inverts the mapping: a point on the minimap becomes the canvas translation that centres the viewport
    /// on it. Also returns the viewport's new canvas-space corner, so the drawn viewport box can follow the
    /// drag without recomputing the whole mapping.
    /// </summary>
    public static (double X, double Y, double ViewLeft, double ViewTop) TranslateForPoint(
        MiniMapMapping m, double mmX, double mmY, double scale, double viewW, double viewH)
    {
        if (m.Scale <= 0 || scale <= 0) return (0, 0, m.ViewLeft, m.ViewTop);

        var canvasX = m.MinX + (mmX - m.OffX) / m.Scale;
        var canvasY = m.MinY + (mmY - m.OffY) / m.Scale;
        var viewLeft = canvasX - viewW / scale / 2;
        var viewTop  = canvasY - viewH / scale / 2;

        return (-viewLeft * scale, -viewTop * scale, viewLeft, viewTop);
    }
}

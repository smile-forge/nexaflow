using System;
using System.Collections.Generic;

namespace Nexaflow.Features.Images.Services;

/// <summary>The collage's content extent in canvas coordinates.</summary>
public readonly record struct CollageBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width  => MaxX - MinX;
    public double Height => MaxY - MinY;
}

/// <summary>
/// The frozen canvas→minimap mapping for one paint. <see cref="CollageGeometry.MiniMap"/> produces it and
/// <see cref="CollageGeometry.TranslateForMiniMapPoint"/> inverts it, so a drag reads the same mapping the
/// boxes were drawn with.
/// </summary>
public readonly record struct MiniMapMapping(
    double Scale, double OffX, double OffY, double MinX, double MinY,
    double ViewLeft, double ViewTop, double ViewWidth, double ViewHeight);

/// <summary>
/// The collage canvas's arithmetic — content bounds, cursor-anchored zoom, centring, and the minimap
/// mapping. Pure so each rule ("zooming keeps the point under the cursor still", "the minimap only appears
/// when something is off-screen", "clicking the minimap centres the viewport there") is assertable; the view
/// keeps only the transforms and the hit-testing.
/// </summary>
public static class CollageGeometry
{
    /// <summary>Collage card footprint — mirrors the card template's size.</summary>
    public const double CardW = 170, CardH = 150;

    public const double MinScale = 0.2, MaxScale = 5.0;

    /// <summary>Zoom step per wheel notch.</summary>
    public const double ZoomStep = 1.1;

    /// <summary>The union of every card's footprint, or null when there are no cards.</summary>
    public static CollageBounds? ContentBounds(IEnumerable<(double X, double Y)> cards)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        var any = false;

        foreach (var (x, y) in cards)
        {
            any = true;
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x + CardW);
            maxY = Math.Max(maxY, y + CardH);
        }

        return any ? new CollageBounds(minX, minY, maxX, maxY) : null;
    }

    /// <summary>
    /// One wheel notch of zoom anchored at (<paramref name="px"/>, <paramref name="py"/>): the canvas point
    /// under the cursor stays under the cursor. Scale is clamped, and the translation follows whatever
    /// clamping actually applied.
    /// </summary>
    public static (double Scale, double X, double Y) ZoomAt(
        double scale, double tx, double ty, double px, double py, bool zoomIn)
    {
        if (scale <= 0) return (scale, tx, ty);

        var newScale = Math.Clamp(scale * (zoomIn ? ZoomStep : 1 / ZoomStep), MinScale, MaxScale);
        var applied  = newScale / scale;
        return (newScale, px - (px - tx) * applied, py - (py - ty) * applied);
    }

    /// <summary>The translation that centres the whole collage in a viewport, at scale 1.</summary>
    public static (double X, double Y) CentreOn(CollageBounds content, double viewW, double viewH) =>
        ((viewW - content.Width) / 2 - content.MinX, (viewH - content.Height) / 2 - content.MinY);

    /// <summary>
    /// The minimap mapping for the current pan/zoom, or null when the whole collage already fits on screen
    /// (the minimap hides) or the viewport isn't laid out yet.
    /// </summary>
    public static MiniMapMapping? MiniMap(CollageBounds content, double scale, double tx, double ty,
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

    /// <summary>Where a card's box sits on the minimap.</summary>
    public static (double X, double Y, double W, double H) CardBox(MiniMapMapping m, double cardX, double cardY) =>
        (m.OffX + (cardX - m.MinX) * m.Scale, m.OffY + (cardY - m.MinY) * m.Scale,
         Math.Max(2, CardW * m.Scale), Math.Max(2, CardH * m.Scale));

    /// <summary>Where the viewport box sits on the minimap, for a viewport at (left, top) in canvas space.</summary>
    public static (double X, double Y, double W, double H) ViewportBox(MiniMapMapping m, double viewLeft, double viewTop) =>
        (m.OffX + (viewLeft - m.MinX) * m.Scale, m.OffY + (viewTop - m.MinY) * m.Scale,
         Math.Max(4, m.ViewWidth * m.Scale), Math.Max(4, m.ViewHeight * m.Scale));

    /// <summary>
    /// Inverts the mapping: a point on the minimap becomes the canvas translation that centres the viewport
    /// on it. Also returns the viewport's new canvas-space corner so the drawn viewport box can follow.
    /// </summary>
    public static (double X, double Y, double ViewLeft, double ViewTop) TranslateForMiniMapPoint(
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

using System.Collections.Generic;
using System.Linq;
using Nexaflow.Visuals.Common.Layout;

namespace Nexaflow.Features.Images.Services;

/// <summary>The collage's content extent in canvas coordinates.</summary>
public readonly record struct CollageBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width  => MaxX - MinX;
    public double Height => MaxY - MinY;

    internal CanvasBounds ToCanvas() => new(MinX, MinY, MaxX, MaxY);
}

/// <summary>
/// The collage canvas's arithmetic, in the collage's own terms: fixed-size cards, its zoom step and its
/// scale limits. The rules themselves — cursor-anchored zoom, centring, the minimap mapping and its
/// inverse — are the shared <see cref="PanZoomMiniMap"/>, which the scratchpad's corkboard uses too.
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
        => PanZoomMiniMap.Bounds(cards.Select(c => (c.X, c.Y, CardW, CardH))) is { } b
            ? new CollageBounds(b.MinX, b.MinY, b.MaxX, b.MaxY)
            : null;

    /// <summary>One wheel notch of zoom anchored at the cursor, clamped to the collage's scale limits.</summary>
    public static (double Scale, double X, double Y) ZoomAt(
        double scale, double tx, double ty, double px, double py, bool zoomIn)
        => PanZoomMiniMap.ZoomAt(scale, tx, ty, px, py,
                                 zoomIn ? ZoomStep : 1 / ZoomStep, MinScale, MaxScale);

    /// <summary>The translation that centres the whole collage in a viewport, at scale 1.</summary>
    public static (double X, double Y) CentreOn(CollageBounds content, double viewW, double viewH)
        => PanZoomMiniMap.CentreOn(content.ToCanvas(), viewW, viewH);

    /// <summary>
    /// The minimap mapping for the current pan/zoom, or null when the whole collage already fits on screen
    /// (the minimap hides) or the viewport isn't laid out yet.
    /// </summary>
    public static MiniMapMapping? MiniMap(CollageBounds content, double scale, double tx, double ty,
                                          double viewW, double viewH, double mmW, double mmH)
        => PanZoomMiniMap.Compute(content.ToCanvas(), scale, tx, ty, viewW, viewH, mmW, mmH);

    /// <summary>Where a card's box sits on the minimap.</summary>
    public static (double X, double Y, double W, double H) CardBox(MiniMapMapping m, double cardX, double cardY)
        => PanZoomMiniMap.Box(m, cardX, cardY, CardW, CardH, minSize: 2);

    /// <summary>Where the viewport box sits on the minimap, for a viewport at (left, top) in canvas space.</summary>
    public static (double X, double Y, double W, double H) ViewportBox(MiniMapMapping m, double viewLeft, double viewTop)
        => PanZoomMiniMap.ViewportBox(m, viewLeft, viewTop);

    /// <summary>
    /// Inverts the mapping: a point on the minimap becomes the canvas translation that centres the viewport
    /// on it, plus the viewport's new canvas-space corner so the drawn box can follow.
    /// </summary>
    public static (double X, double Y, double ViewLeft, double ViewTop) TranslateForMiniMapPoint(
        MiniMapMapping m, double mmX, double mmY, double scale, double viewW, double viewH)
        => PanZoomMiniMap.TranslateForPoint(m, mmX, mmY, scale, viewW, viewH);
}

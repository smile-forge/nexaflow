using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Nexaflow.Visuals.Common.Layout;

namespace Nexaflow.Features.Images.Services;

/// <summary>The collage's content extent in canvas coordinates.</summary>
public readonly record struct CollageBounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width  => MaxX - MinX;
    public double Height => MaxY - MinY;

    /// <summary>The same extent in the terms the pan/zoom surface reckons in.</summary>
    public CanvasBounds ToCanvasBounds() => new(MinX, MinY, MaxX, MaxY);
}

/// <summary>
/// What the collage canvas is, as opposed to how a pan/zoom canvas behaves: fixed-footprint cards
/// scattered across an unbounded surface, and the scale range that keeps them findable.
/// <para>
/// The behaviour — cursor-anchored zoom, the drag, the overview and its inverse mapping — is the shared
/// <see cref="PanZoomSurface"/>, which the scratchpad's corkboard and markdown graph diagrams sit on
/// too. What is left here is the two things only the collage knows: how big a card is, and where the
/// cards are.
/// </para>
/// </summary>
public static class CollageGeometry
{
    /// <summary>Collage card footprint — mirrors the card template's size.</summary>
    public const double CardW = 170, CardH = 150;

    /// <summary>How far the canvas may be zoomed. The floor stops a big collage being shrunk to
    /// nothing; the ceiling stops a thumbnail filling the screen as a blur.</summary>
    public const double MinScale = 0.2, MaxScale = 5.0;

    /// <summary>The union of every card's footprint, or null when there are no cards.</summary>
    public static CollageBounds? ContentBounds(IEnumerable<(double X, double Y)> cards)
        => PanZoomMiniMap.Bounds(cards.Select(c => (c.X, c.Y, CardW, CardH))) is { } b
            ? new CollageBounds(b.MinX, b.MinY, b.MaxX, b.MaxY)
            : null;

    /// <summary>The translation that centres the whole collage in a viewport, at scale 1.</summary>
    public static (double X, double Y) CentreOn(CollageBounds content, double viewW, double viewH)
        => PanZoomMiniMap.CentreOn(content.ToCanvasBounds(), viewW, viewH);

    /// <summary>Every card as a box for the overview, so it shows the shape of the pile rather than one
    /// blank rectangle you cannot navigate by.</summary>
    public static IEnumerable<MiniMapItem> MiniMapItems(IEnumerable<(double X, double Y)> cards, Brush fill)
        => cards.Select(c => new MiniMapItem(c.X, c.Y, CardW, CardH, fill));
}

using System.Linq;
using Nexaflow.Features.Images.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The collage canvas's arithmetic: the cursor-anchored zoom, the initial centring, and the minimap's
/// canvas↔minimap mapping and its inverse.
/// <para>
/// This is where a pan/zoom surface actually goes wrong — a zoom that drifts the content out from under the
/// cursor, a minimap that stays on screen when there is nothing off it, or a click that lands the viewport
/// somewhere other than where the user pointed. None of that needs a rendered canvas to assert, so it is
/// pulled out of the view and checked directly.
/// </para>
/// </summary>
[TestClass]
public class CollageGeometryTests
{
    private static void AssertClose(double expected, double actual, string because = "") =>
        Assert.IsTrue(System.Math.Abs(expected - actual) < 1e-6,
                      $"{because} (expected {expected}, got {actual})");

    // ── Content bounds ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-collage-panzoom")]
    public void ContentBounds_CoverEveryCardsWholeFootprint()
    {
        var bounds = CollageGeometry.ContentBounds([(0d, 0d), (100d, 40d)]);

        Assert.IsNotNull(bounds);
        AssertClose(0, bounds!.Value.MinX);
        AssertClose(100 + CollageGeometry.CardW, bounds.Value.MaxX, "a card extends past its own origin");
        AssertClose(40 + CollageGeometry.CardH, bounds.Value.MaxY);
    }

    [TestMethod]
    [CoversNode("images-collage-panzoom")]
    public void ContentBounds_OfAnEmptyCollage_AreNothingRatherThanZero()
        => Assert.IsNull(CollageGeometry.ContentBounds([]),
                         "an empty collage has no extent — zero would centre the view on a phantom card");

    // ── Cursor-anchored zoom ──────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-collage-panzoom")]
    public void Zooming_KeepsThePointUnderTheCursorUnderTheCursor()
    {
        const double px = 300, py = 200;
        double scale = 1, tx = -50, ty = 25;

        // The canvas point currently beneath the cursor.
        var canvasX = (px - tx) / scale;
        var canvasY = (py - ty) / scale;

        (scale, tx, ty) = CollageGeometry.ZoomAt(scale, tx, ty, px, py, zoomIn: true);

        AssertClose(px, canvasX * scale + tx, "the same canvas point must still project onto the cursor");
        AssertClose(py, canvasY * scale + ty);
        Assert.IsTrue(scale > 1, "a wheel-up notch zooms in");
    }

    [TestMethod]
    [CoversNode("images-collage-panzoom")]
    public void Zoom_IsClampedAtBothEnds_AndTheTranslationFollowsTheClamp()
    {
        // Already at the ceiling: another notch in must change nothing at all, translation included.
        var (scale, tx, ty) = CollageGeometry.ZoomAt(CollageGeometry.MaxScale, 10, 20, 300, 200, zoomIn: true);
        AssertClose(CollageGeometry.MaxScale, scale);
        AssertClose(10, tx, "a refused zoom must not still pan the canvas");
        AssertClose(20, ty);

        var (floor, _, _) = CollageGeometry.ZoomAt(CollageGeometry.MinScale, 0, 0, 0, 0, zoomIn: false);
        AssertClose(CollageGeometry.MinScale, floor);
    }

    // ── Centring ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-collage-panzoom")]
    public void CentringPutsTheCollageMiddleInTheViewportMiddle()
    {
        var bounds = new CollageBounds(100, 200, 500, 400);   // 400 × 200, centred on (300, 300)

        var (x, y) = CollageGeometry.CentreOn(bounds, viewW: 800, viewH: 600);

        AssertClose(400, 300 + x, "the content centre lands on the viewport centre");
        AssertClose(300, 300 + y);
    }

    // ── Minimap ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-collage-minimap")]
    public void MiniMap_StaysHidden_WhileTheWholeCollageFitsOnScreen()
    {
        var bounds = new CollageBounds(0, 0, 200, 150);

        var mapping = CollageGeometry.MiniMap(bounds, scale: 1, tx: 0, ty: 0,
                                              viewW: 800, viewH: 600, mmW: 170, mmH: 120);

        Assert.IsNull(mapping, "an overview box is noise when there is nothing to navigate to");
    }

    [TestMethod]
    [CoversNode("images-collage-minimap")]
    public void MiniMap_AppearsWithABoxForEachCardAndOneForTheViewport()
    {
        var bounds = new CollageBounds(0, 0, 4000, 3000);   // far larger than the viewport

        var mapping = CollageGeometry.MiniMap(bounds, scale: 1, tx: 0, ty: 0,
                                              viewW: 800, viewH: 600, mmW: 170, mmH: 120);

        Assert.IsNotNull(mapping);
        var m = mapping!.Value;

        // Everything drawn on the minimap must land inside it — check the furthest card, the one whose
        // footprint reaches exactly the far corner of the bounds.
        var (cx, cy, cw, ch) = CollageGeometry.CardBox(m, 4000 - CollageGeometry.CardW, 3000 - CollageGeometry.CardH);
        Assert.IsTrue(cx >= -1e-6 && cy >= -1e-6, "a card box must not be drawn off the left/top of the minimap");
        Assert.IsTrue(cx + cw <= 170 + 1e-6 && cy + ch <= 120 + 1e-6, "nor past its right/bottom");

        var (vx, vy, vw, vh) = CollageGeometry.ViewportBox(m, m.ViewLeft, m.ViewTop);
        AssertClose(m.OffX, vx, "the viewport sits at the mapped minimum, offset by the letterbox margin");
        AssertClose(m.OffY, vy);
        Assert.IsTrue(vw >= 4 && vh >= 4, "the viewport box stays grabbable even on a huge collage");
    }

    [TestMethod]
    [CoversNode("images-collage-minimap")]
    public void ClickingTheMiniMap_CentresTheViewportOnThatPoint()
    {
        var bounds = new CollageBounds(0, 0, 4000, 3000);
        var m = CollageGeometry.MiniMap(bounds, 1, 0, 0, 800, 600, 170, 120)!.Value;

        // Aim at a known canvas point by mapping it forward, then clicking exactly there.
        const double targetX = 2500, targetY = 1800;
        var mmX = m.OffX + (targetX - m.MinX) * m.Scale;
        var mmY = m.OffY + (targetY - m.MinY) * m.Scale;

        var (tx, ty, viewLeft, viewTop) =
            CollageGeometry.TranslateForMiniMapPoint(m, mmX, mmY, scale: 1, viewW: 800, viewH: 600);

        AssertClose(targetX, viewLeft + 400, "the clicked point ends up in the middle of the viewport");
        AssertClose(targetY, viewTop + 300);
        AssertClose(-viewLeft, tx, "and the canvas translation matches that viewport corner");
        AssertClose(-viewTop, ty);
    }

    [TestMethod]
    [CoversNode("images-collage-minimap")]
    public void MiniMap_MappingIsUnavailable_BeforeTheViewportHasBeenLaidOut()
    {
        var bounds = new CollageBounds(0, 0, 4000, 3000);

        Assert.IsNull(CollageGeometry.MiniMap(bounds, 1, 0, 0, viewW: 0, viewH: 0, mmW: 170, mmH: 120),
                      "a zero-sized viewport would divide the mapping by nothing");
    }
}

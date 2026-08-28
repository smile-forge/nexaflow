using System;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Layout;

namespace Nexaflow.Tests.Visuals.Layout;

/// <summary>
/// The arithmetic behind an infinite pan/zoom canvas with an overview minimap, shared by the scratchpad's
/// corkboard and the image viewer's collage.
/// <para>
/// These are the four ways such a surface goes wrong, and none of them need a rendered canvas: a zoom that
/// drifts the content out from under the cursor, an overview box that stays on screen when there is nothing
/// off it, a box drawn outside the minimap it belongs to, and a click that lands the viewport somewhere
/// other than where the user pointed.
/// </para>
/// </summary>
[TestClass]
public class PanZoomMiniMapTests
{
    private static void AssertClose(double expected, double actual, string because = "") =>
        Assert.IsTrue(Math.Abs(expected - actual) < 1e-6, $"{because} (expected {expected}, got {actual})");

    // ── Content bounds ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("minimap-navigation")]
    public void BoundsCoverEveryItemsWholeFootprint_AtItsOwnSize()
    {
        // Notes are individually sized — unlike collage cards — so the bounds must use each item's own
        // width and height rather than a single card constant.
        var bounds = PanZoomMiniMap.Bounds([(0d, 0d, 200d, 150d), (500d, 40d, 80d, 400d)]);

        Assert.IsNotNull(bounds);
        AssertClose(0, bounds!.Value.MinX);
        AssertClose(580, bounds.Value.MaxX, "the far item ends at its own right edge");
        AssertClose(440, bounds.Value.MaxY, "and the tall one sets the bottom");
    }

    [TestMethod]
    [CoversNode("minimap-navigation")]
    public void AnEmptyCanvasHasNoBounds_RatherThanZeroSizedOnes()
        => Assert.IsNull(PanZoomMiniMap.Bounds([]),
                         "zero bounds would centre the view on a phantom item at the origin");

    // ── Cursor-anchored zoom ──────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("wheel-zoom")]
    public void ZoomingKeepsThePointUnderTheCursorUnderTheCursor()
    {
        const double px = 300, py = 200;
        double scale = 1, tx = -50, ty = 25;

        var canvasX = (px - tx) / scale;
        var canvasY = (py - ty) / scale;

        (scale, tx, ty) = PanZoomMiniMap.ZoomAt(scale, tx, ty, px, py, factor: 1.1, minScale: 0.08, maxScale: 4);

        AssertClose(px, canvasX * scale + tx, "the same canvas point must still project onto the cursor");
        AssertClose(py, canvasY * scale + ty);
    }

    [TestMethod]
    [CoversNode("wheel-zoom")]
    public void ZoomIsClampedAtBothEnds_AndTheTranslationFollowsTheClamp()
    {
        var (scale, tx, ty) = PanZoomMiniMap.ZoomAt(4.0, 10, 20, 300, 200, factor: 1.1, minScale: 0.08, maxScale: 4);
        AssertClose(4.0, scale);
        AssertClose(10, tx, "a refused zoom must not still pan the canvas");
        AssertClose(20, ty);

        var (floor, _, _) = PanZoomMiniMap.ZoomAt(0.08, 0, 0, 0, 0, factor: 1 / 1.1, minScale: 0.08, maxScale: 4);
        AssertClose(0.08, floor);
    }

    [TestMethod]
    [CoversNode("zoom-preset-picker")]
    public void APresetZoomIsTheSameOperationAimedAtTheViewportCentre()
    {
        // The picker sets an absolute percentage, expressed as the factor that gets there from the current
        // scale — so 50% from 100% must land exactly on 0.5, not near it.
        const double current = 1.0, target = 0.5;

        var (scale, _, _) = PanZoomMiniMap.ZoomAt(current, 0, 0, 400, 300,
                                                  factor: target / current, minScale: 0.08, maxScale: 4);

        AssertClose(0.5, scale);
    }

    // ── Centring ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("pan-canvas")]
    public void CentringPutsTheContentMiddleInTheViewportMiddle()
    {
        var bounds = new CanvasBounds(100, 200, 500, 400);   // 400 × 200, centred on (300, 300)

        var (x, y) = PanZoomMiniMap.CentreOn(bounds, viewW: 800, viewH: 600);

        AssertClose(400, 300 + x);
        AssertClose(300, 300 + y);
    }

    // ── Minimap ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("minimap-navigation")]
    public void TheMiniMapStaysHidden_WhileEverythingFitsOnScreen()
    {
        var bounds = new CanvasBounds(0, 0, 200, 150);

        Assert.IsNull(PanZoomMiniMap.Compute(bounds, scale: 1, tx: 0, ty: 0,
                                             viewW: 800, viewH: 600, mmW: 158, mmH: 98),
                      "an overview box is noise when there is nothing to navigate to");
    }

    [TestMethod]
    [CoversNode("minimap-navigation")]
    public void EveryBoxDrawnOnTheMiniMapLandsInsideIt()
    {
        var bounds = new CanvasBounds(0, 0, 4000, 3000);

        var m = PanZoomMiniMap.Compute(bounds, 1, 0, 0, 800, 600, 158, 98)!.Value;

        // The furthest item — the one whose footprint reaches the far corner of the bounds.
        var (x, y, w, h) = PanZoomMiniMap.Box(m, 3800, 2850, 200, 150, minSize: 2);
        Assert.IsTrue(x >= -1e-6 && y >= -1e-6, "a box must not be drawn off the left/top of the minimap");
        Assert.IsTrue(x + w <= 158 + 1e-6 && y + h <= 98 + 1e-6, "nor past its right/bottom");

        var (_, _, vw, vh) = PanZoomMiniMap.ViewportBox(m, m.ViewLeft, m.ViewTop);
        Assert.IsTrue(vw >= 4 && vh >= 4, "the viewport box stays grabbable even on a huge canvas");
    }

    [TestMethod]
    [CoversNode("minimap-navigation")]
    public void ATinyItemStillGetsAVisibleBox()
    {
        var m = PanZoomMiniMap.Compute(new CanvasBounds(0, 0, 40000, 30000), 1, 0, 0, 800, 600, 158, 98)!.Value;

        var (_, _, w, h) = PanZoomMiniMap.Box(m, 0, 0, 1, 1, minSize: 2);

        Assert.AreEqual(2, w, "scaled honestly this would round to nothing and the item would vanish");
        Assert.AreEqual(2, h);
    }

    [TestMethod]
    [CoversNode("minimap-navigation")]
    public void ClickingTheMiniMapCentresTheViewportOnThatPoint()
    {
        var m = PanZoomMiniMap.Compute(new CanvasBounds(0, 0, 4000, 3000), 1, 0, 0, 800, 600, 158, 98)!.Value;

        // Aim at a known canvas point by mapping it forward, then clicking exactly there.
        const double targetX = 2500, targetY = 1800;
        var mmX = m.OffX + (targetX - m.MinX) * m.Scale;
        var mmY = m.OffY + (targetY - m.MinY) * m.Scale;

        var (tx, ty, viewLeft, viewTop) =
            PanZoomMiniMap.TranslateForPoint(m, mmX, mmY, scale: 1, viewW: 800, viewH: 600);

        AssertClose(targetX, viewLeft + 400, "the clicked point ends up in the middle of the viewport");
        AssertClose(targetY, viewTop + 300);
        AssertClose(-viewLeft, tx, "and the canvas translation matches that viewport corner");
        AssertClose(-viewTop, ty);
    }

    [TestMethod]
    [CoversNode("minimap-navigation")]
    public void ADragAtADifferentZoomStillCentresCorrectly()
    {
        // The mapping is frozen at the zoom it was drawn with; inverting it has to use the live scale for
        // the viewport size, or the drag drifts as you zoom.
        var m = PanZoomMiniMap.Compute(new CanvasBounds(0, 0, 4000, 3000), 2, 0, 0, 800, 600, 158, 98)!.Value;

        var (_, _, viewLeft, viewTop) =
            PanZoomMiniMap.TranslateForPoint(m, m.OffX, m.OffY, scale: 2, viewW: 800, viewH: 600);

        AssertClose(m.MinX - 200, viewLeft, "at 2x the viewport covers half as much canvas");
        AssertClose(m.MinY - 150, viewTop);
    }

    [TestMethod]
    [CoversNode("minimap-navigation")]
    public void NoMappingBeforeTheViewportHasBeenLaidOut()
    {
        var bounds = new CanvasBounds(0, 0, 4000, 3000);

        Assert.IsNull(PanZoomMiniMap.Compute(bounds, 1, 0, 0, viewW: 0, viewH: 0, mmW: 158, mmH: 98),
                      "a zero-sized viewport would divide the mapping by nothing");
        Assert.IsNull(PanZoomMiniMap.Compute(bounds, scale: 0, tx: 0, ty: 0,
                                             viewW: 800, viewH: 600, mmW: 158, mmH: 98));
    }
}

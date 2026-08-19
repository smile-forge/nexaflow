using System.Linq;
using Nexaflow.Features.Images.Services;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Layout;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The collage canvas: what it is a window onto, and that the shared pan/zoom surface behaves correctly
/// when pointed at it.
/// <para>
/// The mechanism — cursor-anchored zoom, the overview mapping and its inverse — belongs to
/// <see cref="PanZoomMiniMap"/> and is asserted there in its own right. What is checked here is the part
/// the collage supplies and could get wrong on its own: a card's footprint reaching past its origin, an
/// empty collage having no extent at all, and the scale limits it hands the surface. The last three
/// drive the shared arithmetic with collage-shaped inputs — this is where a pan/zoom surface actually
/// goes wrong, so it is worth asserting for the collage's own numbers rather than assumed from the
/// generic case.
/// </para>
/// </summary>
[TestClass]
public class CollageGeometryTests
{
    /// <summary>The overview the collage draws into — <see cref="PanZoomSurface"/>'s fixed size.</summary>
    private const double MiniW = 168, MiniH = 112;

    private static void AssertClose(double expected, double actual, string because = "") =>
        Assert.IsTrue(System.Math.Abs(expected - actual) < 1e-6,
                      $"{because} (expected {expected}, got {actual})");

    private static MiniMapMapping? MiniMap(CollageBounds content, double scale, double tx, double ty,
                                           double viewW, double viewH) =>
        PanZoomMiniMap.Compute(content.ToCanvasBounds(), scale, tx, ty, viewW, viewH, MiniW, MiniH);

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

    // ── Cursor-anchored zoom, at the collage's limits ─────────────────────────

    [TestMethod]
    [CoversNode("images-collage-panzoom")]
    public void Zooming_KeepsThePointUnderTheCursorUnderTheCursor()
    {
        const double px = 300, py = 200;
        double scale = 1, tx = -50, ty = 25;

        // The canvas point currently beneath the cursor.
        var canvasX = (px - tx) / scale;
        var canvasY = (py - ty) / scale;

        (scale, tx, ty) = PanZoomMiniMap.ZoomAt(scale, tx, ty, px, py, factor: 1.15,
                                                CollageGeometry.MinScale, CollageGeometry.MaxScale);

        AssertClose(px, canvasX * scale + tx, "the same canvas point must still project onto the cursor");
        AssertClose(py, canvasY * scale + ty);
        Assert.IsTrue(scale > 1, "a wheel-up notch zooms in");
    }

    [TestMethod]
    [CoversNode("images-collage-panzoom")]
    public void Zoom_IsClampedAtBothEnds_AndTheTranslationFollowsTheClamp()
    {
        // Already at the ceiling: another notch in must change nothing at all, translation included.
        var (scale, tx, ty) = PanZoomMiniMap.ZoomAt(CollageGeometry.MaxScale, 10, 20, 300, 200, 1.15,
                                                    CollageGeometry.MinScale, CollageGeometry.MaxScale);
        AssertClose(CollageGeometry.MaxScale, scale);
        AssertClose(10, tx, "a refused zoom must not still pan the canvas");
        AssertClose(20, ty);

        var (floor, _, _) = PanZoomMiniMap.ZoomAt(CollageGeometry.MinScale, 0, 0, 0, 0, 1 / 1.15,
                                                  CollageGeometry.MinScale, CollageGeometry.MaxScale);
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

        var mapping = MiniMap(bounds, scale: 1, tx: 0, ty: 0, viewW: 800, viewH: 600);

        Assert.IsNull(mapping, "an overview box is noise when there is nothing to navigate to");
    }

    [TestMethod]
    [CoversNode("images-collage-minimap")]
    public void MiniMap_AppearsWithABoxForEachCardAndOneForTheViewport()
    {
        var bounds = new CollageBounds(0, 0, 4000, 3000);   // far larger than the viewport

        var mapping = MiniMap(bounds, scale: 1, tx: 0, ty: 0, viewW: 800, viewH: 600);

        Assert.IsNotNull(mapping);
        var m = mapping!.Value;

        // Everything drawn on the minimap must land inside it — check the furthest card, the one whose
        // footprint reaches exactly the far corner of the bounds.
        var far  = CollageGeometry.MiniMapItems(
            [(4000 - CollageGeometry.CardW, 3000 - CollageGeometry.CardH)], fill: null!).Single();
        var (cx, cy, cw, ch) = PanZoomMiniMap.Box(m, far.X, far.Y, far.Width, far.Height, minSize: 2);
        Assert.IsTrue(cx >= -1e-6 && cy >= -1e-6, "a card box must not be drawn off the left/top of the minimap");
        Assert.IsTrue(cx + cw <= MiniW + 1e-6 && cy + ch <= MiniH + 1e-6, "nor past its right/bottom");

        var (vx, vy, vw, vh) = PanZoomMiniMap.ViewportBox(m, m.ViewLeft, m.ViewTop);
        AssertClose(m.OffX, vx, "the viewport sits at the mapped minimum, offset by the letterbox margin");
        AssertClose(m.OffY, vy);
        Assert.IsTrue(vw >= 4 && vh >= 4, "the viewport box stays grabbable even on a huge collage");
    }

    [TestMethod]
    [CoversNode("images-collage-minimap")]
    public void ClickingTheMiniMap_CentresTheViewportOnThatPoint()
    {
        var bounds = new CollageBounds(0, 0, 4000, 3000);
        var m = MiniMap(bounds, 1, 0, 0, 800, 600)!.Value;

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
    [CoversNode("images-collage-minimap")]
    public void MiniMap_MappingIsUnavailable_BeforeTheViewportHasBeenLaidOut()
    {
        var bounds = new CollageBounds(0, 0, 4000, 3000);

        Assert.IsNull(MiniMap(bounds, 1, 0, 0, viewW: 0, viewH: 0),
                      "a zero-sized viewport would divide the mapping by nothing");
    }
}

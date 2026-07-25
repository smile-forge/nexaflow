using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Layout;

namespace Nexaflow.Tests.Core.Visuals.Layout;

/// <summary>
/// Fitting fixed-size content into a viewport, shared by the DICOM stage and the SVG canvas.
/// <para>
/// Neither viewer can lean on WPF's <c>Stretch</c> — DICOM draws its measurement overlay in screen space so
/// annotation strokes stay one width at any zoom, and the SVG canvas re-tessellates rather than scaling a
/// bitmap — so both own the transform, and both fail the same three ways. An image fitted a few percent too
/// large loses its edge with nothing to show for it; a zoom that drifts moves an annotation off the anatomy
/// it was measuring; and a viewport layout hasn't measured yet divides by zero.
/// </para>
/// </summary>
[TestClass]
[CoversNode("dicom-zoom-pan")]
public class ViewportFitTests
{
    private const double Tol = 1e-9;

    // ── Fit ───────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dicom-zoom-buttons")]
    public void Fit_ScalesToTheTighterAxis_SoTheWholeThingIsOnScreen()
    {
        // Wide content in a square viewport: width is the constraint.
        var m = ViewportFit.Fit(400, 100, 200, 200);

        Assert.AreEqual(0.5, m.M11, Tol, "scaled by the tighter of the two ratios");
        Assert.AreEqual(0.5, m.M22, Tol, "uniform — nothing here may be stretched");
        Assert.IsTrue(400 * m.M11 <= 200 + Tol && 100 * m.M22 <= 200 + Tol, "all of it is inside the viewport");
    }

    [TestMethod]
    [CoversNode("dicom-zoom-buttons")]
    public void Fit_LetterboxesOnTheSlackAxis()
    {
        var m = ViewportFit.Fit(400, 100, 200, 200);

        Assert.AreEqual(0, m.OffsetX, Tol, "no slack across the constrained axis");
        Assert.AreEqual((200 - 100 * 0.5) / 2, m.OffsetY, Tol, "equal margins above and below");
    }

    [TestMethod]
    [CoversNode("dicom-zoom-buttons")]
    public void Fit_EnlargesContentSmallerThanTheViewport()
    {
        // Fit means fit, not "shrink only" — a 4×4 DICOM sample or a 16px icon fills the window.
        var m = ViewportFit.Fit(4, 4, 800, 400);

        Assert.AreEqual(100, m.M11, Tol);
        Assert.AreEqual((800 - 400) / 2, m.OffsetX, Tol);
    }

    [TestMethod]
    [CoversNode("dicom-zoom-buttons")]
    public void Fit_OnAnUnmeasuredViewport_IsTheIdentity_NotAnInfiniteScale()
    {
        // The content arrives before layout has sized the host. Dividing by zero here would put an infinite
        // scale on it; the caller retries once a SizeChanged fires.
        Assert.AreEqual(Matrix.Identity, ViewportFit.Fit(512, 512, 0, 0));
        Assert.AreEqual(Matrix.Identity, ViewportFit.Fit(0, 0, 800, 600));
    }

    // ── Fit, as a separate scale and translation ──────────────────────────────

    [TestMethod]
    [CoversNode("svg-canvas")]
    public void FitScaled_AgreesWithTheMatrixForm()
    {
        var m = ViewportFit.Fit(400, 100, 200, 200);
        var (scale, x, y) = ViewportFit.FitScaled(400, 100, 200, 200);

        Assert.AreEqual(m.M11, scale, Tol, "one rule, two shapes — they must not drift apart");
        Assert.AreEqual(m.OffsetX, x, Tol);
        Assert.AreEqual(m.OffsetY, y, Tol);
    }

    [TestMethod]
    [CoversNode("svg-canvas")]
    public void FitScaled_ClampsToTheZoomRange_AndStillCentres()
    {
        // A 1×1 icon in a big window would fit at 800×, well past any sane zoom ceiling.
        var (scale, x, y) = ViewportFit.FitScaled(1, 1, 800, 600, minScale: 0.05, maxScale: 64);

        Assert.AreEqual(64, scale, Tol, "the fit is clamped to the same range the wheel obeys");
        Assert.AreEqual((800 - 64) / 2, x, Tol, "and it is still centred at the clamped size");
        Assert.AreEqual((600 - 64) / 2, y, Tol);
    }

    [TestMethod]
    [CoversNode("svg-canvas")]
    public void FitScaled_OnAnUnmeasuredViewport_ReturnsZero_SoTheCallerLeavesItsTransformAlone()
    {
        Assert.AreEqual(0, ViewportFit.FitScaled(120, 120, 0, 0).Scale);
        Assert.AreEqual(0, ViewportFit.FitScaled(0, 0, 800, 600).Scale, "nothing loaded yet");
    }

    // ── Actual size ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dicom-zoom-buttons")]
    public void ActualSize_IsNaturalSize_Centred()
    {
        var m = ViewportFit.ActualSize(100, 50, 300, 200);

        Assert.AreEqual(1, m.M11, Tol);
        Assert.AreEqual(1, m.M22, Tol);
        Assert.AreEqual(100, m.OffsetX, Tol);
        Assert.AreEqual(75, m.OffsetY, Tol);
    }

    [TestMethod]
    [CoversNode("dicom-zoom-buttons")]
    public void ActualSize_OfOversizedContent_OverflowsRatherThanShrinking()
    {
        // A 512² CT slice at 1:1 in a small stage. The negative offset is the point: the frame is hosted so
        // that it keeps natural size and is clipped — clamping here would silently show a scaled crop while
        // claiming to be 1:1.
        var m = ViewportFit.ActualSize(512, 512, 200, 200);

        Assert.AreEqual(1, m.M11, Tol, "1:1 stays 1:1 however small the viewport");
        Assert.IsTrue(m.OffsetX < 0 && m.OffsetY < 0, "centred on content wider than the viewport");
        Assert.AreEqual(-156, m.OffsetX, Tol);
    }

    // ── Zoom ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ZoomAt_KeepsThePointUnderTheCursorUnderTheCursor()
    {
        var view = ViewportFit.Fit(512, 512, 800, 600);
        var cursor = new Point(310, 240);
        var before = ViewportFit.ToContent(view, cursor);

        var zoomed = ViewportFit.ZoomAt(view, cursor, 1.15);
        var after = ViewportFit.ToContent(zoomed, cursor);

        Assert.AreEqual(before.X, after.X, 1e-6, "the anchored point does not slide out from under the pointer");
        Assert.AreEqual(before.Y, after.Y, 1e-6);
        Assert.IsTrue(zoomed.M11 > view.M11, "and it did actually zoom in");
    }

    [TestMethod]
    public void ZoomAt_InAndBackOut_ReturnsToWhereItStarted()
    {
        var view = ViewportFit.Fit(512, 512, 800, 600);
        var cursor = new Point(120, 90);

        var round = ViewportFit.ZoomAt(ViewportFit.ZoomAt(view, cursor, 1.15), cursor, 1 / 1.15);

        Assert.AreEqual(view.M11, round.M11, 1e-9);
        Assert.AreEqual(view.OffsetX, round.OffsetX, 1e-6, "no drift accumulates across a wheel back and forth");
        Assert.AreEqual(view.OffsetY, round.OffsetY, 1e-6);
    }

    [TestMethod]
    public void ToContent_MapsTheViewportCentreToTheContentCentre_UnderFit()
    {
        var m = ViewportFit.Fit(512, 256, 800, 600);

        var p = ViewportFit.ToContent(m, new Point(400, 300));

        Assert.AreEqual(256, p.X, 1e-6);
        Assert.AreEqual(128, p.Y, 1e-6);
    }
}

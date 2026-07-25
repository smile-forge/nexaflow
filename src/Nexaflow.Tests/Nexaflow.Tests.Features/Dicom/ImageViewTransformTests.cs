using System.Windows;
using System.Windows.Media;
using Nexaflow.Features.Dicom.Viewing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Dicom;

/// <summary>
/// The image→screen transform for the DICOM stage.
/// <para>
/// A medical image is not an ordinary picture: the whole of it has to be on screen and the measurement
/// overlay has to land on the pixels it was drawn against. Both depend on this matrix being right, and
/// neither shows a symptom you would notice — an image fitted a few percent too large loses its edge
/// silently, and a zoom that drifts moves an annotation off the anatomy it was measuring.
/// </para>
/// </summary>
[TestClass]
[CoversNode("dicom-zoom-pan")]
public class ImageViewTransformTests
{
    private const double Tol = 1e-9;

    // ── Fit ───────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dicom-zoom-buttons")]
    public void Fit_ScalesToTheTighterAxis_SoTheWholeImageIsOnScreen()
    {
        // A wide image in a square stage: width is the constraint.
        var m = ImageViewTransform.Fit(400, 100, 200, 200);

        Assert.AreEqual(0.5, m.M11, Tol, "scaled by the tighter of the two ratios");
        Assert.AreEqual(0.5, m.M22, Tol, "uniform — a DICOM image must never be stretched");
        Assert.IsTrue(400 * m.M11 <= 200 + Tol && 100 * m.M22 <= 200 + Tol, "the whole image is inside the stage");
    }

    [TestMethod]
    [CoversNode("dicom-zoom-buttons")]
    public void Fit_LetterboxesOnTheSlackAxis()
    {
        var m = ImageViewTransform.Fit(400, 100, 200, 200);

        Assert.AreEqual(0, m.OffsetX, Tol, "no slack across the constrained axis");
        Assert.AreEqual((200 - 100 * 0.5) / 2, m.OffsetY, Tol, "equal margins above and below");
    }

    [TestMethod]
    [CoversNode("dicom-zoom-buttons")]
    public void Fit_EnlargesAnImageSmallerThanTheStage()
    {
        // A 4x4 sample in a real viewport — fit means fit, not "shrink only".
        var m = ImageViewTransform.Fit(4, 4, 800, 400);

        Assert.AreEqual(100, m.M11, Tol);
        Assert.AreEqual((800 - 400) / 2, m.OffsetX, Tol);
    }

    [TestMethod]
    [CoversNode("dicom-zoom-buttons")]
    public void Fit_OnAnUnmeasuredStage_IsTheIdentity_NotAnInfiniteScale()
    {
        // The first bitmap arrives before layout has sized the stage. Dividing by zero here would put an
        // infinite scale on the frame; the view retries once OnRenderSizeChanged fires.
        Assert.AreEqual(Matrix.Identity, ImageViewTransform.Fit(512, 512, 0, 0));
        Assert.AreEqual(Matrix.Identity, ImageViewTransform.Fit(0, 0, 800, 600));
    }

    // ── Actual size ───────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("dicom-zoom-buttons")]
    public void ActualSize_IsSourcePixels_Centred()
    {
        var m = ImageViewTransform.ActualSize(100, 50, 300, 200);

        Assert.AreEqual(1, m.M11, Tol);
        Assert.AreEqual(1, m.M22, Tol);
        Assert.AreEqual(100, m.OffsetX, Tol);
        Assert.AreEqual(75, m.OffsetY, Tol);
    }

    [TestMethod]
    [CoversNode("dicom-zoom-buttons")]
    public void ActualSize_OfAnOversizedImage_OverflowsRatherThanShrinking()
    {
        // A 512² CT slice at 1:1 in a small stage. The negative offset is the point: the frame sits on a
        // Canvas so it keeps its natural size and is clipped — clamping it here would silently show a
        // scaled crop while claiming to be 1:1.
        var m = ImageViewTransform.ActualSize(512, 512, 200, 200);

        Assert.AreEqual(1, m.M11, Tol, "1:1 stays 1:1 however small the stage");
        Assert.IsTrue(m.OffsetX < 0 && m.OffsetY < 0, "centred on an image wider than the viewport");
        Assert.AreEqual(-156, m.OffsetX, Tol);
    }

    // ── Zoom ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ZoomAt_KeepsThePixelUnderTheCursorUnderTheCursor()
    {
        var view = ImageViewTransform.Fit(512, 512, 800, 600);
        var cursor = new Point(310, 240);
        var before = ImageViewTransform.ToImage(view, cursor);

        var zoomed = ImageViewTransform.ZoomAt(view, cursor, 1.15);
        var after = ImageViewTransform.ToImage(zoomed, cursor);

        Assert.AreEqual(before.X, after.X, 1e-6, "the anchored pixel does not slide out from under the pointer");
        Assert.AreEqual(before.Y, after.Y, 1e-6);
        Assert.IsTrue(zoomed.M11 > view.M11, "and it did actually zoom in");
    }

    [TestMethod]
    public void ZoomAt_InAndBackOut_ReturnsToWhereItStarted()
    {
        var view = ImageViewTransform.Fit(512, 512, 800, 600);
        var cursor = new Point(120, 90);

        var round = ImageViewTransform.ZoomAt(ImageViewTransform.ZoomAt(view, cursor, 1.15), cursor, 1 / 1.15);

        Assert.AreEqual(view.M11, round.M11, 1e-9);
        Assert.AreEqual(view.OffsetX, round.OffsetX, 1e-6, "no drift accumulates across a wheel back and forth");
        Assert.AreEqual(view.OffsetY, round.OffsetY, 1e-6);
    }

    [TestMethod]
    public void ToImage_MapsTheStageCentreToTheImageCentre_UnderFit()
    {
        var m = ImageViewTransform.Fit(512, 256, 800, 600);

        var p = ImageViewTransform.ToImage(m, new Point(400, 300));

        Assert.AreEqual(256, p.X, 1e-6);
        Assert.AreEqual(128, p.Y, 1e-6);
    }
}

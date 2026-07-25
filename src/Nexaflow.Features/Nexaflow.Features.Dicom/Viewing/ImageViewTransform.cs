using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Features.Dicom.Viewing;

/// <summary>
/// The image→screen transform for the DICOM stage: fit, actual size, and a cursor-anchored zoom.
/// <para>
/// This is pulled out of the view because the stage cannot lean on WPF's own <c>Stretch</c> the way an
/// ordinary image viewer does. The measurement overlay is drawn in <i>screen</i> space so its strokes and
/// labels stay one width at any zoom, which means the view has to own the matrix and project every
/// annotation point through it by hand — and a matrix the code-behind computes is a matrix nothing can
/// assert. The rules worth pinning are all here: fit letterboxes and centres, actual size lets an image
/// larger than the stage overflow rather than be clamped to it, and a zoom keeps the pixel under the
/// cursor under the cursor.
/// </para>
/// </summary>
public static class ImageViewTransform
{
    /// <summary>
    /// Scales the image to sit wholly inside the viewport and centres it. An unmeasured stage (either
    /// extent still zero during first layout) yields the identity, so the caller can simply try again once
    /// layout has run.
    /// </summary>
    public static Matrix Fit(double imageW, double imageH, double viewW, double viewH)
    {
        if (imageW <= 0 || imageH <= 0 || viewW <= 0 || viewH <= 0) return Matrix.Identity;

        var scale = Math.Min(viewW / imageW, viewH / imageH);
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale)) scale = 1;
        return Centred(scale, imageW, imageH, viewW, viewH);
    }

    /// <summary>
    /// Puts the image back to source pixels, centred. For an image larger than the stage the offsets go
    /// negative on purpose — the frame is hosted on a Canvas precisely so oversized content keeps its full
    /// natural size and is clipped, rather than being squeezed into the viewport.
    /// </summary>
    public static Matrix ActualSize(double imageW, double imageH, double viewW, double viewH)
    {
        if (imageW <= 0 || imageH <= 0) return Matrix.Identity;
        return Centred(1, imageW, imageH, viewW, viewH);
    }

    /// <summary>
    /// Zooms about a point in screen space. The point is pulled to the origin, scaled and pushed back, so
    /// whatever pixel was under the cursor is still under it afterwards.
    /// </summary>
    public static Matrix ZoomAt(Matrix view, Point anchor, double factor)
    {
        if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor)) return view;
        view.Translate(-anchor.X, -anchor.Y);
        view.Scale(factor, factor);
        view.Translate(anchor.X, anchor.Y);
        return view;
    }

    /// <summary>Screen point back to image (source-pixel) space; the identity for a degenerate transform.</summary>
    public static Point ToImage(Matrix view, Point screen)
    {
        if (!view.HasInverse) return screen;
        var inv = view;
        inv.Invert();
        return inv.Transform(screen);
    }

    private static Matrix Centred(double scale, double imageW, double imageH, double viewW, double viewH)
        => new(scale, 0, 0, scale, (viewW - imageW * scale) / 2, (viewH - imageH * scale) / 2);
}

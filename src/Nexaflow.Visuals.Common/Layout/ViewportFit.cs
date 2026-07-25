using System.Windows;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Layout;

/// <summary>
/// Fitting fixed-size content into a viewport, and zooming about a point.
/// <para>
/// Two viewers own their own transform rather than letting WPF's <c>Stretch</c> do it, for unrelated
/// reasons: the DICOM stage draws its measurement overlay in <i>screen</i> space so strokes stay one width
/// at any zoom, and the SVG canvas re-tessellates rather than scaling a bitmap. Both then need the same
/// three rules — fit letterboxes and centres, a zoom keeps the point under the cursor still, and a viewport
/// layout has not measured yet must not produce an infinite scale.
/// </para>
/// <para>
/// Both shapes are here because the two callers hold their transform differently: DICOM as one
/// <see cref="Matrix"/> it also has to invert (screen → image, for annotation points), SVG as a
/// <c>ScaleTransform</c> beside a <c>TranslateTransform</c>. The arithmetic is the same either way. For a
/// pan/zoom <i>canvas</i> with an overview minimap, see <see cref="PanZoomMiniMap"/>.
/// </para>
/// </summary>
public static class ViewportFit
{
    /// <summary>
    /// Scales the content to sit wholly inside the viewport and centres it. An unmeasured viewport (either
    /// extent still zero during first layout) yields the identity, so the caller can simply try again once
    /// layout has run.
    /// </summary>
    public static Matrix Fit(double contentW, double contentH, double viewW, double viewH)
    {
        if (contentW <= 0 || contentH <= 0 || viewW <= 0 || viewH <= 0) return Matrix.Identity;

        var scale = FitScale(contentW, contentH, viewW, viewH);
        return Centred(scale, contentW, contentH, viewW, viewH);
    }

    /// <summary>
    /// The same fit as <see cref="Fit"/>, for a caller holding a separate scale and translation, and
    /// clamped to a zoom range. A zero scale is returned for an unmeasured viewport — the caller should
    /// leave its transform alone and retry.
    /// </summary>
    public static (double Scale, double X, double Y) FitScaled(
        double contentW, double contentH, double viewW, double viewH,
        double minScale = 0.0001, double maxScale = double.MaxValue)
    {
        if (contentW <= 0 || contentH <= 0 || viewW <= 0 || viewH <= 0) return (0, 0, 0);

        var scale = Math.Clamp(FitScale(contentW, contentH, viewW, viewH), minScale, maxScale);
        return (scale, (viewW - contentW * scale) / 2, (viewH - contentH * scale) / 2);
    }

    /// <summary>
    /// Puts the content back to its natural size, centred. For content larger than the viewport the offsets
    /// go negative on purpose — a host that keeps natural size and clips is showing a true 1:1, whereas
    /// clamping here would show a scaled crop while claiming to be 1:1.
    /// </summary>
    public static Matrix ActualSize(double contentW, double contentH, double viewW, double viewH)
    {
        if (contentW <= 0 || contentH <= 0) return Matrix.Identity;
        return Centred(1, contentW, contentH, viewW, viewH);
    }

    /// <summary>
    /// Zooms about a point in screen space. The point is pulled to the origin, scaled and pushed back, so
    /// whatever was under the cursor is still under it afterwards.
    /// </summary>
    public static Matrix ZoomAt(Matrix view, Point anchor, double factor)
    {
        if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor)) return view;
        view.Translate(-anchor.X, -anchor.Y);
        view.Scale(factor, factor);
        view.Translate(anchor.X, anchor.Y);
        return view;
    }

    /// <summary>Screen point back to content coordinates; the identity for a degenerate transform.</summary>
    public static Point ToContent(Matrix view, Point screen)
    {
        if (!view.HasInverse) return screen;
        var inv = view;
        inv.Invert();
        return inv.Transform(screen);
    }

    private static double FitScale(double contentW, double contentH, double viewW, double viewH)
    {
        var scale = Math.Min(viewW / contentW, viewH / contentH);
        return scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale) ? 1 : scale;
    }

    private static Matrix Centred(double scale, double contentW, double contentH, double viewW, double viewH)
        => new(scale, 0, 0, scale, (viewW - contentW * scale) / 2, (viewH - contentH * scale) / 2);
}

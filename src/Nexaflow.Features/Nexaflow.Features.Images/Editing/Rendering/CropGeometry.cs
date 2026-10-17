using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Rendering;

/// <summary>What a render is a picture of.</summary>
public enum RenderFrame
{
    /// <summary>The finished picture: cropped, turned, flipped and resized.</summary>
    Output,

    /// <summary>
    /// The whole straightened picture, turned and flipped but uncropped and unresized — what the crop frame is
    /// dragged over. Its corners outside the tilted picture are transparent.
    /// </summary>
    CropEditing,
}

/// <summary>
/// The geometry of a recipe, in one place, for the renderer and the editor alike: every frame is reached from the
/// upright source through one matrix, so what the crop handles are drawn over is exactly what is saved.
/// </summary>
/// <remarks>
/// <para>The spaces, in order:</para>
/// <list type="number">
/// <item><b>source</b> — upright full-resolution pixels (the EXIF orientation already applied);</item>
/// <item><b>straightened</b> — source rotated by the straighten angle about its centre, where the crop rectangle lives;</item>
/// <item><b>display</b> — the crop (or, while cropping, the whole straightened picture) turned and flipped;</item>
/// <item><b>canvas</b> — display scaled to the pixels actually rendered: the output size, times a preview scale.</item>
/// </list>
/// All of it is plain arithmetic on <see cref="SKMatrix"/>, so it is testable without rendering.
/// </remarks>
public static class CropGeometry
{
    /// <summary>The smallest crop edge, in source pixels.</summary>
    public const double MinCropEdge = 8;

    private const double Epsilon = 1e-3;

    // ── Straightening ───────────────────────────────────────────────────────

    /// <summary>Source → straightened: a clockwise turn by <paramref name="degrees"/> about the picture's centre.</summary>
    public static SKMatrix Straighten(SKSize source, double degrees) =>
        SKMatrix.CreateRotationDegrees((float)degrees, source.Width / 2, source.Height / 2);

    /// <summary>The bounding box of the straightened picture, in the straightened frame.</summary>
    public static SKRect StraightenedBounds(SKSize source, double degrees) =>
        Straighten(source, degrees).MapRect(SKRect.Create(source));

    /// <summary>True when every corner of <paramref name="crop"/> lies on the straightened picture.</summary>
    public static bool Contains(SKSize source, double degrees, CropRect crop)
    {
        foreach (var (x, y) in Corners(crop))
        {
            var (sx, sy) = Unstraighten(source, degrees, x, y);
            if (sx < -Epsilon || sy < -Epsilon || sx > source.Width + Epsilon || sy > source.Height + Epsilon) return false;
        }
        return true;
    }

    /// <summary>
    /// The largest rectangle of <paramref name="aspect"/> (width ÷ height) centred on the picture that fits inside it
    /// at <paramref name="degrees"/> — what a crop shrinks to as the picture is straightened, so no corner shows.
    /// </summary>
    public static CropRect Inscribed(SKSize source, double degrees, double aspect)
    {
        double rad = degrees * Math.PI / 180, cos = Math.Abs(Math.Cos(rad)), sin = Math.Abs(Math.Sin(rad));
        // A centred w×h rectangle turned back by −θ reaches w/2·cos + h/2·sin across and w/2·sin + h/2·cos down;
        // with w = aspect·h each bound gives a largest h, and the smaller one fits both.
        double halfH = Math.Min(source.Width / 2 / (aspect * cos + sin), source.Height / 2 / (aspect * sin + cos));
        double halfW = aspect * halfH;
        double cx = source.Width / 2, cy = source.Height / 2;
        return new CropRect(cx - halfW, cy - halfH, 2 * halfW, 2 * halfH);
    }

    /// <summary>What is kept when the recipe names no crop: everything, inset to the straightened picture.</summary>
    public static CropRect DefaultCrop(SKSize source, double degrees) => degrees == 0
        ? new CropRect(0, 0, source.Width, source.Height)
        : Inscribed(source, degrees, source.Width / source.Height);

    /// <summary>The crop the recipe renders with.</summary>
    public static CropRect EffectiveCrop(RecipeGeometry geometry, SKSize source) =>
        geometry.Crop ?? DefaultCrop(source, geometry.StraightenDegrees);

    /// <summary>
    /// <paramref name="crop"/> shrunk about its centre, keeping its shape, until it lies on the picture at
    /// <paramref name="degrees"/>; a centre off the picture is first brought to the picture's centre.
    /// </summary>
    public static CropRect FitInside(SKSize source, double degrees, CropRect crop)
    {
        double cx = crop.CentreX, cy = crop.CentreY;
        var (qx, qy) = Unstraighten(source, degrees, cx, cy);
        if (qx < 0 || qy < 0 || qx > source.Width || qy > source.Height)
        {
            cx = source.Width / 2;
            cy = source.Height / 2;
            (qx, qy) = (cx, cy);
        }

        // Each corner is centre + k·offset; turned back to the source, every coordinate is linear in k, so the
        // largest k keeping it within [0, size] is read straight off each bound.
        double rad = -degrees * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        double k = 1;
        foreach (var (ox, oy) in new[] { (-1.0, -1.0), (1.0, -1.0), (1.0, 1.0), (-1.0, 1.0) })
        {
            double dx = ox * crop.Width / 2, dy = oy * crop.Height / 2;
            double ex = dx * cos - dy * sin, ey = dx * sin + dy * cos;
            k = Math.Min(k, Limit(qx, ex, source.Width));
            k = Math.Min(k, Limit(qy, ey, source.Height));
        }

        double w = crop.Width * k, h = crop.Height * k;
        return new CropRect(cx - w / 2, cy - h / 2, w, h);

        static double Limit(double at, double step, double size) =>
            step > Epsilon ? (size - at) / step : step < -Epsilon ? -at / step : double.PositiveInfinity;
    }

    /// <summary>
    /// The furthest point from <paramref name="valid"/> towards <paramref name="desired"/> that still lies on the
    /// picture and is at least <see cref="MinCropEdge"/> across — how a dragged handle or a moved frame stops at the
    /// picture's edge rather than past it.
    /// </summary>
    public static CropRect ClampTowards(SKSize source, double degrees, CropRect valid, CropRect desired)
    {
        if (Acceptable(desired)) return desired;

        // Containment is convex in the edges, so the valid share of the path is one interval from its start.
        double lo = 0, hi = 1;
        for (int i = 0; i < 24; i++)
        {
            double mid = (lo + hi) / 2;
            if (Acceptable(Lerp(valid, desired, mid))) lo = mid; else hi = mid;
        }
        return Lerp(valid, desired, lo);

        bool Acceptable(CropRect r) => r.Width >= MinCropEdge && r.Height >= MinCropEdge && Contains(source, degrees, r);
    }

    // ── Turns, flips and aspect ─────────────────────────────────────────────

    public static SKSize Turned(SKSize size, int quarterTurns) =>
        (quarterTurns & 1) == 1 ? new SKSize(size.Height, size.Width) : size;

    /// <summary>
    /// A <paramref name="size"/>d frame → display: <see cref="RecipeGeometry.QuarterTurns"/> clockwise, then the flips.
    /// </summary>
    public static SKMatrix Display(RecipeGeometry geometry, SKSize size)
    {
        float w = size.Width, h = size.Height;
        var turn = geometry.QuarterTurns switch
        {
            1 => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            2 => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            3 => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            _ => SKMatrix.Identity,
        };
        var shown = Turned(size, geometry.QuarterTurns);
        if (geometry.FlipHorizontal) turn = turn.PostConcat(new SKMatrix(-1, 0, shown.Width, 0, 1, 0, 0, 0, 1));
        if (geometry.FlipVertical)   turn = turn.PostConcat(new SKMatrix(1, 0, 0, 0, -1, shown.Height, 0, 0, 1));
        return turn;
    }

    /// <summary>
    /// The width ÷ height a preset asks of the crop <i>as displayed</i>; null for a free crop.
    /// </summary>
    public static double? DisplayAspect(RecipeGeometry geometry, SKSize source)
    {
        double? landscape = geometry.Aspect switch
        {
            AspectPreset.Square      => 1,
            AspectPreset.FourThree   => 4.0 / 3,
            AspectPreset.SixteenNine => 16.0 / 9,
            AspectPreset.Original    => Math.Max(source.Width, source.Height) / Math.Min(source.Width, source.Height),
            _                        => null,
        };
        return landscape is { } a && geometry.AspectPortrait ? 1 / a : landscape;
    }

    /// <summary>The same aspect read in the straightened frame, where the crop is stored: a quarter turn inverts it.</summary>
    public static double? FrameAspect(RecipeGeometry geometry, SKSize source) =>
        DisplayAspect(geometry, source) is { } a ? ((geometry.QuarterTurns & 1) == 1 ? 1 / a : a) : null;

    /// <summary>
    /// The largest rectangle of the preset's aspect centred on the current crop that fits on the picture — what
    /// choosing a preset sets the crop to.
    /// </summary>
    public static CropRect ApplyAspect(RecipeGeometry geometry, SKSize source)
    {
        var crop = EffectiveCrop(geometry, source);
        if (FrameAspect(geometry, source) is not { } aspect) return crop;

        double w = crop.Width, h = crop.Height;
        if (w / h > aspect) w = h * aspect; else h = w / aspect;
        var shaped = new CropRect(crop.CentreX - w / 2, crop.CentreY - h / 2, w, h);
        return FitInside(source, geometry.StraightenDegrees, shaped);
    }

    // ── Output ──────────────────────────────────────────────────────────────

    /// <summary>The finished picture's size in pixels: the crop, turned, then resized.</summary>
    public static SKSizeI OutputSize(ImageRecipe recipe, SKSize source)
    {
        var crop  = EffectiveCrop(recipe.Geometry, source);
        double restored = recipe.Restore?.Scale ?? 1;   // a restored picture is the same picture, larger
        var shown = Turned(new SKSize((float)(crop.Width * restored), (float)(crop.Height * restored)), recipe.Geometry.QuarterTurns);
        if (recipe.Resize is not { } resize) return Round(shown.Width, shown.Height);

        if (resize.Mode == ResizeMode.Percent)
            return Round(shown.Width * resize.Percent / 100, shown.Height * resize.Percent / 100);
        if (resize.LockAspect)
            return Round(resize.Width, resize.Width * shown.Height / shown.Width);
        return Round(resize.Width, resize.Height);
    }

    /// <summary>
    /// Straightened frame → canvas for <paramref name="frame"/>, at <paramref name="scale"/> × the frame's natural
    /// size, with the canvas size it fills. The editor maps its handles through this; the renderer prepends
    /// <see cref="Straighten"/>.
    /// </summary>
    public static SKMatrix FrameToCanvas(ImageRecipe recipe, SKSize source, RenderFrame frame, double scale, out SKSizeI canvas)
    {
        var geometry = recipe.Geometry;
        SKRect region;
        SKSizeI natural;
        if (frame == RenderFrame.CropEditing)
        {
            region = StraightenedBounds(source, geometry.StraightenDegrees);
            var shown = Turned(region.Size, geometry.QuarterTurns);
            natural = Round(shown.Width, shown.Height);
        }
        else
        {
            var crop = EffectiveCrop(geometry, source);
            region  = SKRect.Create((float)crop.X, (float)crop.Y, (float)crop.Width, (float)crop.Height);
            natural = OutputSize(recipe, source);
        }

        canvas = Round(natural.Width * scale, natural.Height * scale);
        var displayed = Turned(region.Size, geometry.QuarterTurns);

        return SKMatrix.CreateTranslation(-region.Left, -region.Top)
            .PostConcat(Display(geometry, region.Size))
            .PostConcat(SKMatrix.CreateScale(canvas.Width / displayed.Width, canvas.Height / displayed.Height));
    }

    /// <summary>Source → canvas: the whole of a render's geometry.</summary>
    public static SKMatrix SourceToCanvas(ImageRecipe recipe, SKSize source, RenderFrame frame, double scale, out SKSizeI canvas) =>
        Straighten(source, recipe.Geometry.StraightenDegrees)
            .PostConcat(FrameToCanvas(recipe, source, frame, scale, out canvas));

    /// <summary>Source pixels per canvas pixel across the render — how sharp the decoded source needs to be.</summary>
    public static double SourcePixelsPerCanvasPixel(SKMatrix sourceToCanvas) =>
        1 / Math.Sqrt(Math.Abs(sourceToCanvas.ScaleX * sourceToCanvas.ScaleY - sourceToCanvas.SkewX * sourceToCanvas.SkewY));

    // ── Level tool ──────────────────────────────────────────────────────────

    /// <summary>
    /// The straighten angle that makes the line <paramref name="a"/>→<paramref name="b"/>, drawn on the displayed
    /// picture, level — or plumb, when it is nearer vertical — clamped to the straighten range.
    /// </summary>
    public static double LevelAngle(RecipeGeometry geometry, SKPoint a, SKPoint b)
    {
        double drawn = Math.Atan2(b.Y - a.Y, b.X - a.X) * 180 / Math.PI;
        double offLevel = drawn - 90 * Math.Round(drawn / 90);

        // Turning the display back by the drawn tilt is the same turn of the source, except through a mirror,
        // which runs the other way.
        bool mirrored = geometry.FlipHorizontal ^ geometry.FlipVertical;
        double next = geometry.StraightenDegrees + (mirrored ? offLevel : -offLevel);
        return Math.Clamp(next, -RecipeGeometry.MaxStraightenDegrees, RecipeGeometry.MaxStraightenDegrees);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static (double X, double Y) Unstraighten(SKSize source, double degrees, double x, double y)
    {
        double rad = -degrees * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        double cx = source.Width / 2, cy = source.Height / 2, dx = x - cx, dy = y - cy;
        return (cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
    }

    private static IEnumerable<(double X, double Y)> Corners(CropRect r)
    {
        yield return (r.X, r.Y);
        yield return (r.Right, r.Y);
        yield return (r.Right, r.Bottom);
        yield return (r.X, r.Bottom);
    }

    private static CropRect Lerp(CropRect a, CropRect b, double t) => CropRect.FromEdges(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t,
        a.Right + (b.Right - a.Right) * t,
        a.Bottom + (b.Bottom - a.Bottom) * t);

    private static SKSizeI Round(double w, double h) =>
        new(Math.Max(1, (int)Math.Round(w)), Math.Max(1, (int)Math.Round(h)));
}

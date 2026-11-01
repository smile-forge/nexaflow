using System.Runtime.InteropServices;
using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Selection;

/// <summary>
/// A region drawn by hand made coverage: each shape rasterized anti-aliased by Skia and met with what is selected as its
/// mode says, each refinement applied to everything selected so far — in the order they were made, at any scale, so the
/// same region gives the selection shown over a preview and the mask a save is cut by.
/// </summary>
public static class RegionRaster
{
    /// <summary>
    /// <paramref name="region"/>'s steps applied in turn to <paramref name="start"/>, its lengths times
    /// <paramref name="scale"/> — canvas pixels per pixel the region is written in.
    /// </summary>
    public static TiledCoverage Apply(TiledCoverage start, RecipeRegion? region, double scale)
    {
        if (region is null) return start;
        var coverage = start;
        foreach (var step in region.Steps)
            coverage = step switch
            {
                RegionShape shape => coverage.Combined(Shape(shape, coverage.Width, coverage.Height, scale), shape.Mode),
                RegionInvert => coverage.Inverted(),
                RegionFeather feather => coverage.Feathered(feather.Radius * scale),
                RegionGrow grow => coverage.Grown(grow.By * scale),
                RegionSmooth smooth => coverage.Smoothed(smooth.Radius * scale),
                _ => throw new NotSupportedException($"No way to apply a {step.GetType().Name}."),
            };
        return coverage;
    }

    /// <summary>The coverage of <paramref name="shape"/> alone on a <paramref name="width"/> × <paramref name="height"/>
    /// canvas, its lengths times <paramref name="scale"/>: only the tiles it touches are held.</summary>
    public static TiledCoverage Shape(RegionShape shape, int width, int height, double scale)
    {
        using var path = PathOf(shape, scale);
        var clear = TiledCoverage.Clear(width, height);
        var outer = path.Bounds;
        if (outer.IsEmpty) return clear;
        var rect = SKRectI.Intersect(new SKRectI((int)Math.Floor(outer.Left) - 1, (int)Math.Floor(outer.Top) - 1,
                                                 (int)Math.Ceiling(outer.Right) + 1, (int)Math.Ceiling(outer.Bottom) + 1),
                                     new SKRectI(0, 0, width, height));
        if (rect.IsEmpty) return clear;

        using var bitmap = new SKBitmap(new SKImageInfo(rect.Width, rect.Height, SKColorType.Alpha8, SKAlphaType.Premul));
        if (bitmap.GetPixels() == IntPtr.Zero) throw new InvalidOperationException($"No memory for a {rect.Width}×{rect.Height} selection.");
        bitmap.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(bitmap))
        using (var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, Color = SKColors.White })
        {
            canvas.Translate(-rect.Left, -rect.Top);
            canvas.DrawPath(path, paint);
        }

        var pixels = new byte[rect.Width * rect.Height];
        var origin = bitmap.GetPixels();
        for (int y = 0; y < rect.Height; y++)
            Marshal.Copy(origin + y * bitmap.RowBytes, pixels, y * rect.Width, rect.Width);
        return clear.Written(rect, pixels);
    }

    /// <summary>The outline of <paramref name="shape"/>, its lengths times <paramref name="scale"/>; empty for a polygon of
    /// fewer than three points, which encloses nothing.</summary>
    public static SKPath PathOf(RegionShape shape, double scale)
    {
        using var builder = new SKPathBuilder { FillType = SKPathFillType.Winding };
        float s = (float)scale;
        switch (shape)
        {
            case RegionRectangle r:
                builder.AddRect(Box(r.Left, r.Top, r.Right, r.Bottom, s), SKPathDirection.Clockwise);
                break;
            case RegionEllipse e:
                builder.AddOval(Box(e.Left, e.Top, e.Right, e.Bottom, s), SKPathDirection.Clockwise);
                break;
            case RegionPolygon { Points.Length: >= 3 } p:
                builder.AddPoly([.. p.Points.Select(point => new SKPoint((float)point.X * s, (float)point.Y * s))], close: true);
                break;
            case RegionPolygon:
                break;
            default:
                throw new NotSupportedException($"No outline for a {shape.GetType().Name}.");
        }
        return builder.Detach();
    }

    private static SKRect Box(double left, double top, double right, double bottom, float scale)
        => new SKRect((float)Math.Min(left, right) * scale, (float)Math.Min(top, bottom) * scale,
                      (float)Math.Max(left, right) * scale, (float)Math.Max(top, bottom) * scale);
}

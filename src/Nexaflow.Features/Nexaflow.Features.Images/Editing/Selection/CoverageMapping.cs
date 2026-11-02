using System.Runtime.InteropServices;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Selection;

/// <summary>Coverage carried from one set of coordinates to another — a canvas's selection onto a picture layer's source, a
/// pick on a picture layer onto its canvas — drawn through the matrix between them, smoothly resampled.</summary>
public static class CoverageMapping
{
    /// <summary><paramref name="coverage"/> drawn through <paramref name="toTarget"/> onto a <paramref name="width"/> ×
    /// <paramref name="height"/> coverage; whatever falls outside the coverage's own area is uncovered.</summary>
    public static TiledCoverage Map(TiledCoverage coverage, SKMatrix toTarget, int width, int height)
    {
        if (coverage.IsEmpty) return TiledCoverage.Clear(width, height);
        using var source = SKImage.FromPixelCopy(new SKImageInfo(coverage.Width, coverage.Height, SKColorType.Alpha8, SKAlphaType.Premul), coverage.ToBytes())
                           ?? throw new InvalidOperationException("The coverage could not be made an image.");
        var info = new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul);
        using var target = new SKBitmap(info);
        if (target.GetPixels() == IntPtr.Zero) throw new InvalidOperationException($"No memory for a {width}×{height} selection.");
        target.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(target))
        using (var paint = new SKPaint { IsAntialias = true })
        {
            canvas.SetMatrix(toTarget);
            canvas.DrawImage(source, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }
        var bytes = new byte[width * height];
        var origin = target.GetPixels();
        for (int y = 0; y < height; y++)
            Marshal.Copy(origin + y * target.RowBytes, bytes, y * width, width);
        return TiledCoverage.FromBytes(bytes, width, height);
    }
}

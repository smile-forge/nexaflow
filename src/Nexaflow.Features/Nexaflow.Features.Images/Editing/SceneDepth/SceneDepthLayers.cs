using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Selection;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.SceneDepth;

/// <summary>
/// A photo's scene depth as material for the user's own compositing: its nearness as greys — white the nearest, black the
/// farthest — laid where the photo lies on a layered picture's canvas, as a layer of its own or as a layer's mask.
/// </summary>
public static class SceneDepthLayers
{
    /// <summary>The nearness of a <paramref name="photo"/>-sized photo as greys over it, a byte a pixel.</summary>
    public static byte[] Greys(SceneDepthPlane plane, SKSizeI photo)
    {
        var nearness = plane.AtSize(photo);
        var greys = new byte[nearness.Length];
        for (int i = 0; i < greys.Length; i++) greys[i] = (byte)Math.Round(Math.Clamp(nearness[i], 0f, 1f) * 255);
        return greys;
    }

    /// <summary>The photo's depth as a mask over the canvas: shown where it is near, hidden where it is far, nothing where the photo
    /// does not reach — <paramref name="photoToCanvas"/> carrying it there.</summary>
    public static TiledCoverage Mask(SceneDepthPlane plane, SKSizeI photo, SKMatrix photoToCanvas, int width, int height)
        => CoverageMapping.Map(TiledCoverage.FromBytes(Greys(plane, photo), photo.Width, photo.Height), photoToCanvas, width, height);

    /// <summary>
    /// The photo's depth as the pixels of a layer, in <paramref name="colour"/>: opaque greys where the photo lies on the canvas —
    /// <paramref name="photoToCanvas"/> carrying it there — transparent elsewhere.
    /// </summary>
    public static TiledPixels Pixels(SceneDepthPlane plane, SKSizeI photo, SKMatrix photoToCanvas, int width, int height, PictureColour colour)
    {
        var greys = Greys(plane, photo);
        var rgba = new byte[greys.Length * 4];
        for (int i = 0; i < greys.Length; i++)
        {
            byte g = greys[i];
            (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]) = (g, g, g, 255);
        }
        using var depth = SKImage.FromPixelCopy(new SKImageInfo(photo.Width, photo.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()), rgba)
                          ?? throw new InvalidOperationException("No memory for the picture's depth.");
        var area = SKRectI.Intersect(SKRectI.Ceiling(photoToCanvas.MapRect(SKRect.Create(photo.Width, photo.Height))), new SKRectI(0, 0, width, height));
        if (area.IsEmpty) return TiledPixels.EmptyIn(colour.Tiles);
        using var bitmap = new SKBitmap(colour.SurfaceInfo(area.Width, area.Height));
        if (bitmap.GetPixels() == IntPtr.Zero) throw new InvalidOperationException($"No memory for a {area.Width}×{area.Height} layer.");
        bitmap.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Translate(-area.Left, -area.Top);
            canvas.Concat(photoToCanvas);
            using var paint = new SKPaint { IsAntialias = true };
            canvas.DrawImage(depth, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear), paint);
        }
        return TiledPixels.FromBitmap(bitmap, area.Left, area.Top, colour.Tiles);
    }
}

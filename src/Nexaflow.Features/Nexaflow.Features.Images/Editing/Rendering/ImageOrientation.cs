using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Rendering;

/// <summary>
/// The EXIF orientation as geometry: the matrix taking stored pixels to the upright picture. It is folded into the
/// render's matrix, so an orientation costs no pixel copy, and a baked save writes the picture upright with the tag
/// reset.
/// </summary>
public static class ImageOrientation
{
    /// <summary>True when the stored pixels are on their side, so the upright picture swaps width and height.</summary>
    public static bool SwapsAxes(SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    public static SKSizeI UprightSize(SKEncodedOrigin origin, SKSizeI stored) =>
        SwapsAxes(origin) ? new SKSizeI(stored.Height, stored.Width) : stored;

    /// <summary>Maps stored pixel coordinates (<paramref name="stored"/> in size) to upright coordinates.</summary>
    public static SKMatrix ToUpright(SKEncodedOrigin origin, SKSize stored)
    {
        float w = stored.Width, h = stored.Height;
        return origin switch
        {
            // x' = scaleX·x + skewX·y + transX,  y' = skewY·x + scaleY·y + transY
            SKEncodedOrigin.TopRight    => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),    // mirrored
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),   // half turn
            SKEncodedOrigin.BottomLeft  => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),    // flipped
            SKEncodedOrigin.LeftTop     => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),     // transposed
            SKEncodedOrigin.RightTop    => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),    // needs a clockwise quarter
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),   // transversed
            SKEncodedOrigin.LeftBottom  => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),    // needs a counter-clockwise quarter
            _                           => SKMatrix.Identity,
        };
    }
}

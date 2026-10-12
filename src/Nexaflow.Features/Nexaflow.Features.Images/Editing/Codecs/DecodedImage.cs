using Nexaflow.Features.Images.Editing.Rendering;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>
/// A picture as decoded: its stored pixels — possibly reduced, never rotated — and what it takes to read them as the
/// upright full-resolution source a recipe is written against.
/// </summary>
public sealed class DecodedImage : IDisposable
{
    public DecodedImage(SKImage pixels, SKEncodedOrigin origin, SKSizeI storedFullSize, ImageContainer container,
                        bool isAnimated, bool isHighBitDepth)
    {
        Pixels         = pixels;
        Origin         = origin;
        StoredFullSize = storedFullSize;
        Container      = container;
        IsAnimated     = isAnimated;
        IsHighBitDepth = isHighBitDepth;
    }

    /// <summary>The decoded pixels, as stored (the orientation is applied by the render, not here).</summary>
    public SKImage Pixels { get; }

    /// <summary>The EXIF orientation the pixels were stored with.</summary>
    public SKEncodedOrigin Origin { get; }

    /// <summary>The stored pixel size at full resolution.</summary>
    public SKSizeI StoredFullSize { get; }

    public ImageContainer Container { get; }

    /// <summary>More than one frame; only the first is shown and edited.</summary>
    public bool IsAnimated { get; }

    /// <summary>More than 8 bits a channel — rendered and saved at half-float precision so nothing is lost.</summary>
    public bool IsHighBitDepth { get; }

    /// <summary>The upright full-resolution size: the space every recipe coordinate is in.</summary>
    public SKSize SourceSize => ImageOrientation.UprightSize(Origin, StoredFullSize);

    /// <summary>True when the pixels are the full stored resolution.</summary>
    public bool IsFullResolution => Pixels.Width == StoredFullSize.Width && Pixels.Height == StoredFullSize.Height;

    /// <summary>Full-resolution source pixels per decoded pixel (1 at full resolution).</summary>
    public double Reduction => (double)StoredFullSize.Width / Pixels.Width;

    /// <summary>The colour space the pixels are in (sRGB when the file names none).</summary>
    public SKColorSpace ColorSpace => Pixels.ColorSpace ?? SKColorSpace.CreateSrgb();

    /// <summary>Decoded pixel → upright full-resolution source pixel.</summary>
    public SKMatrix PixelsToSource =>
        SKMatrix.CreateScale((float)StoredFullSize.Width / Pixels.Width, (float)StoredFullSize.Height / Pixels.Height)
            .PostConcat(ImageOrientation.ToUpright(Origin, StoredFullSize));

    public void Dispose() => Pixels.Dispose();
}

/// <summary>A picture that could not be decoded.</summary>
public sealed class ImageDecodeException(string message) : Exception(message);

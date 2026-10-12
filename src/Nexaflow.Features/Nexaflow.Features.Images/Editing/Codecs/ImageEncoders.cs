using Nexaflow.Features.Images.Editing.Metadata;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>How a picture is compressed on its way out.</summary>
/// <param name="Quality">0–100, for the lossy formats.</param>
/// <param name="Lossless">WebP only: keep every pixel exactly (as a lossless source was).</param>
public sealed record EncodeOptions(int Quality, bool Lossless = false);

/// <summary>Turns a rendered picture into one or more containers' bytes.</summary>
public interface IImageEncoder
{
    bool Encodes(ImageContainer container);

    /// <summary>True when what it writes in <paramref name="container"/> keeps EXIF, XMP and the like.</summary>
    bool CarriesMetadata(ImageContainer container);

    byte[] Encode(SKImage picture, ImageContainer container, EncodeOptions options, MetadataBlocks metadata);
}

/// <summary>The encoders, asked in order for the first that writes a container.</summary>
public sealed class ImageEncoders(IReadOnlyList<IImageEncoder> encoders)
{
    public bool CanEncode(ImageContainer container) => encoders.Any(e => e.Encodes(container));

    public bool CarriesMetadata(ImageContainer container) =>
        encoders.FirstOrDefault(e => e.Encodes(container))?.CarriesMetadata(container) ?? false;

    public byte[] Encode(SKImage picture, ImageContainer container, EncodeOptions options, MetadataBlocks metadata) =>
        (encoders.FirstOrDefault(e => e.Encodes(container)) ?? throw new NotSupportedException($"No encoder writes {container} images."))
            .Encode(picture, container, options, metadata);

    /// <summary>
    /// The picture's pixels as <paramref name="colorType"/>, unpremultiplied, in its own colour space — what the
    /// encoders that take raw rows want.
    /// </summary>
    internal static SKBitmap Pixels(SKImage picture, SKColorType colorType, SKAlphaType alphaType)
    {
        var bitmap = new SKBitmap(new SKImageInfo(picture.Width, picture.Height, colorType, alphaType, picture.ColorSpace));
        if (!picture.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
        {
            bitmap.Dispose();
            throw new InvalidOperationException("The rendered picture could not be read back.");
        }
        return bitmap;
    }

    /// <summary>True when no pixel is even partly transparent.</summary>
    internal static bool IsOpaque(SKImage picture)
    {
        if (picture.AlphaType == SKAlphaType.Opaque) return true;
        using var alpha = new SKBitmap(new SKImageInfo(picture.Width, picture.Height, SKColorType.Alpha8, SKAlphaType.Premul));
        if (!picture.ReadPixels(alpha.Info, alpha.GetPixels(), alpha.RowBytes, 0, 0)) return false;
        foreach (byte a in alpha.GetPixelSpan()) if (a != 255) return false;
        return true;
    }
}

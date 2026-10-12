using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>Turns one or more containers' bytes into pixels.</summary>
public interface IImageDecoder
{
    bool Decodes(ImageContainer container);

    /// <summary>
    /// Decodes the first frame. With <paramref name="maxLongEdge"/> the result's longer side is at most that (and no
    /// smaller than it needs to be); without it, the full resolution.
    /// </summary>
    DecodedImage Decode(byte[] data, ImageContainer container, int? maxLongEdge);
}

/// <summary>The decoders, asked in order for the first that reads a container.</summary>
public sealed class ImageDecoders(IReadOnlyList<IImageDecoder> decoders)
{
    public bool CanDecode(ImageContainer container) => decoders.Any(d => d.Decodes(container));

    public DecodedImage Decode(byte[] data, int? maxLongEdge)
    {
        var container = ImageContainers.Sniff(data.AsSpan(0, Math.Min(data.Length, 16)));
        var decoder = decoders.FirstOrDefault(d => d.Decodes(container))
            ?? throw new ImageDecodeException($"No decoder reads {container} images.");
        return decoder.Decode(data, container, maxLongEdge);
    }

    /// <summary>
    /// <paramref name="bitmap"/> reduced so its longer side is <paramref name="maxLongEdge"/>, with mipmapped
    /// sampling so detail averages rather than aliases; the bitmap itself when it is already that small.
    /// </summary>
    internal static SKBitmap Reduce(SKBitmap bitmap, int maxLongEdge)
    {
        int longEdge = Math.Max(bitmap.Width, bitmap.Height);
        if (longEdge <= maxLongEdge) return bitmap;

        double scale = (double)maxLongEdge / longEdge;
        var info = bitmap.Info.WithSize(Math.Max(1, (int)Math.Round(bitmap.Width * scale)), Math.Max(1, (int)Math.Round(bitmap.Height * scale)));
        var reduced = new SKBitmap(info);
        bitmap.ScalePixels(reduced, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        bitmap.Dispose();
        return reduced;
    }

    /// <summary>An immutable image over <paramref name="bitmap"/>'s pixels, taking them over.</summary>
    internal static SKImage Seal(SKBitmap bitmap)
    {
        bitmap.SetImmutable();
        var image = SKImage.FromBitmap(bitmap);
        bitmap.Dispose();
        return image;
    }
}

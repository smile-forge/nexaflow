using Nexaflow.Features.Images.Editing.Metadata;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>
/// JPEG, PNG and WebP through Skia's encoders, which embed the picture's colour profile themselves; the rest of the
/// metadata is put back by <see cref="MetadataTransfer"/>. A 16-bit picture is written as 16-bit PNG.
/// </summary>
public sealed class SkiaImageEncoder(MetadataTransfer metadata) : IImageEncoder
{
    public bool Encodes(ImageContainer container) => container is ImageContainer.Jpeg or ImageContainer.Png or ImageContainer.Webp;

    public bool CarriesMetadata(ImageContainer container) => metadata.CanWrite(container);

    /// <summary>PNG and WebP keep an alpha channel; JPEG lays transparency on white.</summary>
    public bool CarriesTransparency(ImageContainer container) => container is ImageContainer.Png or ImageContainer.Webp;

    public byte[] Encode(SKImage picture, ImageContainer container, EncodeOptions options, MetadataBlocks blocks)
    {
        byte[] encoded = container switch
        {
            ImageContainer.Jpeg => EncodeJpeg(picture, options),
            ImageContainer.Png  => EncodePng(picture),
            ImageContainer.Webp => EncodeWebp(picture, options),
            _ => throw new NotSupportedException($"Skia does not write {container} here."),
        };
        return metadata.Write(encoded, container, blocks);
    }

    private static byte[] EncodeJpeg(SKImage picture, EncodeOptions options)
    {
        // JPEG has no alpha and 8 bits a channel: transparency is laid on white, and wide pixels narrowed.
        using var flat = new SKBitmap(new SKImageInfo(picture.Width, picture.Height, SKColorType.Rgba8888, SKAlphaType.Opaque, picture.ColorSpace));
        using (var canvas = new SKCanvas(flat))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawImage(picture, 0, 0, SKSamplingOptions.Default, null);
        }
        using var pixmap = flat.PeekPixels();
        using var data = pixmap.Encode(new SKJpegEncoderOptions(Math.Clamp(options.Quality, 1, 100),
                                                                 SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore));
        return data?.ToArray() ?? throw new InvalidOperationException("The JPEG encoder produced nothing.");
    }

    private static byte[] EncodePng(SKImage picture)
    {
        using var pixmap = picture.PeekPixels() ?? throw new InvalidOperationException("The picture has no pixels to encode.");
        using var data = pixmap.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 6));
        return data?.ToArray() ?? throw new InvalidOperationException("The PNG encoder produced nothing.");
    }

    private static byte[] EncodeWebp(SKImage picture, EncodeOptions options)
    {
        using var narrow = ImageEncoders.Pixels(picture, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var pixmap = narrow.PeekPixels();
        var compression = options.Lossless ? SKWebpEncoderCompression.Lossless : SKWebpEncoderCompression.Lossy;
        using var data = pixmap.Encode(new SKWebpEncoderOptions(compression, Math.Clamp(options.Quality, 1, 100)));
        return data?.ToArray() ?? throw new InvalidOperationException("The WebP encoder produced nothing.");
    }
}

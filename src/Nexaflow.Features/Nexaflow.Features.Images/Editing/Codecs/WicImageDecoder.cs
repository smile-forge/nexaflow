using SkiaSharp;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>
/// TIFF, which Skia has no codec for, through Windows Imaging Component. Pixels are handed to Skia as 8-bit
/// premultiplied BGRA, or 16-bit RGBA widened to half-float when the file holds more than 8 bits a channel.
/// </summary>
/// <remarks>WIC objects belong to the thread that made them; everything here is made and finished on the caller's.</remarks>
public sealed class WicImageDecoder : IImageDecoder
{
    private const string OrientationQuery = "/ifd/{ushort=274}";

    public bool Decodes(ImageContainer container) => container == ImageContainer.Tiff;

    public DecodedImage Decode(byte[] data, ImageContainer container, int? maxLongEdge)
    {
        using var stream = new MemoryStream(data, writable: false);
        BitmapFrame frame;
        try
        {
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                                               BitmapCacheOption.OnLoad);
            frame = decoder.Frames[0];
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException)
        {
            throw new ImageDecodeException($"Not a readable {container} image: {ex.Message}");
        }

        bool highBitDepth = frame.Format.BitsPerPixel / Math.Max(1, frame.Format.Masks.Count) > 8;
        var stored = new SKSizeI(frame.PixelWidth, frame.PixelHeight);
        var colorSpace = ColorSpaceOf(frame);

        var bitmap = highBitDepth ? CopyWide(frame, colorSpace) : CopyNarrow(frame, colorSpace);
        if (maxLongEdge is { } edge) bitmap = ImageDecoders.Reduce(bitmap, edge);

        return new DecodedImage(ImageDecoders.Seal(bitmap), OriginOf(frame), stored, container,
                                isAnimated: false, isHighBitDepth: highBitDepth);
    }

    private static SKBitmap CopyNarrow(BitmapSource frame, SKColorSpace? colorSpace)
    {
        var source = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        var bitmap = new SKBitmap(new SKImageInfo(frame.PixelWidth, frame.PixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul, colorSpace));
        source.CopyPixels(System.Windows.Int32Rect.Empty, bitmap.GetPixels(), bitmap.ByteCount, bitmap.RowBytes);
        return bitmap;
    }

    private static SKBitmap CopyWide(BitmapSource frame, SKColorSpace? colorSpace)
    {
        var source = new FormatConvertedBitmap(frame, PixelFormats.Rgba64, null, 0);
        using var wide = new SKBitmap(new SKImageInfo(frame.PixelWidth, frame.PixelHeight, SKColorType.Rgba16161616, SKAlphaType.Unpremul, colorSpace));
        source.CopyPixels(System.Windows.Int32Rect.Empty, wide.GetPixels(), wide.ByteCount, wide.RowBytes);

        var half = new SKBitmap(new SKImageInfo(frame.PixelWidth, frame.PixelHeight, SKColorType.RgbaF16, SKAlphaType.Premul, colorSpace));
        wide.PeekPixels().ReadPixels(half.Info, half.GetPixels(), half.RowBytes);
        return half;
    }

    private static SKEncodedOrigin OriginOf(BitmapFrame frame)
    {
        try
        {
            return frame.Metadata is BitmapMetadata metadata && metadata.GetQuery(OrientationQuery) is ushort tag && tag is >= 1 and <= 8
                ? (SKEncodedOrigin)tag
                : SKEncodedOrigin.TopLeft;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return SKEncodedOrigin.TopLeft;
        }
    }

    private static SKColorSpace? ColorSpaceOf(BitmapFrame frame)
    {
        if (frame.ColorContexts is not { Count: > 0 } contexts) return null;
        try
        {
            using var profile = contexts[0].OpenProfileStream();
            using var buffer = new MemoryStream();
            profile.CopyTo(buffer);
            return SKColorSpace.CreateIcc(buffer.ToArray());
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}

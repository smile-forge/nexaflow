using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>
/// JPEG, PNG, WebP, GIF, BMP and ICO through Skia's codecs. A reduced JPEG is decoded reduced — the codec scales in
/// the DCT, so a preview of a 50-megapixel photo never decodes 50 megapixels.
/// </summary>
public sealed class SkiaImageDecoder : IImageDecoder
{
    public bool Decodes(ImageContainer container) => container is
        ImageContainer.Jpeg or ImageContainer.Png or ImageContainer.Webp or ImageContainer.Gif or ImageContainer.Bmp or ImageContainer.Ico;

    public DecodedImage Decode(byte[] data, ImageContainer container, int? maxLongEdge)
    {
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData) ?? throw new ImageDecodeException($"Not a readable {container} image.");

        var info = codec.Info;
        bool highBitDepth = info.ColorType is SKColorType.Rgba16161616 or SKColorType.RgbaF16 or SKColorType.RgbaF32 or SKColorType.Rgba1010102;
        var colorType = highBitDepth ? SKColorType.RgbaF16 : SKImageInfo.PlatformColorType;
        var alphaType = info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul;

        var size = info.Size;
        if (maxLongEdge is { } limit && Math.Max(size.Width, size.Height) > limit)
        {
            var scaled = codec.GetScaledDimensions((float)limit / Math.Max(size.Width, size.Height));
            // The codec rounds to the scales it can do natively; one that lands under the limit would be softer than
            // asked, so that case decodes larger and is reduced exactly below.
            if (Math.Max(scaled.Width, scaled.Height) >= limit) size = scaled;
        }

        var bitmap = new SKBitmap(new SKImageInfo(size.Width, size.Height, colorType, alphaType, info.ColorSpace));
        var result = codec.GetPixels(bitmap.Info, bitmap.GetPixels());
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            bitmap.Dispose();
            throw new ImageDecodeException($"The {container} image could not be decoded ({result}).");
        }

        if (maxLongEdge is { } edge) bitmap = ImageDecoders.Reduce(bitmap, edge);

        return new DecodedImage(ImageDecoders.Seal(bitmap), codec.EncodedOrigin, info.Size, container,
                                isAnimated: codec.FrameCount > 1, isHighBitDepth: highBitDepth);
    }
}

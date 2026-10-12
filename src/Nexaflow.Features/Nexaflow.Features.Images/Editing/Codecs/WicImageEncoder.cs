using Nexaflow.Features.Images.Editing.Metadata;
using SkiaSharp;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.Editing.Codecs;

/// <summary>
/// BMP and GIF through Windows Imaging Component, from 8-bit sRGB pixels. Neither format has a place for EXIF or
/// XMP, so nothing but the pixels is written; GIF's palette is chosen by WIC.
/// </summary>
public sealed class WicImageEncoder : IImageEncoder
{
    public bool Encodes(ImageContainer container) => container is ImageContainer.Bmp or ImageContainer.Gif;

    public bool CarriesMetadata(ImageContainer container) => false;

    public byte[] Encode(SKImage picture, ImageContainer container, EncodeOptions options, MetadataBlocks metadata)
    {
        using var srgb = new SKBitmap(new SKImageInfo(picture.Width, picture.Height, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        if (!picture.ReadPixels(srgb.Info, srgb.GetPixels(), srgb.RowBytes, 0, 0))
            throw new InvalidOperationException("The rendered picture could not be read back.");

        var source = BitmapSource.Create(srgb.Width, srgb.Height, 96, 96, PixelFormats.Pbgra32, null,
                                          srgb.GetPixelSpan().ToArray(), srgb.RowBytes);
        BitmapEncoder encoder = container == ImageContainer.Gif ? new GifBitmapEncoder() : new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}

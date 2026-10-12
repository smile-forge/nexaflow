using SkiaSharp;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.Editing.Wpf;

/// <summary>Skia pictures as WPF bitmaps.</summary>
public static class SkiaWpf
{
    /// <summary>
    /// A frozen sRGB copy of <paramref name="picture"/> — safe to make on any thread and hand to the UI thread, for
    /// pictures made once (thumbnails) rather than a stream of frames.
    /// </summary>
    public static BitmapSource ToFrozenBitmap(SKImage picture)
    {
        using var srgb = new SKBitmap(new SKImageInfo(picture.Width, picture.Height, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        if (!picture.ReadPixels(srgb.Info, srgb.GetPixels(), srgb.RowBytes, 0, 0))
            throw new InvalidOperationException("The rendered picture could not be read back.");

        var bitmap = BitmapSource.Create(srgb.Width, srgb.Height, 96, 96, PixelFormats.Pbgra32, null,
                                         srgb.GetPixels(), srgb.ByteCount, srgb.RowBytes);
        bitmap.Freeze();
        return bitmap;
    }
}

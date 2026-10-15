using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Core.Services.Tools;

/// <summary>Pictures read from and written to files as interleaved 8-bit RGBA, with Windows' own codecs.</summary>
internal static class PictureFiles
{
    /// <summary>The extensions <see cref="Read"/> takes.</summary>
    public static readonly IReadOnlyList<string> Readable = [".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff", ".gif", ".heic", ".jxr"];

    /// <summary>The picture at <paramref name="path"/>, upright as its EXIF orientation says, as interleaved RGBA with
    /// straight alpha.</summary>
    /// <exception cref="NotSupportedException">Windows has no codec that reads it.</exception>
    public static (byte[] Rgba, int Width, int Height) Read(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        BitmapSource upright = Upright(frame, Orientation(frame));
        var bgra = new FormatConvertedBitmap(upright, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth, height = bgra.PixelHeight;
        var pixels = new byte[width * height * 4];
        bgra.CopyPixels(pixels, width * 4, 0);
        SwapRedAndBlue(pixels);
        return (pixels, width, height);
    }

    /// <summary>The picture encoded for <paramref name="extension"/>: JPEG at quality 95 for a JPEG, else PNG, which keeps
    /// the alpha.</summary>
    public static byte[] Encode(byte[] rgba, int width, int height, string extension)
    {
        var bgra = (byte[])rgba.Clone();
        SwapRedAndBlue(bgra);
        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        BitmapEncoder encoder = extension.ToLowerInvariant() is ".jpg" or ".jpeg"
            ? new JpegBitmapEncoder { QualityLevel = 95 }
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(extension.ToLowerInvariant() is ".jpg" or ".jpeg" ? new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0) : source));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    /// <summary>The EXIF orientation, 1 to 8; 1 where the file says none.</summary>
    private static int Orientation(BitmapFrame frame)
    {
        try
        {
            return frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("System.Photo.Orientation") is ushort o and >= 1 and <= 8 ? o : 1;
        }
        catch (NotSupportedException)
        {
            return 1;   // a format whose metadata Windows cannot query
        }
    }

    /// <summary>The frame turned and mirrored as EXIF orientation <paramref name="orientation"/> asks.</summary>
    private static BitmapSource Upright(BitmapSource frame, int orientation)
    {
        Transform? turn = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(-1, 1) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new RotateTransform(270), new ScaleTransform(-1, 1) } },
            8 => new RotateTransform(270),
            _ => null,
        };
        return turn is null ? frame : new TransformedBitmap(frame, turn);
    }

    private static void SwapRedAndBlue(byte[] pixels)
    {
        for (int i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
    }
}

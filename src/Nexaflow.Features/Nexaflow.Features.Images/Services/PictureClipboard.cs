using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Images.Services;

/// <summary>A picture put on the clipboard with its transparency.</summary>
internal static class PictureClipboard
{
    /// <summary>Puts <paramref name="png"/> on the clipboard as a PNG — transparency kept, for what reads one — and as a
    /// bitmap, for what reads only that.</summary>
    public static void CopyPng(byte[] png)
    {
        var data = new DataObject();
        data.SetData("PNG", new MemoryStream(png), autoConvert: false);
        data.SetImage(BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad));
        Clipboard.SetDataObject(data, copy: true);
    }

    /// <summary>The picture on the clipboard as PNG — as put there, transparency kept, or encoded from the bitmap another
    /// application put there; null where it holds none.</summary>
    public static byte[]? ReadPng()
    {
        var data = Clipboard.GetDataObject();
        if (data is null) return null;
        if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png) return png.ToArray();
        if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not { } bitmap) return null;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Whether the clipboard holds a picture to paste.</summary>
    public static bool HoldsPicture() => Clipboard.ContainsData("PNG") || Clipboard.ContainsImage();
}

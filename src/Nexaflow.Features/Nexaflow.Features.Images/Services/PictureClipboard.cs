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
}

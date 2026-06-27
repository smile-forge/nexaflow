using System.IO;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Audio.Services;

/// <summary>Decodes embedded cover-art bytes into a frozen, UI-ready <see cref="BitmapSource"/>.</summary>
public static class ImageHelpers
{
    public static BitmapSource? ToBitmap(byte[]? data)
    {
        if (data is not { Length: > 0 }) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(data);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}

using System.IO;
using System.Windows.Media.Imaging;

namespace Nexaflow.Visuals.Common.Previews;

/// <summary>
/// A page's picture of itself for the conversation's context preview, where its live view (a browser, a 3D
/// viewport) cannot be shown a second time. Decodes a PNG the page captured no larger than
/// <see cref="MaxEdge"/>, so the full-size capture is not kept, and freezes it so the preview can bind it from
/// wherever it is built.
/// </summary>
public static class PageSnapshot
{
    /// <summary>The longest edge a snapshot keeps: enough to recognise the page in the narrow context panel.</summary>
    public const int MaxEdge = 480;

    public static BitmapSource Decode(byte[] png)
    {
        int width, height;
        using (var header = new MemoryStream(png))
        {
            var frame = BitmapDecoder.Create(header, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            (width, height) = (frame.PixelWidth, frame.PixelHeight);
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption  = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(png);
        if (width >= height && width > MaxEdge) image.DecodePixelWidth = MaxEdge;
        else if (height > width && height > MaxEdge) image.DecodePixelHeight = MaxEdge;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

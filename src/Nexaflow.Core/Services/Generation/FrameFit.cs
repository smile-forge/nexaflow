using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Providers.Common;

namespace Nexaflow.Core.Services.Generation;

/// <summary>A picture made the exact size a video model starts its clip from.</summary>
internal static class FrameFit
{
    /// <summary>
    /// <paramref name="picture"/> cut to the frame's proportions about its centre, then scaled to
    /// <paramref name="width"/> × <paramref name="height"/>, as a PNG.
    /// </summary>
    public static MediaBytes Fit(MediaBytes picture, int width, int height)
    {
        using var input = new MemoryStream(picture.Bytes, writable: false);
        var source = BitmapFrame.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        double aspect = (double)width / height;
        int cutWidth  = source.PixelWidth, cutHeight = source.PixelHeight;
        if ((double)cutWidth / cutHeight > aspect) cutWidth  = Math.Max(1, (int)Math.Round(cutHeight * aspect));
        else                                       cutHeight = Math.Max(1, (int)Math.Round(cutWidth / aspect));

        var cut    = new CroppedBitmap(source, new Int32Rect((source.PixelWidth - cutWidth) / 2, (source.PixelHeight - cutHeight) / 2, cutWidth, cutHeight));
        var scaled = new TransformedBitmap(cut, new ScaleTransform((double)width / cutWidth, (double)height / cutHeight));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(scaled));
        using var output = new MemoryStream();
        encoder.Save(output);
        return new MediaBytes(output.ToArray(), "image/png");
    }
}

using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Nexaflow.Providers.Common;

namespace Nexaflow.AvaloniaShell.Platform;

/// <summary>
/// Fits a picture to a video model's frame with Avalonia's imaging, as Nexaflow.Core's <c>WpfPictureFitting</c> does with WPF's: the
/// centre cut to the frame's shape, scaled to its size, as a PNG. Drawn on the UI thread, where Avalonia renders.
/// </summary>
internal sealed class AvaloniaPictureFitting(IUiThread ui) : IPictureFitting
{
    public MediaBytes FitToFrame(MediaBytes picture, int width, int height) => ui.Run(() =>
    {
        using var input  = new MemoryStream(picture.Bytes, writable: false);
        using var source = new Bitmap(input);

        double aspect = (double)width / height;
        int cutWidth = source.PixelSize.Width, cutHeight = source.PixelSize.Height;
        if ((double)cutWidth / cutHeight > aspect) cutWidth  = Math.Max(1, (int)Math.Round(cutHeight * aspect));
        else                                       cutHeight = Math.Max(1, (int)Math.Round(cutWidth / aspect));

        // Source rectangles are in the bitmap's device-independent units, its pixels at its own DPI.
        double toUnits = 96.0 / source.Dpi.X;
        var cut = new Rect((source.PixelSize.Width - cutWidth) / 2 * toUnits, (source.PixelSize.Height - cutHeight) / 2 * toUnits,
                           cutWidth * toUnits, cutHeight * toUnits);

        using var frame = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var drawing = frame.CreateDrawingContext())
            drawing.DrawImage(source, cut, new Rect(0, 0, width, height));

        using var output = new MemoryStream();
        frame.Save(output, PngBitmapEncoderOptions.Default);
        return new MediaBytes(output.ToArray(), "image/png");
    });
}

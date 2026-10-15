using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// A stretch of what was laid out, painted into a picture — what keeping a picture of a block saves, what a block's copy
/// carries, and what a document written out in another format puts where something is drawn rather than written.
/// </summary>
public static class LayoutPictures
{
    /// <summary>
    /// <paramref name="box"/> of what <paramref name="root"/> lays out, painted <paramref name="scale"/> times its laid size at
    /// <paramref name="dpiX"/> by <paramref name="dpiY"/> dots to the inch, on <paramref name="ground"/> where one is given and
    /// on nothing where not — or null where there is nothing to paint.
    /// </summary>
    /// <param name="foreground">What anything drawn in the reader's text colour is drawn in.</param>
    public static BitmapSource? Painted(Piece root, Rect box, double scale, Brush foreground, Brush? ground, double dpiX = 96, double dpiY = 96)
    {
        if (box.IsEmpty || box.Width <= 0 || box.Height <= 0 || scale <= 0) return null;

        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            if (ground is not null) dc.DrawRectangle(ground, null, new Rect(0, 0, box.Width * scale, box.Height * scale));

            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushTransform(new TranslateTransform(-box.X, -box.Y));
            dc.PushClip(new RectangleGeometry(box));
            LayoutPainter.Paint(dc, root, foreground);
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(box.Width * scale * dpiX / 96)),
            Math.Max(1, (int)Math.Ceiling(box.Height * scale * dpiY / 96)),
            dpiX, dpiY, PixelFormats.Pbgra32);

        bitmap.Render(visual);
        bitmap.Freeze();

        return bitmap;
    }

    /// <summary>A picture as the PNG it is saved as.</summary>
    public static byte[] Png(BitmapSource picture)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));

        using var bytes = new MemoryStream();
        encoder.Save(bytes);

        return bytes.ToArray();
    }
}

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.IO.Common;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace Nexaflow.Features.Pdf.Editing;

/// <summary>
/// A picture inserted into a document, written as a one-page PDF of its own: a page the size it is given — its
/// neighbour's — with the picture scaled to fit, centred, standing as its EXIF orientation says. A JPEG of grey or
/// colour samples is written as it is; any other picture is decoded and written as a PNG, its transparency kept.
/// </summary>
internal static class PdfImagePageWriter
{
    /// <summary>
    /// Writes the picture at <paramref name="imagePath"/> on a page <paramref name="page"/> points in size.
    /// </summary>
    /// <exception cref="NotSupportedException">The file is not a picture this machine can decode.</exception>
    public static void Write(string imagePath, Size page, Stream output, CancellationToken ct)
    {
        var bytes = VirtualFileSystem.Instance.ReadAllBytes(imagePath);
        ct.ThrowIfCancellationRequested();

        BitmapFrame frame;
        try
        {
            frame = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
        {
            throw new NotSupportedException($"'{imagePath}' is not a picture this machine can decode.", ex);
        }

        var orientation = Orientation(frame);
        var turned = orientation >= 5;
        var shown = turned ? new Size(frame.PixelHeight, frame.PixelWidth) : new Size(frame.PixelWidth, frame.PixelHeight);
        var box = Fit(shown, page);

        using var builder = new PdfDocumentBuilder(output, disposeStream: false);
        var pdfPage = builder.AddPage(page.Width, page.Height);

        if (frame.Decoder is JpegBitmapDecoder && (frame.Format == PixelFormats.Gray8 || frame.Format == PixelFormats.Bgr24))
        {
            pdfPage.AddJpeg(new MemoryStream(bytes), Placement(orientation, box));
            return;
        }

        var upright = Upright(frame, orientation);
        using var png = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(upright));
        encoder.Save(png);
        png.Position = 0;
        pdfPage.AddPng(png, new PdfRectangle(box.Left, box.Top, box.Right, box.Bottom));
    }

    /// <summary>
    /// The largest box of <paramref name="picture"/>'s proportions that fits <paramref name="page"/>, centred on it —
    /// a picture smaller than the page is scaled up to it too. In page units, from the bottom left: <see cref="Rect.Top"/>
    /// is its lower edge.
    /// </summary>
    public static Rect Fit(Size picture, Size page)
    {
        var scale = Math.Min(page.Width / picture.Width, page.Height / picture.Height);
        var width = picture.Width * scale;
        var height = picture.Height * scale;
        return new Rect((page.Width - width) / 2, (page.Height - height) / 2, width, height);
    }

    /// <summary>
    /// The matrix that draws a picture stored as it was taken into <paramref name="box"/> as it is to be seen: the
    /// picture's unit square turned or mirrored by its EXIF <paramref name="orientation"/> (1 to 8), then scaled and
    /// placed. Upright pictures are 1.
    /// </summary>
    public static TransformationMatrix Placement(int orientation, Rect box)
    {
        // Each orientation as where it takes the picture's unit square: x' = a·u + c·v + e, y' = b·u + d·v + f.
        var (a, b, c, d, e, f) = orientation switch
        {
            2 => (-1.0, 0.0, 0.0, 1.0, 1.0, 0.0),
            3 => (-1.0, 0.0, 0.0, -1.0, 1.0, 1.0),
            4 => (1.0, 0.0, 0.0, -1.0, 0.0, 1.0),
            5 => (0.0, -1.0, -1.0, 0.0, 1.0, 1.0),
            6 => (0.0, -1.0, 1.0, 0.0, 0.0, 1.0),
            7 => (0.0, 1.0, 1.0, 0.0, 0.0, 0.0),
            8 => (0.0, 1.0, -1.0, 0.0, 1.0, 0.0),
            _ => (1.0, 0.0, 0.0, 1.0, 0.0, 0.0),
        };

        return TransformationMatrix.FromValues(
            box.Width * a, box.Height * b,
            box.Width * c, box.Height * d,
            box.Width * e + box.X, box.Height * f + box.Y);
    }

    /// <summary>
    /// The picture's EXIF orientation, 1 to 8; 1 where it states none.
    /// </summary>
    private static int Orientation(BitmapFrame frame)
    {
        if (frame.Metadata is not BitmapMetadata metadata) return 1;
        foreach (var query in new[] { "/app1/ifd/{ushort=274}", "/ifd/{ushort=274}" })
        {
            try
            {
                if (metadata.ContainsQuery(query) && metadata.GetQuery(query) is ushort value && value is >= 1 and <= 8)
                    return value;
            }
            catch (NotSupportedException) { }
        }

        return 1;
    }

    /// <summary>
    /// The picture as it is to be seen, in colours a PNG holds: turned or mirrored by its orientation, and converted
    /// from CMYK, which a PNG cannot carry.
    /// </summary>
    private static BitmapSource Upright(BitmapSource picture, int orientation)
    {
        if (picture.Format == PixelFormats.Cmyk32)
            picture = new FormatConvertedBitmap(picture, PixelFormats.Bgr32, null, 0);

        Transform? transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(270) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new ScaleTransform(-1, 1), new RotateTransform(90) } },
            8 => new RotateTransform(270),
            _ => null,
        };

        if (transform is null) return picture;

        var turned = new TransformedBitmap(picture, transform);
        turned.Freeze();
        return turned;
    }
}

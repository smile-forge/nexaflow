using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Pdf.Document;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>
/// Whether a page's artwork runs across the lines it would be cut along, read from a picture of the page. A line that
/// crosses nothing — the gutter between two pages printed to a sheet — is one colour from end to end, whatever colour
/// the sheet is; where artwork crosses it, the line meets other colours.
/// </summary>
internal sealed class PdfInk
{
    /// <summary>How far a channel may stray from the line's own colour and still be that colour: smoothing, a scan's grain.</summary>
    private const int Tolerance = 40;

    /// <summary>The share of a line that may be other colours before artwork is said to cross it: a speck, a crop mark.</summary>
    private const double Allowance = 0.02;

    private readonly byte[] _pixels;
    private readonly int _width;
    private readonly int _height;

    private PdfInk(byte[] pixels, int width, int height)
    {
        _pixels = pixels;
        _width = width;
        _height = height;
    }

    /// <summary>The page as <paramref name="picture"/> shows it.</summary>
    public static PdfInk Of(BitmapSource picture)
    {
        var bgra = picture.Format == PixelFormats.Bgra32 || picture.Format == PixelFormats.Pbgra32
            ? picture
            : new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);
        return new PdfInk(pixels, bgra.PixelWidth, bgra.PixelHeight);
    }

    /// <summary>Whether artwork crosses <paramref name="cut"/>.</summary>
    public bool Crosses(PdfPageCut cut)
    {
        var length = cut.Vertical ? _height : _width;
        var breadth = cut.Vertical ? _width : _height;
        if (length == 0 || breadth == 0) return false;

        var at = Math.Clamp((int)(cut.At * breadth), 0, breadth - 1);
        var line = new int[length * 3];
        for (var i = 0; i < length; i++)
        {
            var offset = cut.Vertical ? (i * _width + at) * 4 : (at * _width + i) * 4;
            line[i * 3] = _pixels[offset];
            line[i * 3 + 1] = _pixels[offset + 1];
            line[i * 3 + 2] = _pixels[offset + 2];
        }

        var own = new[] { Median(line, 0, length), Median(line, 1, length), Median(line, 2, length) };
        var other = 0;
        for (var i = 0; i < length; i++)
        {
            if (Math.Abs(line[i * 3] - own[0]) > Tolerance
                || Math.Abs(line[i * 3 + 1] - own[1]) > Tolerance
                || Math.Abs(line[i * 3 + 2] - own[2]) > Tolerance)
                other++;
        }

        return other > Allowance * length;
    }

    /// <summary>The middle value of one channel along the line.</summary>
    private static int Median(int[] line, int channel, int length)
    {
        var values = new int[length];
        for (var i = 0; i < length; i++)
            values[i] = line[i * 3 + channel];
        Array.Sort(values);
        return values[length / 2];
    }
}

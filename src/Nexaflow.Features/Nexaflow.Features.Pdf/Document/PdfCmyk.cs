using System;
using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// Device CMYK as a screen shows it. Device CMYK names inks, not colours: what the inks look like depends on the press,
/// and a document that says nothing more means the press most documents are made for. That press is Windows' own
/// <c>RSWOP.icm</c> — US Web Coated (SWOP), the profile every Windows installation carries — and colours go through it
/// with Windows' colour management, so a CMYK page looks as it does in any colour-managed reader rather than the
/// oversaturated <c>(1 - c)(1 - k)</c> arithmetic gives. Only where the profile cannot be found does that arithmetic stand in.
/// </summary>
internal static class PdfCmyk
{
    private static readonly Lazy<ColorContext?> Swop = new(Profile);
    private static readonly Lazy<ColorContext> Srgb = new(static () => new ColorContext(PixelFormats.Bgra32));
    private static readonly ConcurrentDictionary<uint, Color> Solids = new();

    /// <summary>One CMYK colour, each ink 0..1, as sRGB carrying <paramref name="alpha"/> (0..255).</summary>
    public static Color Colour(double c, double m, double y, double k, byte alpha)
    {
        var key = (uint)PdfColours.ToByte(c) << 24 | (uint)PdfColours.ToByte(m) << 16 | (uint)PdfColours.ToByte(y) << 8 | PdfColours.ToByte(k);
        var rgb = Solids.GetOrAdd(key, static packed =>
        {
            var inks = new[] { (byte)(packed >> 24), (byte)(packed >> 16), (byte)(packed >> 8), (byte)packed };
            var converted = Convert(inks, 1, 1);
            return Color.FromRgb(converted[0], converted[1], converted[2]);
        });
        return Color.FromArgb(alpha, rgb.R, rgb.G, rgb.B);
    }

    /// <summary>
    /// CMYK samples, four bytes a pixel, as RGB samples three bytes a pixel.
    /// </summary>
    public static byte[] Convert(ReadOnlySpan<byte> cmyk, int width, int height)
    {
        var count = width * height;
        if (Swop.Value is { } swop)
        {
            var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Cmyk32, null, cmyk[..(count * 4)].ToArray(), width * 4);
            var converted = new ColorConvertedBitmap(source, swop, Srgb.Value, PixelFormats.Rgb24);
            var rgb = new byte[width * 3 * height];
            converted.CopyPixels(rgb, width * 3, 0);
            return rgb;
        }

        var naive = new byte[count * 3];
        for (var p = 0; p < count; p++)
        {
            PdfColours.CmykToRgb(cmyk[p * 4], cmyk[p * 4 + 1], cmyk[p * 4 + 2], cmyk[p * 4 + 3], out var r, out var g, out var b);
            naive[p * 3] = r;
            naive[p * 3 + 1] = g;
            naive[p * 3 + 2] = b;
        }

        return naive;
    }

    private static ColorContext? Profile()
    {
        try
        {
            var path = Path.Combine(Environment.SystemDirectory, "spool", "drivers", "color", "RSWOP.icm");
            return File.Exists(path) ? new ColorContext(new Uri(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}

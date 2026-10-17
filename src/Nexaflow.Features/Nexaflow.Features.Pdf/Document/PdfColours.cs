using System;
using System.Windows.Media;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Graphics.Core;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>Colour from a PDF's colour spaces into the screen's.</summary>
internal static class PdfColours
{
    /// <summary>
    /// <paramref name="colour"/> as an sRGB colour carrying <paramref name="alpha"/>; black when the document gave
    /// none. A pattern has no single colour and is not converted here. Device CMYK goes through the press it means under
    /// <paramref name="intent"/>; every other colour space has converted its colour already.
    /// </summary>
    public static Color ToColour(IColor? colour, double alpha, RenderingIntent intent)
    {
        var a = ToByte(alpha);
        if (colour is null) return Color.FromArgb(a, 0, 0, 0);
        if (colour is CMYKColor cmyk) return PdfCmyk.Colour(cmyk.C, cmyk.M, cmyk.Y, cmyk.K, a, intent);

        var (red, green, blue) = colour.ToRGBValues();
        return Color.FromArgb(a, ToByte(red), ToByte(green), ToByte(blue));
    }

    /// <summary>
    /// A device CMYK sample as RGB by the specification's own arithmetic (§10.4.2): <c>(1 - c)(1 - k)</c>, black
    /// generation and undercolour removal left at identity. What <see cref="PdfCmyk"/> falls back on when Windows' CMYK
    /// profile cannot be found.
    /// </summary>
    public static void CmykToRgb(byte c, byte m, byte y, byte k, out byte r, out byte g, out byte b)
    {
        var white = 255 - k;
        r = (byte)((255 - c) * white / 255);
        g = (byte)((255 - m) * white / 255);
        b = (byte)((255 - y) * white / 255);
    }

    /// <summary>A 0..1 component as a byte, clamped.</summary>
    public static byte ToByte(double component) => (byte)Math.Round(Math.Clamp(component, 0, 1) * 255);
}

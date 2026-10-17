using System;
using System.Windows.Media;
using UglyToad.PdfPig.Graphics.Colors.Icc;
using UglyToad.PdfPig.Graphics.Core;

namespace Nexaflow.Features.Pdf.Document;

/// <summary>
/// Device CMYK as a screen shows it. Device CMYK names inks, not colours: what the inks look like depends on the press,
/// and a document that says nothing more means the press most documents are made for. That press is Windows' own
/// <c>RSWOP.icm</c> — US Web Coated (SWOP), the profile every Windows installation carries — and colours go through it
/// with Windows' colour engine (<see cref="PdfColourEngine"/>) under the page's rendering intent, so a CMYK page looks
/// as it does in any colour-managed reader rather than the oversaturated <c>(1 - c)(1 - k)</c> arithmetic gives. Only
/// where the profile cannot be found does that arithmetic stand in.
/// </summary>
internal static class PdfCmyk
{
    private static readonly Lazy<PdfIccProfile?> Swop = new(static () => PdfColourEngine.Shared.Installed("RSWOP.icm"));

    /// <summary>One CMYK colour, each ink 0..1, as sRGB carrying <paramref name="alpha"/> (0..255).</summary>
    public static Color Colour(double c, double m, double y, double k, byte alpha, RenderingIntent intent)
    {
        if (Press(intent) is { } swop)
        {
            var (red, green, blue) = swop.ToRgb([c, m, y, k]);
            return Color.FromArgb(alpha, PdfColours.ToByte(red), PdfColours.ToByte(green), PdfColours.ToByte(blue));
        }

        PdfColours.CmykToRgb(PdfColours.ToByte(c), PdfColours.ToByte(m), PdfColours.ToByte(y), PdfColours.ToByte(k),
            out var r, out var g, out var b);
        return Color.FromArgb(alpha, r, g, b);
    }

    /// <summary>
    /// CMYK samples, four bytes a pixel, as RGB samples three bytes a pixel.
    /// </summary>
    public static byte[] Convert(ReadOnlySpan<byte> cmyk, int width, int height, RenderingIntent intent)
    {
        var count = width * height;
        var rgb = new byte[count * 3];
        if (Press(intent) is { } swop)
        {
            swop.Transform(cmyk[..(count * 4)], rgb);
            return rgb;
        }

        for (var p = 0; p < count; p++)
        {
            PdfColours.CmykToRgb(cmyk[p * 4], cmyk[p * 4 + 1], cmyk[p * 4 + 2], cmyk[p * 4 + 3], out var r, out var g, out var b);
            rgb[p * 3] = r;
            rgb[p * 3 + 1] = g;
            rgb[p * 3 + 2] = b;
        }

        return rgb;
    }

    /// <summary>The press's transform under <paramref name="intent"/>, or under relative colorimetric, the PDF default.</summary>
    private static IIccTransform? Press(RenderingIntent intent) =>
        Swop.Value is { } press
        && (press.TryGetTransform(intent, out var transform) || press.TryGetTransform(RenderingIntent.RelativeColorimetric, out transform))
            ? transform
            : null;
}

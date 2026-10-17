using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UglyToad.PdfPig.Graphics.Core;

namespace Nexaflow.Features.Pdf.Rendering;

/// <summary>
/// PDF's blend modes (ISO 32000-1 §11.3.5), worked per pixel: a source composited over a backdrop as the mode says. WPF
/// composites only by source-over, so a blended group is drawn twice to pictures — what lies under it, and itself — and
/// combined here.
/// <para>
/// The result carries the source's coverage as its alpha and, as its colour, the blend weighted by how much of each
/// pixel the backdrop covers, so drawing it source-over the backdrop gives the page the mode asks for where the source
/// covers it, and the backdrop untouched where it does not.
/// </para>
/// </summary>
internal static class PdfBlending
{
    /// <summary>
    /// <paramref name="source"/> (premultiplied BGRA, transparent where it does not paint) blended over
    /// <paramref name="backdrop"/> (premultiplied BGRA, transparent where the page has painted nothing) by
    /// <paramref name="mode"/>: a straight-alpha picture the same size, to draw source-over the backdrop.
    /// <para>
    /// Where the backdrop covers <c>αb</c> of a pixel, the blend applies to that much of it and the source shows as itself
    /// over the rest: the colour is <c>(1 - αb)·Cs + αb·B(Cb, Cs)</c> (ISO 32000-1, 11.3.6). Drawn source-over the backdrop
    /// that gives the group's result exactly, and keeps its coverage, so a later blend reads it as a backdrop in turn.
    /// </para>
    /// </summary>
    public static BitmapSource Composite(BitmapSource backdrop, BitmapSource source, BlendMode mode)
    {
        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var stride = width * 4;
        var under = Pixels(backdrop, width, height);
        var over = Pixels(source, width, height);
        var result = new byte[stride * height];

        Span<double> cb = stackalloc double[3];
        Span<double> cs = stackalloc double[3];
        Span<double> blended = stackalloc double[3];

        for (var i = 0; i < result.Length; i += 4)
        {
            var alpha = over[i + 3];
            if (alpha == 0) continue;

            var a = alpha / 255.0;
            var ab = under[i + 3] / 255.0;
            for (var c = 0; c < 3; c++)
            {
                // BGRA order; colours to 0..1, both unpremultiplied.
                cb[2 - c] = ab == 0 ? 0 : Math.Min(1, under[i + c] / 255.0 / ab);
                cs[2 - c] = Math.Min(1, over[i + c] / 255.0 / a);
            }

            if (ab == 0) cs.CopyTo(blended);
            else Blend(mode, cb, cs, blended);

            for (var c = 0; c < 3; c++)
            {
                var colour = (1 - ab) * cs[2 - c] + ab * Math.Clamp(blended[2 - c], 0, 1);
                result[i + c] = (byte)Math.Round(colour * 255);
            }

            result[i + 3] = alpha;
        }

        var picture = BitmapSource.Create(width, height, source.DpiX, source.DpiY, PixelFormats.Bgra32, null, result, stride);
        picture.Freeze();
        return picture;
    }

    /// <summary>The blend function <c>B(Cb, Cs)</c> for one pixel, each colour RGB 0..1.</summary>
    internal static void Blend(BlendMode mode, ReadOnlySpan<double> cb, ReadOnlySpan<double> cs, Span<double> into)
    {
        switch (mode)
        {
            case BlendMode.Hue:
                SetLum(SetSat(cs, Sat(cb), into), Lum(cb), into);
                return;
            case BlendMode.Saturation:
                SetLum(SetSat(cb, Sat(cs), into), Lum(cb), into);
                return;
            case BlendMode.Color:
                SetLum(cs, Lum(cb), into);
                return;
            case BlendMode.Luminosity:
                SetLum(cb, Lum(cs), into);
                return;
        }

        for (var c = 0; c < 3; c++) into[c] = Separable(mode, cb[c], cs[c]);
    }

    private static double Separable(BlendMode mode, double b, double s) => mode switch
    {
        BlendMode.Multiply   => b * s,
        BlendMode.Screen     => b + s - b * s,
        BlendMode.Overlay    => HardLight(s, b),
        BlendMode.Darken     => Math.Min(b, s),
        BlendMode.Lighten    => Math.Max(b, s),
        BlendMode.ColorDodge => b == 0 ? 0 : s >= 1 ? 1 : Math.Min(1, b / (1 - s)),
        BlendMode.ColorBurn  => b >= 1 ? 1 : s <= 0 ? 0 : 1 - Math.Min(1, (1 - b) / s),
        BlendMode.HardLight  => HardLight(b, s),
        BlendMode.SoftLight  => SoftLight(b, s),
        BlendMode.Difference => Math.Abs(b - s),
        BlendMode.Exclusion  => b + s - 2 * b * s,
        _                    => s,
    };

    private static double HardLight(double b, double s) => s <= 0.5 ? b * 2 * s : Screen(b, 2 * s - 1);

    private static double Screen(double b, double s) => b + s - b * s;

    private static double SoftLight(double b, double s)
    {
        if (s <= 0.5) return b - (1 - 2 * s) * b * (1 - b);
        var d = b <= 0.25 ? ((16 * b - 12) * b + 4) * b : Math.Sqrt(b);
        return b + (2 * s - 1) * (d - b);
    }

    private static double Lum(ReadOnlySpan<double> c) => 0.3 * c[0] + 0.59 * c[1] + 0.11 * c[2];

    private static double Sat(ReadOnlySpan<double> c) => Math.Max(c[0], Math.Max(c[1], c[2])) - Math.Min(c[0], Math.Min(c[1], c[2]));

    /// <summary><paramref name="c"/> with its luminosity moved to <paramref name="l"/>, kept in gamut.</summary>
    private static void SetLum(ReadOnlySpan<double> c, double l, Span<double> into)
    {
        var d = l - Lum(c);
        for (var i = 0; i < 3; i++) into[i] = c[i] + d;
        Clip(into);
    }

    private static void Clip(Span<double> c)
    {
        var l = Lum(c);
        var n = Math.Min(c[0], Math.Min(c[1], c[2]));
        var x = Math.Max(c[0], Math.Max(c[1], c[2]));
        for (var i = 0; i < 3; i++)
        {
            if (n < 0 && l - n != 0) c[i] = l + (c[i] - l) * l / (l - n);
            if (x > 1 && x - l != 0) c[i] = l + (c[i] - l) * (1 - l) / (x - l);
        }
    }

    /// <summary><paramref name="c"/> with its saturation set to <paramref name="s"/>, its hue kept.</summary>
    private static Span<double> SetSat(ReadOnlySpan<double> c, double s, Span<double> into)
    {
        var max = Math.Max(c[0], Math.Max(c[1], c[2]));
        var min = Math.Min(c[0], Math.Min(c[1], c[2]));
        for (var i = 0; i < 3; i++)
            into[i] = max > min ? (c[i] - min) * s / (max - min) : 0;
        return into;
    }

    private static byte[] Pixels(BitmapSource bitmap, int width, int height)
    {
        var bgra = bitmap.Format == PixelFormats.Pbgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Pbgra32, null, 0);
        var pixels = new byte[width * 4 * height];
        bgra.CopyPixels(new System.Windows.Int32Rect(0, 0, Math.Min(width, bgra.PixelWidth), Math.Min(height, bgra.PixelHeight)),
            pixels, width * 4, 0);
        return pixels;
    }
}

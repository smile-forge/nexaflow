using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Nexaflow.Markdown.Layout;

namespace Nexaflow.Visuals.Text.Editor.Highlighting;

/// <summary>
/// Recognises a colour written in source — <c>#FF3B30</c>, <c>rgb(255 59 48)</c>, <c>hsl(4 100% 59%)</c>,
/// <c>Tomato</c> — so the editor can show what it actually looks like.
///
/// <para>
/// Language matters for exactly one thing, and it is the thing that silently produces a wrong colour:
/// <b>where the alpha channel sits in an 8-digit hex literal</b>. XAML reads <c>#AARRGGBB</c>, CSS reads
/// <c>#RRGGBBAA</c>. The same eight characters are two different colours, so the caller says which dialect it
/// is reading rather than this guessing.
/// </para>
/// </summary>
public static class ColorLiterals
{
    /// <summary>Grammar ids that put the alpha channel first in a hex literal (the XAML/WPF convention).</summary>
    private static readonly HashSet<string> AlphaFirstGrammars =
        new(StringComparer.Ordinal) { "xaml", "xml" };

    public static bool AlphaFirst(string? grammarId) => grammarId is not null && AlphaFirstGrammars.Contains(grammarId);



    /// <summary>
    /// The colour <paramref name="token"/> denotes, or null when it denotes none. Surrounding quotes are
    /// ignored, so an attribute value can be passed as it appears in the document.
    /// </summary>
    public static LayoutColor? Parse(string token, bool alphaFirst)
    {
        var s = token.Trim().Trim('"', '\'');
        if (s.Length == 0) return null;

        if (s[0] == '#') return Hex(s.AsSpan(1), alphaFirst);
        if (s.IndexOf('(') > 0) return Functional(s);
        return Named(s);
    }

    /// <summary>
    /// The colour <paramref name="word"/> names — one of the X11 names CSS and XAML share, in any case — or null for any other word.
    /// </summary>
    private static LayoutColor? Named(string word) => word.All(char.IsAsciiLetter) ? LayoutColors.Parse(word) : null;

    private static LayoutColor? Hex(ReadOnlySpan<char> h, bool alphaFirst)
    {
        foreach (var c in h)
            if (!Uri.IsHexDigit(c)) return null;

        static byte Pair(ReadOnlySpan<char> s, int i) =>
            byte.Parse(s.Slice(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        static byte Single(char c) =>
            (byte)(Convert.ToByte(c.ToString(), 16) * 0x11);   // #abc -> #aabbcc

        return h.Length switch
        {
            3 => LayoutColor.Of(Single(h[0]), Single(h[1]), Single(h[2])),
            4 => alphaFirst
                ? new LayoutColor(Single(h[0]), Single(h[1]), Single(h[2]), Single(h[3]))
                : new LayoutColor(Single(h[3]), Single(h[0]), Single(h[1]), Single(h[2])),
            6 => LayoutColor.Of(Pair(h, 0), Pair(h, 2), Pair(h, 4)),
            8 => alphaFirst
                ? new LayoutColor(Pair(h, 0), Pair(h, 2), Pair(h, 4), Pair(h, 6))
                : new LayoutColor(Pair(h, 6), Pair(h, 0), Pair(h, 2), Pair(h, 4)),
            _ => null,
        };
    }

    /// <summary>CSS functional notation. Both the legacy comma form and the modern space form are accepted,
    /// including the <c>/ alpha</c> suffix, because both are in real stylesheets.</summary>
    private static LayoutColor? Functional(string s)
    {
        var open = s.IndexOf('(');
        var close = s.LastIndexOf(')');
        if (close <= open) return null;

        var fn = s[..open].Trim().ToLowerInvariant();
        var args = s[(open + 1)..close].Replace('/', ' ').Replace(',', ' ')
                                       .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (args.Length is < 3 or > 4) return null;

        return fn switch
        {
            "rgb" or "rgba" => FromRgb(args),
            "hsl" or "hsla" => FromHsl(args),
            _ => null,
        };
    }

    private static LayoutColor? FromRgb(string[] a)
    {
        if (!Channel(a[0], 255, out var r) || !Channel(a[1], 255, out var g) || !Channel(a[2], 255, out var b))
            return null;
        var alpha = a.Length == 4 && Channel(a[3], 1, out var al) ? (byte)Math.Round(al * 255) : (byte)255;
        return new LayoutColor(alpha, (byte)Math.Round(r), (byte)Math.Round(g), (byte)Math.Round(b));
    }

    private static LayoutColor? FromHsl(string[] a)
    {
        if (!Number(a[0], out var h) || !Channel(a[1], 1, out var sat) || !Channel(a[2], 1, out var light))
            return null;
        var alpha = a.Length == 4 && Channel(a[3], 1, out var al) ? (byte)Math.Round(al * 255) : (byte)255;

        h = ((h % 360) + 360) % 360;
        var c = (1 - Math.Abs(2 * light - 1)) * sat;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = light - c / 2;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return new LayoutColor(alpha, Byte(r + m), Byte(g + m), Byte(b + m));
    }

    private static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);

    /// <summary>A channel as a bare number or a percentage, scaled to <paramref name="full"/>.</summary>
    private static bool Channel(string token, double full, out double value)
    {
        var percent = token.EndsWith('%');
        if (!Number(percent ? token[..^1] : token, out value)) return false;
        if (percent) value = value / 100 * full;
        value = Math.Clamp(value, 0, full);
        return true;
    }

    private static bool Number(string token, out double value) =>
        double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

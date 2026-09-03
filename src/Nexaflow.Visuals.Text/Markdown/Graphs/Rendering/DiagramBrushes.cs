using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Colour and brush helpers shared by the diagram renderers — freezing, alpha tinting, CSS colour
/// parsing and the readable-ink choice.  Each renderer had grown its own copy of these; sharing them
/// keeps a tint in one diagram the same strength as the same tint in another, and gives the one
/// place to fix a colour rule.
///
/// Every brush returned is frozen, so it is safe to hold and cheap to reuse.
/// </summary>
internal static class DiagramBrushes
{
    /// <summary>A frozen solid brush of <paramref name="c"/>.</summary>
    internal static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    /// <summary>A frozen brush of <paramref name="c"/> at <paramref name="alpha"/> — the standard wash/fill tint.</summary>
    internal static Brush Tint(Color c, byte alpha)
    {
        var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    /// <summary>As <see cref="Tint(Color, byte)"/>, taking the colour from an existing brush.</summary>
    internal static Brush Tint(Brush brush, byte alpha, Color fallback = default) =>
        Tint(ColorOf(brush, fallback), alpha);

    /// <summary>The colour behind a brush, or <paramref name="fallback"/> when it is not a solid one.</summary>
    internal static Color ColorOf(Brush? brush, Color fallback) =>
        (brush as SolidColorBrush)?.Color ?? fallback;

    /// <summary>Perceived brightness, 0–255 (ITU-R BT.601 weights).</summary>
    internal static double Luminance(Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;

    /// <summary>
    /// Picks readable ink for text sitting on <paramref name="background"/>: <paramref name="onLight"/>
    /// over a bright background, <paramref name="onDark"/> otherwise.  The threshold is the one the
    /// renderers already used.
    /// </summary>
    internal static Brush OnColor(Color background, Brush onLight, Brush onDark, double threshold = 140) =>
        Luminance(background) > threshold ? onLight : onDark;

    /// <summary>
    /// Parses a CSS-ish colour — <c>#rgb</c>/<c>#rrggbb</c>, a named colour, or <c>rgb(r, g, b)</c>
    /// (which <see cref="ColorConverter"/> does not accept).  Null when it is not a colour at all,
    /// so a caller can fall back rather than guess.
    /// </summary>
    internal static Color? ParseCss(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        color = color.Trim();

        if (color.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            int o = color.IndexOf('('), c = color.IndexOf(')');
            if (o > 0 && c > o)
            {
                var nums = color[(o + 1)..c].Split(',');
                if (nums.Length >= 3
                    && byte.TryParse(nums[0].Trim(), out byte r)
                    && byte.TryParse(nums[1].Trim(), out byte g)
                    && byte.TryParse(nums[2].Trim(), out byte b))
                    return Color.FromRgb(r, g, b);
            }
            return null;
        }

        try { return (Color)ColorConverter.ConvertFromString(color)!; }
        catch { return null; }
    }

    /// <summary>
    /// Flattens <paramref name="over"/> onto opaque <paramref name="under"/> — the colour a
    /// translucent fill actually shows as.  Needed before a luminance test, because a half-alpha
    /// fill over a dark ground is dark however bright its own colour is.
    /// </summary>
    internal static Color Composite(Color over, Color under)
    {
        double a = over.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round(over.R * a + under.R * (1 - a)),
            (byte)Math.Round(over.G * a + under.G * (1 - a)),
            (byte)Math.Round(over.B * a + under.B * (1 - a)));
    }
}

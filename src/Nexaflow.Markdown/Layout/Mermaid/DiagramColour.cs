namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>
/// Colour arithmetic: what a colour somebody wrote comes to, how bright it is, what a translucent one shows as over
/// what is behind it, and which of two inks reads over it.
///
/// <para>
/// The layer under <see cref="DiagramInk"/>, which is what a builder actually asks: this decides what
/// <c>#ff6b6b</c>, <c>red</c> and <c>rgb(1, 2, 3)</c> mean once, for every diagram, so a tint in one is the same
/// strength as the same tint in another.
/// </para>
/// </summary>
internal static class DiagramColour
{
    /// <summary><paramref name="colour"/> at <paramref name="alpha"/>, as an ink — the standard wash and fill tint.</summary>
    internal static LayoutInk Tint(ILayoutMedium medium, LayoutColor colour, byte alpha) =>
        medium.Ink(new LayoutColor(alpha, colour.R, colour.G, colour.B));

    /// <summary>As <see cref="Tint(ILayoutMedium, LayoutColor, byte)"/>, taking the colour from an ink.</summary>
    internal static LayoutInk Tint(ILayoutMedium medium, LayoutInk ink, byte alpha, LayoutColor fallback = default) =>
        Tint(medium, ColorOf(ink, fallback), alpha);

    /// <summary>The colour an ink is, or <paramref name="fallback"/> when it is not one colour all over.</summary>
    internal static LayoutColor ColorOf(LayoutInk? ink, LayoutColor fallback) => ink is { Solid: true } ? ink.Color : fallback;

    /// <summary>Perceived brightness, 0–255 (ITU-R BT.601 weights).</summary>
    internal static double Luminance(LayoutColor c) => (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B);

    /// <summary>
    /// Picks readable ink for text sitting on <paramref name="background"/>: <paramref name="onLight"/> over a bright background,
    /// <paramref name="onDark"/> otherwise.
    /// </summary>
    internal static LayoutInk OnColor(LayoutColor background, LayoutInk onLight, LayoutInk onDark, double threshold = 140) =>
        Luminance(background) > threshold ? onLight : onDark;

    /// <summary>
    /// Parses a CSS-ish colour — <c>#rgb</c>/<c>#rrggbb</c>, a named colour, or <c>rgb(r, g, b)</c>. Null when it is not a colour at
    /// all, so a caller can fall back rather than guess.
    /// </summary>
    internal static LayoutColor? ParseCss(string? color)
    {
        if (string.IsNullOrWhiteSpace(color)) return null;
        color = color.Trim();

        if (color.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            int open = color.IndexOf('('), close = color.IndexOf(')');
            if (open > 0 && close > open)
            {
                var parts = color[(open + 1)..close].Split(',');
                if (parts.Length >= 3
                    && byte.TryParse(parts[0].Trim(), out var r)
                    && byte.TryParse(parts[1].Trim(), out var g)
                    && byte.TryParse(parts[2].Trim(), out var b))
                    return LayoutColor.Of(r, g, b);
            }

            return null;
        }

        return LayoutColors.Parse(color);
    }

    /// <summary>
    /// Flattens <paramref name="over"/> onto opaque <paramref name="under"/> — the colour a translucent fill actually shows as. Needed
    /// before a luminance test, because a half-alpha fill over a dark ground is dark however bright its own colour is.
    /// </summary>
    internal static LayoutColor Composite(LayoutColor over, LayoutColor under)
    {
        var a = over.A / 255.0;
        return LayoutColor.Of(
            (byte)Math.Round((over.R * a) + (under.R * (1 - a))),
            (byte)Math.Round((over.G * a) + (under.G * (1 - a))),
            (byte)Math.Round((over.B * a) + (under.B * (1 - a))));
    }
}

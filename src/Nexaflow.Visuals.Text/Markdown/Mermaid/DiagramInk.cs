using System;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// What a diagram is drawn in: a colour as its source or front matter wrote it, the theme's colour for one of a series, a
/// colour faded, and ink that reads over a fill.
///
/// <para>
/// <strong>A builder asks for its colours here rather than making brushes.</strong> A colour nobody wrote is always the
/// theme's, so every diagram follows the theme without knowing what it is, and every brush it is handed is frozen. What
/// <c>#ff6b6b</c>, <c>red</c> or <c>rgb(1, 2, 3)</c> comes to is decided once, for all of them.
/// </para>
/// </summary>
internal sealed class DiagramInk(MarkdownPalette palette)
{
    /// <summary>A colour as a style or the front matter wrote it, or null where it wrote none this understands.</summary>
    public Brush? Written(string? colour)
    {
        if (DiagramColour.ParseCss(colour) is not { } parsed) return null;

        var brush = new SolidColorBrush(parsed);
        brush.Freeze();
        return brush;
    }

    /// <summary>The theme's colour for the <paramref name="order"/>th of a series — a slice, a set, a line — round again past the last.</summary>
    public Brush Series(int order) => palette.Series[((order % palette.Series.Count) + palette.Series.Count) % palette.Series.Count];

    /// <summary>The colour written for one of a series, and the theme's for its place where none is.</summary>
    public Brush Series(int order, string? written) => Written(written) ?? Series(order);

    /// <summary>Ink that reads over <paramref name="fill"/>: the theme's dark ink over a light fill, and its light ink over a dark one.</summary>
    public Brush Over(Brush fill) =>
        DiagramColour.OnColor(DiagramColour.ColorOf(fill, Colors.Gray), palette.QrDark, palette.QrLight);

    /// <summary>
    /// The colour a diagram's own surface comes to, opaque. A theme's surfaces are translucent — a light theme's are
    /// alpha-black — so anything painted to hide what is under it has to be painted in what they come to over the page, not
    /// in the surface brush itself.
    /// </summary>
    public static Color Under(MarkdownPalette palette) =>
        DiagramColour.Composite(
            DiagramColour.ColorOf(palette.CodeBg, Colors.Black),
            DiagramColour.Luminance(DiagramColour.ColorOf(palette.Text, Colors.White)) > 140 ? Colors.Black : Colors.White);

    /// <summary>That colour as a brush — what a diagram paints on to cover what it has already drawn.</summary>
    public Brush Surface => Frozen(Under(palette));

    /// <summary>A brush at <paramref name="opacity"/> — itself, where that is whole.</summary>
    public static Brush Faded(Brush brush, double opacity)
    {
        if (opacity >= 1) return brush;

        var faded = brush.Clone();
        faded.Opacity = Math.Max(0, opacity);
        faded.Freeze();
        return faded;
    }

    /// <summary>
    /// The colour a share from nought to one takes along a ramp. The other colour question: <c>Series</c>
    /// says which of a few a group takes, and this says how far along a run a number is.
    /// </summary>
    public Brush Scale(DiagramRamp ramp, double share) => Frozen(DiagramColours.At(ramp, share));

    /// <summary>
    /// The same along a run of colours read off a ramp or written out — the colours themselves, so a
    /// block that named them is not read again for every mark.
    /// </summary>
    public Brush Scale(IReadOnlyList<Color> stops, double share) => Frozen(DiagramColours.At(stops, share));

    /// <summary>
    /// The dashes a style's <c>stroke-dasharray</c> writes, as the lengths drawn and left — or null where it writes none this can
    /// read. Any diagram whose styling can ask for a dashed line reads it the same way.
    /// </summary>
    public static DoubleCollection? Dashes(string? said)
    {
        if (said is not { Length: > 0 }) return null;

        var lengths = said.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(MermaidNumber.Read)
            .OfType<double>()
            .Where(length => length > 0)
            .ToList();

        if (lengths.Count == 0) return null;

        var dashes = new DoubleCollection(lengths);
        dashes.Freeze();
        return dashes;
    }

    private static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }
}

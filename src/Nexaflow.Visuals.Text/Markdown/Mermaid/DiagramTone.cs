using System;
using System.Collections.Generic;
using System.Windows.Media;


namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// A bank of colours a diagram grades its cards by, and how one card is painted from a band of it.
///
/// <para>
/// <strong>A grading is information, not decoration.</strong> Where a diagram sorts what it draws into levels, how deep a
/// card is drawn says which level it is at — so the bands are made from the theme's own accent rather than from fixed hex,
/// and a retheme retunes the whole diagram instead of leaving one level stubbornly the colour it shipped as. Which bank a
/// diagram uses is the diagram's; <see cref="Shaded"/> is how it makes one.
/// </para>
///
/// <para>
/// The ink is chosen for legibility against the fill the card actually shows as, so a pale colour written in the source
/// takes dark text without the author having to say so.
/// </para>
/// </summary>
/// <param name="bands">The colour of each band, deepest first.</param>
internal sealed class DiagramTone(StyleFormat palette, IReadOnlyList<Brush> bands)
{
    /// <summary>Near-black and near-white card ink. Not quite pure, so a card never out-contrasts the page it sits on.</summary>
    private static readonly Brush Dark = DiagramColour.Frozen(Color.FromRgb(0x14, 0x16, 0x1C));
    private static readonly Brush Light = DiagramColour.Frozen(Color.FromRgb(0xF4, 0xF6, 0xFB));

    /// <summary>
    /// What a translucent fill actually shows as — the light theme's surfaces are alpha-black, so a brightness test on the
    /// raw fill would read every card as dark.
    /// </summary>
    private readonly Color under = DiagramInk.Under(palette);

    /// <summary>The colour of one band, held to the bank where a diagram asks past the end of it.</summary>
    public Brush Band(int band) => bands[Math.Clamp(band, 0, bands.Count - 1)];

    /// <summary>
    /// The three brushes a card is painted with: what was written wins, then the band. The outline is the fill deepened,
    /// and the ink is whichever of dark and light reads on the fill.
    /// </summary>
    public (Brush Fill, Brush Stroke, Brush Ink) Card(int band, string? fill, string? border, string? ink)
    {
        var painted = DiagramColour.ParseCss(fill) is { } literal ? DiagramColour.Frozen(literal) : Band(band);
        var colour = DiagramColour.ColorOf(painted, this.under);

        var stroke = DiagramColour.ParseCss(border) is { } edge
            ? DiagramColour.Frozen(edge)
            : DiagramColour.Frozen(Scaled(colour, 1.45));

        var written = DiagramColour.ParseCss(ink) is { } said
            ? DiagramColour.Frozen(said)
            : DiagramColour.OnColor(DiagramColour.Composite(colour, this.under), Dark, Light);

        return (painted, stroke, written);
    }

    /// <summary>
    /// A band of the theme's accent: under one darkens it, over one lightens it toward white. This is how a diagram makes a
    /// grading that follows the theme — the numbers are the diagram's, since what the levels are is the diagram's.
    /// </summary>
    public static Brush Shaded(StyleFormat palette, double factor) =>
        DiagramColour.Frozen(Scaled(DiagramColour.ColorOf(palette.Accent, Colors.SteelBlue), factor));

    /// <summary>Softer ink for a second line on a card — its own, tinted. Translucent, so it settles against its fill.</summary>
    public static Brush Muted(Brush ink) => DiagramColour.Tint(ink, 0xB4, Colors.Gray);

    /// <summary>Scales a colour's brightness, keeping its hue.</summary>
    private static Color Scaled(Color colour, double factor)
    {
        if (factor <= 1)
            return Color.FromRgb((byte)Math.Round(colour.R * factor),
                                 (byte)Math.Round(colour.G * factor),
                                 (byte)Math.Round(colour.B * factor));

        var toward = Math.Min(1, factor - 1);

        return Color.FromRgb((byte)Math.Round(colour.R + ((255 - colour.R) * toward)),
                             (byte)Math.Round(colour.G + ((255 - colour.G) * toward)),
                             (byte)Math.Round(colour.B + ((255 - colour.B) * toward)));
    }
}

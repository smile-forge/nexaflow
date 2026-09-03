using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// The colours a C4 diagram paints its elements with, resolved once per render from the active
/// <see cref="MarkdownPalette"/>.
///
/// C4-PlantUML ships a fixed scheme (person <c>#08427b</c>, system <c>#1168bd</c>, container
/// <c>#438dd5</c>, component <c>#85bbf0</c>, external <c>#999999</c>) whose *information* is the
/// grading, not the particular blues: the deeper the colour the higher the abstraction, and grey
/// means "not ours". So the defaults here reproduce that grading from the theme's own accent instead
/// of the literal hex — a diagram reads as C4 on every theme, and a retheme retunes it rather than
/// leaving one region stubbornly cornflower blue. A theme that wants the canonical scheme (or any
/// other) sets the <c>C4*Brush</c> keys, and an explicit <c>UpdateElementStyle($bgColor=…)</c> in the
/// diagram still wins over both.
/// </summary>
internal sealed class C4Palette
{
    private readonly MarkdownPalette _p;
    private readonly Color _bg;

    private C4Palette(MarkdownPalette p)
    {
        _p = p;
        // What a translucent fill actually shows as — the Light palette's surfaces are alpha-black,
        // so a luminance test on the raw fill would read every card as dark.
        _bg = DiagramBrushes.Composite(
            DiagramBrushes.ColorOf(p.CodeBg, Colors.Black),
            DiagramBrushes.Luminance(DiagramBrushes.ColorOf(p.Text, Colors.White)) > 140 ? Colors.Black : Colors.White);

        var accent = DiagramBrushes.ColorOf(p.Accent, Colors.SteelBlue);
        var muted  = DiagramBrushes.ColorOf(p.TextMuted, Colors.Gray);

        // The C4 grading, in the theme's own accent: deepest for the outermost abstraction.
        Person         = p.C4Person         ?? DiagramBrushes.Frozen(Shade(accent, 0.55));
        System         = p.C4System         ?? DiagramBrushes.Frozen(Shade(accent, 0.78));
        Container      = p.C4Container      ?? DiagramBrushes.Frozen(accent);
        Component      = p.C4Component      ?? DiagramBrushes.Frozen(Shade(accent, 1.35));
        External       = p.C4External       ?? DiagramBrushes.Frozen(muted);
        Boundary       = p.C4Boundary       ?? p.TextMuted;
        DeploymentNode = p.C4DeploymentNode ?? p.QuoteBg;
    }

    internal static C4Palette Resolve(MarkdownPalette palette) => new(palette);

    /// <summary>Near-black and near-white card ink. Not quite pure, so a card never out-contrasts
    /// the page it sits on.</summary>
    private static readonly Brush Ink   = DiagramBrushes.Frozen(Color.FromRgb(0x14, 0x16, 0x1C));
    private static readonly Brush Paper = DiagramBrushes.Frozen(Color.FromRgb(0xF4, 0xF6, 0xFB));

    internal Brush Person         { get; }
    internal Brush System         { get; }
    internal Brush Container      { get; }
    internal Brush Component      { get; }
    internal Brush External       { get; }
    internal Brush Boundary       { get; }
    internal Brush DeploymentNode { get; }

    /// <summary>The fill for a kind, before any external or literal override.</summary>
    internal Brush ForKind(C4ElementKind kind) => kind switch
    {
        C4ElementKind.Person         => Person,
        C4ElementKind.System         => System,
        C4ElementKind.Container      => Container,
        C4ElementKind.Component      => Component,
        C4ElementKind.DeploymentNode => DeploymentNode,
        _                            => System,
    };

    /// <summary>
    /// The three brushes a card is painted with. Resolution order is literal → token → derived
    /// default: a diagram's own <c>$bgColor</c> wins, else the theme's <c>C4*Brush</c>, else the
    /// grading above. The ink is chosen for legibility against the fill it actually shows as, so a
    /// pale <c>$bgColor</c> gets dark text without the author having to say so.
    /// </summary>
    internal (Brush fill, Brush stroke, Brush text) BrushesFor(C4ElementInfo info)
    {
        Brush fill = DiagramBrushes.ParseCss(info.FillColor) is Color literal
            ? DiagramBrushes.Frozen(literal)
            : info.External ? External : ForKind(info.Kind);

        var fillColor = DiagramBrushes.ColorOf(fill, _bg);
        Brush stroke = DiagramBrushes.ParseCss(info.BorderColor) is Color bc
            ? DiagramBrushes.Frozen(bc)
            : DiagramBrushes.Frozen(Shade(fillColor, 1.45));

        // Ink is legibility, not palette: a card fill is a saturated colour of its own, so the
        // theme's Text brush is the wrong answer whenever the two are both dark (a Person card on a
        // light theme) or both light. Choose by the fill's own brightness, as the sequence renderer's
        // OnAccent and the Gantt bars already do.
        Brush text = DiagramBrushes.ParseCss(info.FontColor) is Color fc
            ? DiagramBrushes.Frozen(fc)
            : DiagramBrushes.OnColor(DiagramBrushes.Composite(fillColor, _bg), Ink, Paper);

        return (fill, stroke, text);
    }

    /// <summary>Muted ink for the stereotype line — the card's own text colour, softened. Translucent
    /// rather than pre-blended, so it settles against whatever fill it is drawn over.</summary>
    internal static Brush MetaInk(Brush text) => DiagramBrushes.Tint(text, 0xB4, Colors.Gray);

    /// <summary>
    /// Scales a colour's brightness, keeping its hue — <paramref name="factor"/> below 1 darkens,
    /// above 1 lightens toward white. This is what turns one accent into the C4 grading.
    /// </summary>
    private static Color Shade(Color c, double factor)
    {
        if (factor <= 1)
            return Color.FromRgb(
                (byte)Math.Round(c.R * factor),
                (byte)Math.Round(c.G * factor),
                (byte)Math.Round(c.B * factor));

        double t = Math.Min(1, factor - 1);
        return Color.FromRgb(
            (byte)Math.Round(c.R + (255 - c.R) * t),
            (byte)Math.Round(c.G + (255 - c.G) * t),
            (byte)Math.Round(c.B + (255 - c.B) * t));
    }
}

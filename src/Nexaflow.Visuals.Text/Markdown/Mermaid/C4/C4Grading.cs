using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.C4;

/// <summary>
/// C4's own grading: the bank of colours its cards are drawn in, deepest for the outermost abstraction, with one muted
/// colour for what is somebody else's.
///
/// <para>
/// C4-PlantUML ships a fixed scheme — person <c>#08427b</c>, system <c>#1168bd</c>, container <c>#438dd5</c>, component
/// <c>#85bbf0</c>, external <c>#999999</c> — whose <em>information</em> is the grading and not the particular blues. So the
/// bank is made from the theme's own accent (<see cref="DiagramTone.Shaded"/>) and a theme that wants the canonical scheme
/// names it with the <c>C4*Brush</c> keys. A <c>$bgColor</c> written in the diagram wins over both.
/// </para>
///
/// <para>
/// Which band a card takes is the stages' to say — <c>C4Elements.Banded</c> — so a C4 sequence's lifeline heads and a structural
/// diagram's boxes grade alike, and neither of the two builders holds a colour of its own.
/// </para>
/// </summary>
internal static class C4Grading
{
    /// <summary>The bank, worked out once from the theme a diagram is drawn on.</summary>
    public static DiagramTone Of(StyleFormat palette) => new(palette,
    [
        palette.C4Person ?? DiagramTone.Shaded(palette, 0.55),
        palette.C4System ?? DiagramTone.Shaded(palette, 0.78),
        palette.C4Container ?? DiagramTone.Shaded(palette, 1),
        palette.C4Component ?? DiagramTone.Shaded(palette, 1.35),
        palette.C4DeploymentNode ?? palette.QuoteBg,
        palette.C4External ?? palette.TextMuted,
    ]);

    /// <summary>
    /// What a boundary is drawn in, which is nothing a card is graded by: the theme's own accent, washed. Boundaries nest,
    /// and a wash over a wash deepens, so the levels come apart without any of them being given a colour of its own.
    /// </summary>
    public static System.Windows.Media.Brush Boundary(StyleFormat palette) => palette.C4Boundary ?? palette.Accent;
}

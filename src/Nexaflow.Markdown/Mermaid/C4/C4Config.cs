namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>
/// How big a structural C4 diagram draws itself, on Mermaid's own <c>c4:</c> defaults.
/// </summary>
/// <param name="Widest">How wide a card grows before what is written in it wraps instead — Mermaid's <c>width</c>.</param>
/// <param name="Tallest">And the least deep one is drawn, whatever is written in it — its <c>height</c>.</param>
/// <param name="Apart">Clear air between one card and the next — <c>c4ShapeMargin</c>.</param>
/// <param name="Padded">And between a card's edge and what is written in it — <c>c4ShapePadding</c>.</param>
/// <param name="Framed">Air inside a boundary, round everything it holds — <c>boxMargin</c>.</param>
/// <param name="Across">Air round the whole drawing, across — <c>diagramMarginX</c>.</param>
/// <param name="Padding">And down it — <c>diagramMarginY</c>.</param>
/// <param name="Wraps">Whether what is written in a card wraps to its width rather than running on.</param>
public sealed record C4Metrics(double Widest, double Tallest, double Apart, double Padded, double Framed,
                               double Across, double Padding, bool Wraps)
{
    /// <summary>How wide what is written in a card runs before it wraps.</summary>
    public double Wrapping => this.Wraps ? Math.Max(20, this.Widest - (this.Padded * 2)) : this.Widest * 20;
}

/// <summary>
/// What a C4 block's front matter asks for.
///
/// <para>
/// A <c>C4Sequence</c> is a sequence diagram, so <c>config: sequence:</c> sets everything a sequence diagram's does
/// (<see cref="Sequence.SequenceConfig"/>) and the <c>c4:</c> keys that mean the same thing are read over the top of it,
/// since a reader who wrote a C4 diagram writes C4's names for them. The structural diagrams read the <c>c4:</c> keys alone,
/// as <see cref="C4Metrics"/>.
/// </para>
///
/// <para>
/// <c>c4ShapeInRow</c> and <c>c4BoundaryInRow</c> pack Mermaid's own rows and are read and left alone here: this lays a C4
/// diagram out as the graph it is, so what stands beside what is worked out from what is joined to what.
/// </para>
/// </summary>
public static class C4Config
{
    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static Sequence.SequenceConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static Sequence.SequenceConfig From(MermaidConfig front)
    {
        var config = Sequence.SequenceConfig.From(front);
        var c4 = front.Diagram("c4");

        return config with
        {
            Wraps = c4.Flag("wrap") ?? config.Wraps,
            Widest = c4.Number("width") is { } wide and > 0 ? wide : config.Widest,
            Tallest = c4.Number("height") is { } tall and >= 0 ? tall : config.Tallest,
            Across = c4.Number("diagramMarginX") is { } across and >= 0 ? across : config.Across,
            Downward = c4.Number("diagramMarginY") is { } down and >= 0 ? down : config.Downward,
            Framed = c4.Number("boxMargin") is { } boxed and >= 0 ? boxed : config.Framed,
        };
    }

    /// <summary>And how big a structural C4 diagram draws itself.</summary>
    public static C4Metrics Laid(string? yaml) => Sized(MermaidConfig.Read(yaml));

    public static C4Metrics Sized(MermaidConfig front)
    {
        var c4 = front.Diagram("c4");

        return new C4Metrics(
            Widest: c4.Number("width") is { } wide and > 0 ? wide : 240,
            Tallest: c4.Number("height") is { } tall and >= 0 ? tall : 60,
            Apart: c4.Number("c4ShapeMargin") is { } apart and >= 0 ? apart : 50,
            Padded: c4.Number("c4ShapePadding") is { } padded and >= 0 ? padded : 12,
            Framed: c4.Number("boxMargin") is { } framed and >= 0 ? framed : 10,
            Across: c4.Number("diagramMarginX") is { } across and >= 0 ? across : 50,
            Padding: c4.Number("diagramMarginY") is { } down and >= 0 ? down : 10,
            Wraps: c4.Flag("wrap") ?? true);
    }
}

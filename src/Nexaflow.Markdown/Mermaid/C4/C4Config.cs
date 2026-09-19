namespace Nexaflow.Markdown.Mermaid.C4;

/// <summary>
/// What a C4 sequence's front matter asks for. It is a sequence diagram, so <c>config: sequence:</c> sets everything a
/// sequence diagram's does (<see cref="Sequence.SequenceConfig"/>); the <c>c4:</c> keys that mean the same thing are read
/// over the top of it, since a reader who wrote a C4 diagram writes C4's names for them.
///
/// The rest of the <c>c4:</c> keys — <c>c4ShapeInRow</c>, <c>c4BoundaryInRow</c>, <c>c4ShapeMargin</c> and their kin — are
/// how a structural C4 diagram packs its rows, and a sequence has no rows to pack.
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
}

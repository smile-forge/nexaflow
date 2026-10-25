namespace Nexaflow.Markdown.Mermaid;

/// <summary>The pieces every Mermaid diagram's layout has, whatever the diagram is.</summary>
public static class MermaidPiece
{
    /// <summary>The whole block: its title, its diagram, and what could not be read, top to bottom.</summary>
    public const string Diagram = "Diagram";

    /// <summary>The title set over the diagram.</summary>
    public const string Title = "Title";

    /// <summary>Why part of the block did not draw as itself.</summary>
    public const string Trouble = "Trouble";

    /// <summary>A legend: a row per colour a diagram draws in, saying what it is — see <see cref="DiagramLegend"/>.</summary>
    public const string Legend = "Legend";

    /// <summary>One row of a legend: its swatch, and what it says.</summary>
    public const string Key = "Key";

    /// <summary>The square of colour a legend row explains.</summary>
    public const string Swatch = "Swatch";

    /// <summary>Words set inside a shape — see <see cref="DiagramShapes.Draw"/>.</summary>
    public const string Words = "Words";

    /// <summary>An icon something names, drawn in the glyph it is.</summary>
    public const string Glyph = "Glyph";

    /// <summary>What a shape draws — its outline, filled — which is what a press inside it lands on. See <see cref="DiagramShapes.Draw"/>.</summary>
    public const string Shape = "Shape";

    /// <summary>What a node folds and unfolds by — see <see cref="DiagramChip"/>.</summary>
    public const string Chip = "Chip";

    /// <summary>Another language drawn inside this one's words — see <see cref="DiagramInset"/>.</summary>
    public const string Nested = "Nested";

    /// <summary>What offers the children a node has too many of — see <see cref="DiagramSpill"/>.</summary>
    public const string More = "More";

    /// <summary>What a line draws — an axis's line and ticks — which is what a press near it lands on. See <see cref="DiagramAxis.Draw"/>.</summary>
    public const string Line = "Line";

    /// <summary>A drawing set smaller, because it was still wider than its room once it had reshaped itself to it — see <c>MermaidBuilder.Reduced</c>.</summary>
    public const string Reduced = "Reduced";
}

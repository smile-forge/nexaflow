using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Treemap;

/// <summary>
/// What a treemap's front matter asks for — <c>config: treemap:</c> — with Mermaid's own defaults where it asks for nothing.
/// </summary>
public sealed record TreemapConfig
{
    public static TreemapConfig Default { get; } = new();

    /// <summary>The air between items side by side, and between a section's edge and what is inside it.</summary>
    public double Padding { get; init; } = 10;

    /// <summary>The air round the whole map.</summary>
    public double DiagramPadding { get; init; } = 8;

    /// <summary>Whether each item's value is set in it, and each section's total in its band.</summary>
    public bool ShowValues { get; init; } = true;

    /// <summary>A tenth of how wide the map is drawn, where the room allows it.</summary>
    public double NodeWidth { get; init; } = 100;

    /// <summary>A tenth of how tall the map is drawn, at the width <see cref="NodeWidth"/> makes it.</summary>
    public double NodeHeight { get; init; } = 40;

    /// <summary>How thick an item's outline is.</summary>
    public double BorderWidth { get; init; } = 1;

    /// <summary>How big a value is set.</summary>
    public double ValueFontSize { get; init; } = 12;

    /// <summary>How big a leaf's label is set.</summary>
    public double LabelFontSize { get; init; } = 14;

    /// <summary>How a value is set: a d3 format (<see cref="TreemapValues.Format"/>).</summary>
    public string ValueFormat { get; init; } = TreemapValues.Grouped;

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static TreemapConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    /// <summary>Reads a block's front matter, already read as config.</summary>
    public static TreemapConfig From(MermaidConfig config)
    {
        var treemap = config.Diagram("treemap");

        return new TreemapConfig
        {
            Padding = Math.Max(0, treemap.Number("padding") ?? Default.Padding),
            DiagramPadding = Math.Max(0, treemap.Number("diagramPadding") ?? Default.DiagramPadding),
            ShowValues = treemap.Flag("showValues") ?? Default.ShowValues,
            NodeWidth = Math.Max(10, treemap.Number("nodeWidth") ?? Default.NodeWidth),
            NodeHeight = Math.Max(4, treemap.Number("nodeHeight") ?? Default.NodeHeight),
            BorderWidth = Math.Max(0, treemap.Number("borderWidth") ?? Default.BorderWidth),
            ValueFontSize = Math.Max(4, treemap.Number("valueFontSize") ?? Default.ValueFontSize),
            LabelFontSize = Math.Max(4, treemap.Number("labelFontSize") ?? Default.LabelFontSize),
            ValueFormat = treemap.Value("valueFormat")?.Trim().Trim('\'', '"') is { Length: > 0 } format ? format : Default.ValueFormat,
        };
    }
}

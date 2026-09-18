namespace Nexaflow.Markdown.Mermaid.Flowchart;

/// <summary>
/// What a flowchart's front matter asks for, under <c>config: flowchart:</c>: how far apart the nodes are set, how much clear
/// air the drawing keeps round itself, how wide a label runs before it wraps, and how the lines between nodes are curved.
///
/// <para>
/// Mermaid documents <c>useMaxWidth</c>, <c>htmlLabels</c> and <c>defaultRenderer</c> beside these. The chart is drawn at the
/// size its nodes come to and the document places it, labels are drawn by the layout tree rather than by a browser, and there is
/// one layout here — so none of the three has anything to ask for.
/// </para>
/// </summary>
public sealed record FlowchartConfig
{
    /// <summary>How far apart Mermaid sets two nodes side by side when the front matter asks for nothing.</summary>
    public const double Beside = 50;

    /// <summary>How far apart Mermaid sets one rank of nodes from the next.</summary>
    public const double Along = 50;

    /// <summary>The clear air Mermaid keeps round the whole drawing.</summary>
    public const double Air = 8;

    /// <summary>How wide Mermaid lets a label run before it wraps.</summary>
    public const double Widest = 200;

    public static FlowchartConfig Default { get; } = new();

    /// <summary>How far apart two nodes in the same rank are set.</summary>
    public double NodeSpacing { get; init; } = Beside;

    /// <summary>How far apart one rank is set from the next.</summary>
    public double RankSpacing { get; init; } = Along;

    /// <summary>The clear air round the whole drawing.</summary>
    public double Padding { get; init; } = Air;

    /// <summary>How wide what is written on a node runs before it wraps.</summary>
    public double Wrapping { get; init; } = Widest;

    /// <summary>How much room is left above a subgraph's own blocks for what is written on it.</summary>
    public double TitleMargin { get; init; }

    /// <summary>Whether the line of a link is drawn curved, which is what every curve but <c>linear</c> asks for.</summary>
    public bool Curved { get; init; } = true;

    /// <summary>What the front matter called the curve, as it was written.</summary>
    public string? Curve { get; init; }

    /// <summary>Whether a label wraps of its own accord, which <c>markdownAutoWrap: false</c> turns off.</summary>
    public bool Wraps { get; init; } = true;

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static FlowchartConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static FlowchartConfig From(MermaidConfig config)
    {
        var chart = config.Diagram("flowchart");
        var curve = chart.Value("curve");

        return new FlowchartConfig
        {
            NodeSpacing = chart.Number("nodeSpacing") is { } beside and >= 0 ? beside : Beside,
            RankSpacing = chart.Number("rankSpacing") is { } along and >= 0 ? along : Along,
            Padding = chart.Number("diagramPadding") is { } air and >= 0 ? air : Air,
            Wrapping = chart.Number("wrappingWidth") is { } widest and > 0 ? widest : Widest,
            TitleMargin = chart.Number("subGraphTitleMargin") is { } margin and >= 0 ? margin : 0,
            Curve = curve,
            Curved = !string.Equals(curve, "linear", StringComparison.OrdinalIgnoreCase),
            Wraps = config.Shared.Flag("markdownAutoWrap") ?? true,
        };
    }
}

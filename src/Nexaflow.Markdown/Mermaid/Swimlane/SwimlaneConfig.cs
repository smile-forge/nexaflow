namespace Nexaflow.Markdown.Mermaid.Swimlane;

/// <summary>
/// What a swimlane's front matter asks for, under <c>config: swimlane:</c>.
///
/// <para>
/// A swimlane is a flowchart laid out in lanes, so everything about how it is drawn — how far apart the nodes are set, how much
/// clear air it keeps, how wide a label runs before it wraps, how a line is curved — is the flowchart's own
/// <c>config: flowchart:</c> block, read into <see cref="Chart"/>. This block holds only what the lanes themselves ask for.
/// </para>
/// <para>
/// Mermaid documents <c>layout</c>, <c>look</c>, <c>theme</c>, <c>lineHops</c> and <c>optimizeRanksByCrossings</c> beside these.
/// There is one layout here and the colours are the reader's theme's; a line is drawn where it runs rather than hopping over what
/// it crosses; and the last is a knob on a layering pass this does not have — so none of the five has anything to ask for.
/// </para>
/// </summary>
public sealed record SwimlaneConfig
{
    public static SwimlaneConfig Default { get; } = new();

    /// <summary>What a swimlane shares with every flowchart, which is everything but the lanes.</summary>
    public Flowchart.FlowchartConfig Chart { get; init; } = Flowchart.FlowchartConfig.Default;

    /// <summary>
    /// Whether a link handed from one lane to another goes sideways: it leaves what it reaches where that lane's own work has got
    /// to, rather than holding it a rank further on as a link inside one lane does. <c>ignoreCrossLaneEdges: false</c> asks for the
    /// other, where a handoff counts like any other link.
    /// </summary>
    public bool Sideways { get; init; } = true;

    /// <summary>
    /// Whether the lanes are set across in an order worked out to keep the handoffs between them short, rather than the order they
    /// are written in — <c>automaticLaneOrdering</c>, which is off because the order lanes are written in usually means something.
    /// </summary>
    public bool Ordered { get; init; }

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static SwimlaneConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static SwimlaneConfig From(MermaidConfig config)
    {
        var lanes = config.Diagram("swimlane");

        return new SwimlaneConfig
        {
            Chart = Flowchart.FlowchartConfig.From(config),
            Sideways = lanes.Flag("ignoreCrossLaneEdges") ?? true,
            Ordered = lanes.Flag("automaticLaneOrdering") ?? false,
        };
    }
}

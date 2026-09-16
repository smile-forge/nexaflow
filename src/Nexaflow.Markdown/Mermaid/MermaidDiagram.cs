namespace Nexaflow.Markdown.Mermaid;

/// <summary>Which of Mermaid's diagram types a block is, as its header names it.</summary>
public enum MermaidDiagram
{
    /// <summary>A header naming no type this reads.</summary>
    Unknown,

    /// <summary><c>flowchart</c> or <c>graph</c> — and a block with no header at all.</summary>
    Flowchart,
    Pie,
    Quadrant,
    Sequence,
    Gantt,
    GitGraph,
    Mindmap,
    State,
    Class,
    Requirement,
    Kanban,
    XyChart,
    Radar,
    Ishikawa,
    Sankey,
    Er,
    Venn,
    Cynefin,
    Architecture,
    Swimlane,
    Timeline,
    Journey,
    Block,

    /// <summary><c>C4Context</c>, <c>C4Container</c>, <c>C4Component</c>, <c>C4Dynamic</c>, <c>C4Deployment</c>.</summary>
    C4,
    C4Sequence,
}

/// <summary>The keywords that name each <see cref="MermaidDiagram"/>.</summary>
public static class MermaidDiagrams
{
    /// <summary>
    /// The diagram a header keyword names, ignoring case.
    ///
    /// <para>
    /// Most types are named by a prefix, because Mermaid spells one type several ways — <c>stateDiagram</c> and
    /// <c>stateDiagram-v2</c>, <c>xychart</c> and <c>xychart-beta</c> — and the rest by the whole word, where a longer
    /// word is a different type: <c>flowchart-elk</c> is a layout this does not have, not a flowchart.
    /// </para>
    /// <para>
    /// No keyword at all is a flowchart, which is what a block that has not had its header typed yet has always drawn as.
    /// </para>
    /// </summary>
    public static MermaidDiagram Named(string? keyword)
    {
        if (string.IsNullOrEmpty(keyword)) return MermaidDiagram.Flowchart;

        var word = keyword.ToLowerInvariant();

        foreach (var (prefix, diagram) in Prefixes)
            if (word.StartsWith(prefix, StringComparison.Ordinal)) return diagram;

        return word switch
        {
            "pie" => MermaidDiagram.Pie,
            "quadrantchart" => MermaidDiagram.Quadrant,
            "sequencediagram" => MermaidDiagram.Sequence,
            "gantt" => MermaidDiagram.Gantt,
            "mindmap" => MermaidDiagram.Mindmap,
            "kanban" => MermaidDiagram.Kanban,
            "graph" or "flowchart" => MermaidDiagram.Flowchart,
            "timeline" => MermaidDiagram.Timeline,
            "journey" => MermaidDiagram.Journey,
            "c4sequence" => MermaidDiagram.C4Sequence,
            "c4context" or "c4container" or "c4component" or "c4dynamic" or "c4deployment" => MermaidDiagram.C4,
            _ => MermaidDiagram.Unknown,
        };
    }

    /// <summary>
    /// What this type reads beyond the lines every type shares, or nothing where it has no grammar of its own yet — see
    /// <see cref="IMermaidGrammar"/>.
    /// </summary>
    public static IMermaidGrammar? Grammar(MermaidDiagram diagram) => diagram switch
    {
        MermaidDiagram.Pie => Pies,
        MermaidDiagram.Venn => Venns,
        MermaidDiagram.Radar => Radars,
        _ => null,
    };

    private static readonly Pie.PieGrammar Pies = new();

    private static readonly Venn.VennGrammar Venns = new();

    private static readonly Radar.RadarGrammar Radars = new();

    /// <summary>The types named by how their keyword starts, in the order they are tried.</summary>
    private static readonly (string Prefix, MermaidDiagram Diagram)[] Prefixes =
    [
        ("gitgraph", MermaidDiagram.GitGraph),
        ("statediagram", MermaidDiagram.State),
        ("classdiagram", MermaidDiagram.Class),
        ("requirementdiagram", MermaidDiagram.Requirement),
        ("xychart", MermaidDiagram.XyChart),
        ("radar", MermaidDiagram.Radar),
        ("ishikawa", MermaidDiagram.Ishikawa),
        ("sankey", MermaidDiagram.Sankey),
        ("erdiagram", MermaidDiagram.Er),
        ("venn", MermaidDiagram.Venn),
        ("cynefin", MermaidDiagram.Cynefin),
        ("architecture", MermaidDiagram.Architecture),
        ("swimlane", MermaidDiagram.Swimlane),
        ("block", MermaidDiagram.Block),
    ];
}

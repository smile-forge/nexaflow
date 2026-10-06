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
        MermaidDiagram.XyChart => XyCharts,
        MermaidDiagram.Quadrant => Quadrants,
        MermaidDiagram.Ishikawa => Ishikawas,
        MermaidDiagram.Gantt => Gantts,
        MermaidDiagram.Kanban => Kanbans,
        MermaidDiagram.Mindmap => Mindmaps,
        MermaidDiagram.Cynefin => Cynefins,
        MermaidDiagram.Timeline => Timelines,
        MermaidDiagram.Journey => Journeys,
        MermaidDiagram.GitGraph => Gits,
        MermaidDiagram.Block => Blocks,
        MermaidDiagram.Architecture => Architectures,
        MermaidDiagram.Sankey => Sankeys,
        MermaidDiagram.State => States,
        MermaidDiagram.Class => Classes,
        MermaidDiagram.Requirement => Requirements,
        MermaidDiagram.Er => Ers,
        MermaidDiagram.Sequence => Sequences,
        MermaidDiagram.C4Sequence => C4Sequences,
        MermaidDiagram.C4 => C4s,
        // A swimlane is a flowchart laid out in lanes, and Mermaid reads the two with one parser: its top-level subgraphs
        // are the lanes, and everything else about it is a flowchart's. Only its builder differs.
        MermaidDiagram.Flowchart or MermaidDiagram.Swimlane => Flowcharts,
        _ => null,
    };

    /// <summary>
    /// What reads a block of this type: the diagram's own parser where it has one, and the shared <see cref="MermaidParser"/>
    /// otherwise. A flowchart and a swimlane have their own, because a quoted label runs past the end of the line it starts
    /// on, and a reader handed one line at a time cannot see that.
    ///
    /// <para>
    /// This is the only place that choice is made. The shipped language reads a diagram with what this gives, and so do the
    /// tests that hold a parser to printing back what was written, so a diagram taking up a parser of its own is read by it
    /// everywhere at once rather than in the places somebody remembered.
    /// </para>
    /// </summary>
    public static Func<string?, Ast.ContentNode> ParserFor(MermaidDiagram diagram) =>
        diagram is MermaidDiagram.Flowchart or MermaidDiagram.Swimlane
            ? Flowchart.FlowchartParser.Parse
            : MermaidParser.Parse;

    /// <summary>
    /// What writes back into a block of this type: the diagram's own transpiler where it has one, and the shared
    /// <see cref="MermaidParser"/> otherwise.
    ///
    /// <para>
    /// A diagram owns this as soon as a name in it has to be spelled a particular way for the rest of the block to still
    /// read, which is every diagram whose lines reach a thing by an id. Owning a transpiler is separate from owning a
    /// reader: a diagram may need one, the other, or both.
    /// </para>
    /// </summary>
    public static Func<Editing.ContentChange, Editing.ContentChange?> TranspilerFor(MermaidDiagram diagram) => diagram switch
    {
        MermaidDiagram.Flowchart or MermaidDiagram.Swimlane => Editing.Transpiles.By<Flowchart.FlowchartParser>(),
        MermaidDiagram.State => Editing.Transpiles.By<State.StateParser>(),
        _ => Editing.Transpiles.By<MermaidParser>(),
    };

    private static readonly Pie.PieGrammar Pies = new();

    private static readonly Flowchart.FlowchartGrammar Flowcharts = new();

    private static readonly Venn.VennGrammar Venns = new();

    private static readonly Radar.RadarGrammar Radars = new();

    private static readonly Xy.XyGrammar XyCharts = new();

    private static readonly Quadrant.QuadrantGrammar Quadrants = new();

    private static readonly Ishikawa.IshikawaGrammar Ishikawas = new();

    private static readonly Gantt.GanttGrammar Gantts = new();

    private static readonly Kanban.KanbanGrammar Kanbans = new();

    private static readonly Mindmap.MindmapGrammar Mindmaps = new();

    private static readonly Cynefin.CynefinGrammar Cynefins = new();

    private static readonly Timeline.TimelineGrammar Timelines = new();

    private static readonly Journey.JourneyGrammar Journeys = new();

    private static readonly Git.GitGrammar Gits = new();

    private static readonly Block.BlockGrammar Blocks = new();

    private static readonly Architecture.ArchitectureGrammar Architectures = new();

    private static readonly Sankey.SankeyGrammar Sankeys = new();

    private static readonly State.StateGrammar States = new();

    private static readonly Class.ClassGrammar Classes = new();

    private static readonly Requirement.RequirementGrammar Requirements = new();

    private static readonly Er.ErGrammar Ers = new();

    /// <summary>A sequence diagram, which a C4 sequence is Mermaid's own reading of.</summary>
    private static readonly Sequence.SequenceGrammar Sequences = new();

    /// <summary>A C4 sequence, which is a sequence diagram written in C4-PlantUML's words.</summary>
    private static readonly C4.C4Grammar C4s = new();
    private static readonly C4.C4SequenceGrammar C4Sequences = new();

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

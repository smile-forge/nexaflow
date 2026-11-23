using Nexaflow.Markdown.Layout.Barcode;
using Nexaflow.Markdown.Layout.Chemistry;
using Nexaflow.Markdown.Layout.Code;
using Nexaflow.Markdown.Layout.Maths;
using Nexaflow.Markdown.Layout.Matrix;
using Nexaflow.Markdown.Layout.Mermaid;
using Nexaflow.Markdown.Layout.Mermaid.Architecture;
using Nexaflow.Markdown.Layout.Mermaid.Block;
using Nexaflow.Markdown.Layout.Mermaid.C4;
using Nexaflow.Markdown.Layout.Mermaid.Class;
using Nexaflow.Markdown.Layout.Mermaid.Cynefin;
using Nexaflow.Markdown.Layout.Mermaid.Er;
using Nexaflow.Markdown.Layout.Mermaid.Flowchart;
using Nexaflow.Markdown.Layout.Mermaid.Gantt;
using Nexaflow.Markdown.Layout.Mermaid.Git;
using Nexaflow.Markdown.Layout.Mermaid.Ishikawa;
using Nexaflow.Markdown.Layout.Mermaid.Journey;
using Nexaflow.Markdown.Layout.Mermaid.Kanban;
using Nexaflow.Markdown.Layout.Mermaid.Mindmap;
using Nexaflow.Markdown.Layout.Mermaid.Pie;
using Nexaflow.Markdown.Layout.Mermaid.Quadrant;
using Nexaflow.Markdown.Layout.Mermaid.Radar;
using Nexaflow.Markdown.Layout.Mermaid.Requirement;
using Nexaflow.Markdown.Layout.Mermaid.Sankey;
using Nexaflow.Markdown.Layout.Mermaid.Sequence;
using Nexaflow.Markdown.Layout.Mermaid.State;
using Nexaflow.Markdown.Layout.Mermaid.Swimlane;
using Nexaflow.Markdown.Layout.Mermaid.Timeline;
using Nexaflow.Markdown.Layout.Mermaid.Venn;
using Nexaflow.Markdown.Layout.Mermaid.Xy;
using Nexaflow.Markdown.Layout.Nomnoml;
using Nexaflow.Markdown.Layout.Packet;
using Nexaflow.Markdown.Layout.Plot;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Layout.Scores;
using Nexaflow.Markdown.Layout.PlainText;
using Nexaflow.Markdown.Layout.Treemap;
using Nexaflow.Markdown.Layout.TreeView;
using Nexaflow.Markdown.Layout.WordCloud;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Layout.EventModeling;

namespace Nexaflow.Markdown.Layout;

/// <summary>
/// What lays out each kind of content: the builder a language names, which whatever hosts the content makes — a language only names
/// its builder, and every builder is made from the same six things (<see cref="ContentBuilder"/>).
///
/// <para>
/// A diagram is named here once its grammar is named in <see cref="MermaidDiagrams.Grammar"/> — the two lists are the same list,
/// which the tests hold them to.
/// </para>
/// </summary>
public static class ContentLayouts
{
    /// <summary>A markdown document, and a rich-text one read into the same blocks.</summary>
    public static Type Markdown => typeof(MarkdownBuilder);

    /// <summary>A <c>mermaid</c> block, drawn as the diagram its header names.</summary>
    public static Type MermaidFence => typeof(MermaidFenceBuilder);

    public static Type Nomnoml => typeof(NomnomlBuilder);

    /// <summary>A packet, or a register: its fields drawn as boxes along rows of bits.</summary>
    public static Type Packet => typeof(PacketBuilder);

    /// <summary>A sankey: its flows drawn as ribbons as thick as they are worth, between bars for the nodes they join.</summary>
    public static Type Sankey => typeof(SankeyBuilder);

    /// <summary>A Venn diagram: its sets drawn as circles sized by area, the regions where they overlap, and what is written in each.</summary>
    public static Type Venn => typeof(VennBuilder);

    /// <summary>A quadrant chart: its four quadrants and their captions, its axes' ends, and its points where they stand.</summary>
    public static Type Quadrant => typeof(QuadrantBuilder);

    /// <summary>A timeline: its periods along a spine, across the page or down it, each with its events, its sections banding them.</summary>
    public static Type Timeline => typeof(TimelineBuilder);

    /// <summary>A pie chart: its slices as wedges, clockwise in the order written, with a legend.</summary>
    public static Type Pie => typeof(PieBuilder);

    /// <summary>A radar chart: its axes as spokes, its curves reaching along them, over a graticule.</summary>
    public static Type Radar => typeof(RadarBuilder);

    /// <summary>An XY chart: bars and lines over its axes, either way up.</summary>
    public static Type XyChart => typeof(XyBuilder);

    /// <summary>An Ishikawa diagram: the event at the head of a spine, its causes branching into it.</summary>
    public static Type Ishikawa => typeof(IshikawaBuilder);

    /// <summary>A class diagram: its classes as boxes of members, the relations between them, namespaces boxing them.</summary>
    public static Type Class => typeof(ClassBuilder);

    /// <summary>An architecture diagram: services and junctions in their groups, laid out by the sides their edges leave by.</summary>
    public static Type Architecture => typeof(ArchitectureBuilder);

    /// <summary>A structural C4 diagram: its elements as cards inside their boundaries, and the relationships between them.</summary>
    public static Type C4 => typeof(C4Builder);

    /// <summary>A C4 sequence: its elements as participants, and the relationships between them down a timeline.</summary>
    public static Type C4Sequence => typeof(C4SequenceBuilder);

    /// <summary>A sequence diagram: its participants along the top, and the messages between their lifelines down the page.</summary>
    public static Type Sequence => typeof(SequenceBuilder);

    /// <summary>A user journey: its tasks in sections along a line, each a face scored up and down, its actors keyed below.</summary>
    public static Type Journey => typeof(JourneyBuilder);

    /// <summary>A Cynefin framework: the five domains in their grid, the items placed in each, and the transitions between them.</summary>
    public static Type Cynefin => typeof(CynefinBuilder);

    /// <summary>A mindmap: its root, and the ideas branching off it in a tidy tree, each in the shape its brackets say.</summary>
    public static Type Mindmap => typeof(MindmapBuilder);

    /// <summary>A kanban board: its columns side by side, the cards in each stacked under its title.</summary>
    public static Type Kanban => typeof(KanbanBuilder);

    /// <summary>A flowchart: its nodes ranked the way it runs, the links between them, and its subgraphs boxed round what they hold.</summary>
    public static Type Flowchart => typeof(FlowchartBuilder);

    /// <summary>A swimlane: a flowchart laid out in lanes, its outermost subgraphs the lanes.</summary>
    public static Type Swimlane => typeof(SwimlaneBuilder);

    /// <summary>An entity relationship diagram: each entity a box of its attributes in columns, crow's feet where its relationships meet it.</summary>
    public static Type Er => typeof(ErBuilder);

    /// <summary>A block diagram: the grid its author laid out, the composites nested in it and the links between its blocks.</summary>
    public static Type Block => typeof(BlockBuilder);

    /// <summary>A state diagram: its states and the transitions between them, composite states boxed round their own, and the notes beside them.</summary>
    public static Type State => typeof(StateBuilder);

    /// <summary>A requirement diagram: each requirement and element a box of its fields, and the SysML relations between them.</summary>
    public static Type Requirement => typeof(RequirementBuilder);

    /// <summary>A gantt chart: its tasks as bars along the days they run, in their sections, under an axis of dates.</summary>
    public static Type Gantt => typeof(GanttBuilder);

    /// <summary>A git graph: its commits along the lanes of the branches they are made on, merges and cherry-picks joining them.</summary>
    public static Type GitGraph => typeof(GitBuilder);

    /// <summary>A treemap: its items drawn as tiles as big as their shares, a section's holding the items in it.</summary>
    public static Type Treemap => typeof(TreemapBuilder);

    /// <summary>A tree view: its nodes drawn as rows set in by how deep they stand, joined by lines from each folder.</summary>
    public static Type TreeView => typeof(TreeViewBuilder);

    /// <summary>A two-dimensional code — QR, Data Matrix, PDF417, Aztec — drawn as its modules.</summary>
    public static Type Matrix => typeof(MatrixBuilder);

    public static Type Barcode => typeof(BarcodeBuilder);

    /// <summary>A molecule written as SMILES.</summary>
    public static Type Smiles => typeof(SmilesBuilder);

    /// <summary>A formula written in LaTeX.</summary>
    public static Type Latex => typeof(LatexBuilder);

    /// <summary>A tune written in ABC.</summary>
    public static Type Abc => typeof(AbcBuilder);

    /// <summary>A tune written in LilyPond.</summary>
    public static Type LilyPond => typeof(LilyPondBuilder);

    /// <summary>A scatter, bubble, heatmap or density plot.</summary>
    public static Type Plot => typeof(PlotBuilder);

    public static Type WordCloud => typeof(WordCloudBuilder);

    /// <summary>An event model: frames along a timeline in swimlanes.</summary>
    public static Type EventModeling => typeof(EventModelingBuilder);

    /// <summary>Code, coloured as its grammar reads it.</summary>
    public static Type Code => typeof(CodeBuilder);

    /// <summary>Plain text, laid line by line.</summary>
    public static Type Text => typeof(TextBuilder);

    /// <summary>Content nothing could read, shown as it is written with why beneath it.</summary>
    public static Type Unread => typeof(UnreadBuilder);
}

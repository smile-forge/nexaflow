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
using Nexaflow.Markdown.Layout.Plot;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Layout.Scores;
using Nexaflow.Markdown.Layout.WordCloud;
using Nexaflow.Markdown.Mermaid;

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

    /// <summary>Code, coloured as its grammar reads it.</summary>
    public static Type Code => typeof(CodeBuilder);

    /// <summary>Content nothing could read, shown as it is written with why beneath it.</summary>
    public static Type Unread => typeof(UnreadBuilder);

    /// <summary>The builder a diagram is drawn by, or null for one not drawn on the layout tree.</summary>
    public static Type? Diagram(MermaidDiagram diagram) => diagram switch
    {
        MermaidDiagram.Pie => typeof(PieBuilder),
        MermaidDiagram.Venn => typeof(VennBuilder),
        MermaidDiagram.Radar => typeof(RadarBuilder),
        MermaidDiagram.XyChart => typeof(XyBuilder),
        MermaidDiagram.Quadrant => typeof(QuadrantBuilder),
        MermaidDiagram.Ishikawa => typeof(IshikawaBuilder),
        MermaidDiagram.Gantt => typeof(GanttBuilder),
        MermaidDiagram.Kanban => typeof(KanbanBuilder),
        MermaidDiagram.Mindmap => typeof(MindmapBuilder),
        MermaidDiagram.Cynefin => typeof(CynefinBuilder),
        MermaidDiagram.Timeline => typeof(TimelineBuilder),
        MermaidDiagram.Journey => typeof(JourneyBuilder),
        MermaidDiagram.GitGraph => typeof(GitBuilder),
        MermaidDiagram.Block => typeof(BlockBuilder),
        MermaidDiagram.Architecture => typeof(ArchitectureBuilder),
        MermaidDiagram.Sankey => typeof(SankeyBuilder),
        MermaidDiagram.Flowchart => typeof(FlowchartBuilder),
        MermaidDiagram.Swimlane => typeof(SwimlaneBuilder),
        MermaidDiagram.State => typeof(StateBuilder),
        MermaidDiagram.Class => typeof(ClassBuilder),
        MermaidDiagram.Requirement => typeof(RequirementBuilder),
        MermaidDiagram.Er => typeof(ErBuilder),
        MermaidDiagram.Sequence => typeof(SequenceBuilder),
        MermaidDiagram.C4Sequence => typeof(C4SequenceBuilder),
        MermaidDiagram.C4 => typeof(C4Builder),
        _ => null,
    };
}

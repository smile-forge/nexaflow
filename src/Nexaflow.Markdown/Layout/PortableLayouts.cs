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
/// its builder, and every builder is made from the same six things (<see cref="PortableContentBuilder"/>).
///
/// <para>
/// A diagram is named here once its grammar is named in <see cref="MermaidDiagrams.Grammar"/> — the two lists are the same list,
/// which the tests hold them to.
/// </para>
/// </summary>
public static class PortableLayouts
{
    /// <summary>A markdown document, and a rich-text one read into the same blocks.</summary>
    public static Type Markdown => typeof(PortableMarkdownBuilder);

    /// <summary>A <c>mermaid</c> block, drawn as the diagram its header names.</summary>
    public static Type MermaidFence => typeof(PortableMermaidFenceBuilder);

    public static Type Nomnoml => typeof(PortableNomnomlBuilder);

    /// <summary>A two-dimensional code — QR, Data Matrix, PDF417, Aztec — drawn as its modules.</summary>
    public static Type Matrix => typeof(PortableMatrixBuilder);

    public static Type Barcode => typeof(PortableBarcodeBuilder);

    /// <summary>A molecule written as SMILES.</summary>
    public static Type Smiles => typeof(PortableSmilesBuilder);

    /// <summary>A formula written in LaTeX.</summary>
    public static Type Latex => typeof(PortableLatexBuilder);

    /// <summary>A tune written in ABC.</summary>
    public static Type Abc => typeof(PortableAbcBuilder);

    /// <summary>A tune written in LilyPond.</summary>
    public static Type LilyPond => typeof(PortableLilyPondBuilder);

    /// <summary>A scatter, bubble, heatmap or density plot.</summary>
    public static Type Plot => typeof(PortablePlotBuilder);

    public static Type WordCloud => typeof(PortableWordCloudBuilder);

    /// <summary>Code, coloured as its grammar reads it.</summary>
    public static Type Code => typeof(PortableCodeBuilder);

    /// <summary>Content nothing could read, shown as it is written with why beneath it.</summary>
    public static Type Unread => typeof(PortableUnreadBuilder);

    /// <summary>The builder a diagram is drawn by, or null for one not drawn on the layout tree.</summary>
    public static Type? Diagram(MermaidDiagram diagram) => diagram switch
    {
        MermaidDiagram.Pie => typeof(PortablePieBuilder),
        MermaidDiagram.Venn => typeof(PortableVennBuilder),
        MermaidDiagram.Radar => typeof(PortableRadarBuilder),
        MermaidDiagram.XyChart => typeof(PortableXyBuilder),
        MermaidDiagram.Quadrant => typeof(PortableQuadrantBuilder),
        MermaidDiagram.Ishikawa => typeof(PortableIshikawaBuilder),
        MermaidDiagram.Gantt => typeof(PortableGanttBuilder),
        MermaidDiagram.Kanban => typeof(PortableKanbanBuilder),
        MermaidDiagram.Mindmap => typeof(PortableMindmapBuilder),
        MermaidDiagram.Cynefin => typeof(PortableCynefinBuilder),
        MermaidDiagram.Timeline => typeof(PortableTimelineBuilder),
        MermaidDiagram.Journey => typeof(PortableJourneyBuilder),
        MermaidDiagram.GitGraph => typeof(PortableGitBuilder),
        MermaidDiagram.Block => typeof(PortableBlockBuilder),
        MermaidDiagram.Architecture => typeof(PortableArchitectureBuilder),
        MermaidDiagram.Sankey => typeof(PortableSankeyBuilder),
        MermaidDiagram.Flowchart => typeof(PortableFlowchartBuilder),
        MermaidDiagram.Swimlane => typeof(PortableSwimlaneBuilder),
        MermaidDiagram.State => typeof(PortableStateBuilder),
        MermaidDiagram.Class => typeof(PortableClassBuilder),
        MermaidDiagram.Requirement => typeof(PortableRequirementBuilder),
        MermaidDiagram.Er => typeof(PortableErBuilder),
        MermaidDiagram.Sequence => typeof(PortableSequenceBuilder),
        MermaidDiagram.C4Sequence => typeof(PortableC4SequenceBuilder),
        MermaidDiagram.C4 => typeof(PortableC4Builder),
        _ => null,
    };
}

using System;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Pie;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Quadrant;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Radar;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Venn;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Xy;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// The diagrams drawn on the shared layout tree, and the builder each is drawn by.
///
/// <para>
/// What the Mermaid language makes its builder from (<see cref="Languages.Shipped.Mermaid"/>), once the block's header has
/// named its diagram. A diagram is named here once its grammar is named in <see cref="MermaidDiagrams.Grammar"/> — the two
/// lists are the same list, which the tests hold them to.
/// </para>
/// </summary>
internal static class MermaidBuilders
{
    /// <summary>
    /// Makes the builder a diagram is drawn by, from the five things every builder is made from — which only the engine hands
    /// over (<see cref="ContentEngine"/>).
    /// </summary>
    public delegate Editing.ContentBuilder Make(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting);

    /// <summary>What draws a block whose header names no diagram: the block as written, with the reason.</summary>
    public static Make Unknown { get; } = static (r, s, f, o, n) => new UnknownDiagramBuilder(r, s, f, o, n);

    /// <summary>The builder a diagram is drawn by, or null for one not drawn on the shared tree.</summary>
    public static Make? For(MermaidDiagram diagram) => diagram switch
    {
        MermaidDiagram.Pie => static (r, s, f, o, n) => new PieBuilder(r, s, f, o, n),
        MermaidDiagram.Venn => static (r, s, f, o, n) => new VennBuilder(r, s, f, o, n),
        MermaidDiagram.Radar => static (r, s, f, o, n) => new RadarBuilder(r, s, f, o, n),
        MermaidDiagram.XyChart => static (r, s, f, o, n) => new XyBuilder(r, s, f, o, n),
        MermaidDiagram.Quadrant => static (r, s, f, o, n) => new QuadrantBuilder(r, s, f, o, n),
        MermaidDiagram.Ishikawa => static (r, s, f, o, n) => new Ishikawa.IshikawaBuilder(r, s, f, o, n),
        MermaidDiagram.Gantt => static (r, s, f, o, n) => new Gantt.GanttBuilder(r, s, f, o, n),
        MermaidDiagram.Kanban => static (r, s, f, o, n) => new Kanban.KanbanBuilder(r, s, f, o, n),
        MermaidDiagram.Mindmap => static (r, s, f, o, n) => new Mindmap.MindmapBuilder(r, s, f, o, n),
        MermaidDiagram.Cynefin => static (r, s, f, o, n) => new Cynefin.CynefinBuilder(r, s, f, o, n),
        MermaidDiagram.Timeline => static (r, s, f, o, n) => new Timeline.TimelineBuilder(r, s, f, o, n),
        MermaidDiagram.Journey => static (r, s, f, o, n) => new Journey.JourneyBuilder(r, s, f, o, n),
        MermaidDiagram.GitGraph => static (r, s, f, o, n) => new Git.GitBuilder(r, s, f, o, n),
        MermaidDiagram.Block => static (r, s, f, o, n) => new Block.BlockBuilder(r, s, f, o, n),
        MermaidDiagram.Architecture => static (r, s, f, o, n) => new Architecture.ArchitectureBuilder(r, s, f, o, n),
        MermaidDiagram.Sankey => static (r, s, f, o, n) => new Sankey.SankeyBuilder(r, s, f, o, n),
        MermaidDiagram.Flowchart => static (r, s, f, o, n) => new Flowchart.FlowchartBuilder(r, s, f, o, n),
        MermaidDiagram.Swimlane => static (r, s, f, o, n) => new Swimlane.SwimlaneBuilder(r, s, f, o, n),
        MermaidDiagram.State => static (r, s, f, o, n) => new State.StateBuilder(r, s, f, o, n),
        MermaidDiagram.Class => static (r, s, f, o, n) => new Class.ClassBuilder(r, s, f, o, n),
        MermaidDiagram.Requirement => static (r, s, f, o, n) => new Requirement.RequirementBuilder(r, s, f, o, n),
        MermaidDiagram.Er => static (r, s, f, o, n) => new Er.ErBuilder(r, s, f, o, n),
        MermaidDiagram.Sequence => static (r, s, f, o, n) => new Sequence.SequenceBuilder(r, s, f, o, n),
        MermaidDiagram.C4Sequence => static (r, s, f, o, n) => new C4.C4SequenceBuilder(r, s, f, o, n),
        MermaidDiagram.C4 => static (r, s, f, o, n) => new C4.C4Builder(r, s, f, o, n),
        _ => null,
    };
}

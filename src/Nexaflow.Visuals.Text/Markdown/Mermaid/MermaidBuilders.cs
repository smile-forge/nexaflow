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
/// Every diagram is a language of its own (<see cref="Languages.Shipped.Diagrams"/>), told apart by the builder named here, which
/// the engine makes. A diagram is named here once its grammar is named in <see cref="MermaidDiagrams.Grammar"/> — the two lists
/// are the same list, which the tests hold them to.
/// </para>
/// </summary>
internal static class MermaidBuilders
{
    /// <summary>The builder a diagram is drawn by, or null for one not drawn on the shared tree.</summary>
    public static Type? For(MermaidDiagram diagram) => diagram switch
    {
        MermaidDiagram.Pie => typeof(PieBuilder),
        MermaidDiagram.Venn => typeof(VennBuilder),
        MermaidDiagram.Radar => typeof(RadarBuilder),
        MermaidDiagram.XyChart => typeof(XyBuilder),
        MermaidDiagram.Quadrant => typeof(QuadrantBuilder),
        MermaidDiagram.Ishikawa => typeof(Ishikawa.IshikawaBuilder),
        MermaidDiagram.Gantt => typeof(Gantt.GanttBuilder),
        MermaidDiagram.Kanban => typeof(Kanban.KanbanBuilder),
        MermaidDiagram.Mindmap => typeof(Mindmap.MindmapBuilder),
        MermaidDiagram.Cynefin => typeof(Cynefin.CynefinBuilder),
        MermaidDiagram.Timeline => typeof(Timeline.TimelineBuilder),
        MermaidDiagram.Journey => typeof(Journey.JourneyBuilder),
        MermaidDiagram.GitGraph => typeof(Git.GitBuilder),
        MermaidDiagram.Block => typeof(Block.BlockBuilder),
        MermaidDiagram.Architecture => typeof(Architecture.ArchitectureBuilder),
        MermaidDiagram.Sankey => typeof(Sankey.SankeyBuilder),
        MermaidDiagram.Flowchart => typeof(Flowchart.FlowchartBuilder),
        MermaidDiagram.Swimlane => typeof(Swimlane.SwimlaneBuilder),
        MermaidDiagram.State => typeof(State.StateBuilder),
        MermaidDiagram.Class => typeof(Class.ClassBuilder),
        MermaidDiagram.Requirement => typeof(Requirement.RequirementBuilder),
        MermaidDiagram.Er => typeof(Er.ErBuilder),
        MermaidDiagram.Sequence => typeof(Sequence.SequenceBuilder),
        MermaidDiagram.C4Sequence => typeof(C4.C4SequenceBuilder),
        MermaidDiagram.C4 => typeof(C4.C4Builder),
        _ => null,
    };
}

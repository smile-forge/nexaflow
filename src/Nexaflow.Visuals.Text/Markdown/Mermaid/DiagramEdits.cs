using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// What an edit means in each diagram: every diagram has a handler of its own, told the edits that land in it
/// (<see cref="IOnEdit"/>). A swimlane is a flowchart laid out in lanes, written with the same lines, so the one handler serves both.
/// </summary>
internal static class DiagramEdits
{
    /// <summary>The handler a diagram's edits are told to, or null for one with nothing to say.</summary>
    public static IOnEdit? For(MermaidDiagram diagram) => diagram switch
    {
        MermaidDiagram.Pie => new Pie.PieEdits(),
        MermaidDiagram.Venn => new Venn.VennEdits(),
        MermaidDiagram.Radar => new Radar.RadarEdits(),
        MermaidDiagram.XyChart => new Xy.XyEdits(),
        MermaidDiagram.Quadrant => new Quadrant.QuadrantEdits(),
        MermaidDiagram.Ishikawa => new Ishikawa.IshikawaEdits(),
        MermaidDiagram.Gantt => new Gantt.GanttEdits(),
        MermaidDiagram.Kanban => new Kanban.KanbanEdits(),
        MermaidDiagram.Mindmap => new Mindmap.MindmapEdits(),
        MermaidDiagram.Cynefin => new Cynefin.CynefinEdits(),
        MermaidDiagram.Timeline => new Timeline.TimelineEdits(),
        MermaidDiagram.Journey => new Journey.JourneyEdits(),
        MermaidDiagram.GitGraph => new Git.GitEdits(),
        MermaidDiagram.Block => new Block.BlockEdits(),
        MermaidDiagram.Architecture => new Architecture.ArchitectureEdits(),
        MermaidDiagram.Sankey => new Sankey.SankeyEdits(),
        MermaidDiagram.Flowchart or MermaidDiagram.Swimlane => new Flowchart.FlowchartEdits(),
        MermaidDiagram.State => new State.StateEdits(),
        MermaidDiagram.Class => new Class.ClassEdits(),
        MermaidDiagram.Requirement => new Requirement.RequirementEdits(),
        MermaidDiagram.Er => new Er.ErEdits(),
        MermaidDiagram.Sequence => new Sequence.SequenceEdits(),
        MermaidDiagram.C4Sequence => new C4.C4SequenceEdits(),
        MermaidDiagram.C4 => new C4.C4Edits(),
        _ => null,
    };
}

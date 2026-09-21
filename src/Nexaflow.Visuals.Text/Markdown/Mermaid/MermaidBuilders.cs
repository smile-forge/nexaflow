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
/// What <c>MermaidDiagramHandler</c> asks first: a diagram named here is shown in an element it can be selected and written
/// in, and never reaches a drawing made any other way. A diagram is named here once its grammar is named in
/// <see cref="MermaidDiagrams.Grammar"/> — the two lists are the same list, which the tests hold them to.
/// </para>
/// </summary>
internal static class MermaidBuilders
{
    /// <summary>Draws a block that has already been read, for everything the drawing depends on but the block itself.</summary>
    public delegate Laid Build(ContentReading reading, DiagramLaying laying);

    /// <summary>
    /// A block read: parsed by its grammar, worked over by that grammar's stages, then by whatever the host put
    /// after them, and positioned where it sits in the document holding it.
    /// </summary>
    /// <param name="holes">Whether somebody is writing in it, which puts a hole wherever something is still to be written.</param>
    /// <param name="grammar">What reads it, where the fence's language names the diagram rather than the first line. Null for a Mermaid block, whose header names its own.</param>
    /// <param name="after">What the host runs over the tree once its own stages are done — a binding resolved, say. Null for nothing.</param>
    public static ContentReading Read(string source, bool holes = false, int at = 0,
                                      Nexaflow.Markdown.Mermaid.IMermaidGrammar? grammar = null,
                                      Nexaflow.Markdown.Pipeline.AstPipeline? after = null)
    {
        var tree = MermaidParser.Read(source, holes, grammar);

        return ContentReading.Of(after is null ? tree : after.Run(tree), at);
    }

    /// <summary>The builder a diagram is drawn by, or null for one not drawn on the shared tree.</summary>
    public static Build? For(MermaidDiagram diagram) => diagram switch
    {
        MermaidDiagram.Pie => PieBuilder.Build,
        MermaidDiagram.Venn => VennBuilder.Build,
        MermaidDiagram.Radar => RadarBuilder.Build,
        MermaidDiagram.XyChart => XyBuilder.Build,
        MermaidDiagram.Quadrant => QuadrantBuilder.Build,
        MermaidDiagram.Ishikawa => Ishikawa.IshikawaBuilder.Build,
        MermaidDiagram.Gantt => Gantt.GanttBuilder.Build,
        MermaidDiagram.Kanban => Kanban.KanbanBuilder.Build,
        MermaidDiagram.Mindmap => Mindmap.MindmapBuilder.Build,
        MermaidDiagram.Cynefin => Cynefin.CynefinBuilder.Build,
        MermaidDiagram.Timeline => Timeline.TimelineBuilder.Build,
        MermaidDiagram.Journey => Journey.JourneyBuilder.Build,
        MermaidDiagram.GitGraph => Git.GitBuilder.Build,
        MermaidDiagram.Block => Block.BlockBuilder.Build,
        MermaidDiagram.Architecture => Architecture.ArchitectureBuilder.Build,
        MermaidDiagram.Sankey => Sankey.SankeyBuilder.Build,
        MermaidDiagram.Flowchart => Flowchart.FlowchartBuilder.Build,
        MermaidDiagram.Swimlane => Swimlane.SwimlaneBuilder.Build,
        MermaidDiagram.State => State.StateBuilder.Build,
        MermaidDiagram.Class => Class.ClassBuilder.Build,
        MermaidDiagram.Requirement => Requirement.RequirementBuilder.Build,
        MermaidDiagram.Er => Er.ErBuilder.Build,
        MermaidDiagram.Sequence => Sequence.SequenceBuilder.Build,
        MermaidDiagram.C4Sequence => C4.C4SequenceBuilder.Build,
        MermaidDiagram.C4 => C4.C4Builder.Build,
        _ => null,
    };

    /// <summary>The element a block is shown and written in, or null where its diagram is not drawn on the shared tree.</summary>
    /// <remarks>Read-only where the host takes no edits, which leaves a diagram there looked at, selected and followed.</remarks>
    public static Editing.ContentElement? Element(string source, MermaidDiagram diagram, DiagramRenderOptions options) =>
        For(diagram) is { } build ? MermaidBuilder.Host(source, options, build, options.ReadOnly) : null;

    /// <summary>Lays a block out as its header names, with no caret in it — or null where its diagram is not drawn on the shared tree.</summary>
    public static Laid? Lay(string source, MarkdownPalette palette, double pixelsPerDip = 1, double room = double.PositiveInfinity,
                            bool writing = false, int at = 0) =>
        For(MermaidBlock.Read(source).Diagram)?.Invoke(Read(source, holes: writing, at: at),
                                                       new DiagramLaying(palette, pixelsPerDip, room, writing));
}

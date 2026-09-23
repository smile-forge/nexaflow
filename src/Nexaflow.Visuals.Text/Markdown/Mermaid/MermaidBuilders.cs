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
    /// <summary>
    /// Makes the builder a diagram is drawn by, from the four things every builder is made from. Ask it for a
    /// builder and call <see cref="Editing.ContentBuilder.Lay"/>; there is no other way in and nothing else to
    /// pass.
    /// </summary>
    public delegate Editing.ContentBuilder Make(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly);

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

    /// <summary>
    /// What a host puts between reading a block and drawing it: what the words are bound against, where it said,
    /// and which language reads anything written inside it.
    ///
    /// <para>
    /// Settled once, here, where what the host is showing the document against is in scope — a builder never
    /// learns that there is such a thing as binding, or such a thing as a table of languages.
    /// </para>
    /// </summary>
    internal static Nexaflow.Markdown.Pipeline.AstPipeline After(StyleFormat style, DiagramRenderOptions? options) =>
        new([.. options?.DataContext is { } data
                  ? new Nexaflow.Markdown.Pipeline.IAstStage[] { new Nexaflow.Markdown.Pipeline.Stages.WithBindings(data) }
                  : [],
             new Stages.WithNested(style, options)]);

    /// <summary>The builder a diagram is drawn by, or null for one not drawn on the shared tree.</summary>
    public static Make? For(MermaidDiagram diagram) => diagram switch
    {
        MermaidDiagram.Pie => static (r, s, f, o) => new PieBuilder(r, s, f, o),
        MermaidDiagram.Venn => static (r, s, f, o) => new VennBuilder(r, s, f, o),
        MermaidDiagram.Radar => static (r, s, f, o) => new RadarBuilder(r, s, f, o),
        MermaidDiagram.XyChart => static (r, s, f, o) => new XyBuilder(r, s, f, o),
        MermaidDiagram.Quadrant => static (r, s, f, o) => new QuadrantBuilder(r, s, f, o),
        MermaidDiagram.Ishikawa => static (r, s, f, o) => new Ishikawa.IshikawaBuilder(r, s, f, o),
        MermaidDiagram.Gantt => static (r, s, f, o) => new Gantt.GanttBuilder(r, s, f, o),
        MermaidDiagram.Kanban => static (r, s, f, o) => new Kanban.KanbanBuilder(r, s, f, o),
        MermaidDiagram.Mindmap => static (r, s, f, o) => new Mindmap.MindmapBuilder(r, s, f, o),
        MermaidDiagram.Cynefin => static (r, s, f, o) => new Cynefin.CynefinBuilder(r, s, f, o),
        MermaidDiagram.Timeline => static (r, s, f, o) => new Timeline.TimelineBuilder(r, s, f, o),
        MermaidDiagram.Journey => static (r, s, f, o) => new Journey.JourneyBuilder(r, s, f, o),
        MermaidDiagram.GitGraph => static (r, s, f, o) => new Git.GitBuilder(r, s, f, o),
        MermaidDiagram.Block => static (r, s, f, o) => new Block.BlockBuilder(r, s, f, o),
        MermaidDiagram.Architecture => static (r, s, f, o) => new Architecture.ArchitectureBuilder(r, s, f, o),
        MermaidDiagram.Sankey => static (r, s, f, o) => new Sankey.SankeyBuilder(r, s, f, o),
        MermaidDiagram.Flowchart => static (r, s, f, o) => new Flowchart.FlowchartBuilder(r, s, f, o),
        MermaidDiagram.Swimlane => static (r, s, f, o) => new Swimlane.SwimlaneBuilder(r, s, f, o),
        MermaidDiagram.State => static (r, s, f, o) => new State.StateBuilder(r, s, f, o),
        MermaidDiagram.Class => static (r, s, f, o) => new Class.ClassBuilder(r, s, f, o),
        MermaidDiagram.Requirement => static (r, s, f, o) => new Requirement.RequirementBuilder(r, s, f, o),
        MermaidDiagram.Er => static (r, s, f, o) => new Er.ErBuilder(r, s, f, o),
        MermaidDiagram.Sequence => static (r, s, f, o) => new Sequence.SequenceBuilder(r, s, f, o),
        MermaidDiagram.C4Sequence => static (r, s, f, o) => new C4.C4SequenceBuilder(r, s, f, o),
        MermaidDiagram.C4 => static (r, s, f, o) => new C4.C4Builder(r, s, f, o),
        _ => null,
    };

    /// <summary>Lays a block out as its header names, with no caret in it — or null where its diagram is not drawn on the shared tree.</summary>
    public static Laid? Lay(string source, StyleFormat style, double room = double.PositiveInfinity,
                            bool writing = false, int at = 0, DiagramRenderOptions? options = null, RawZone? shown = null)
    {
        // Read once: which diagram it is comes from the reading that is drawn, never from a second one.
        var reading = Read(source, holes: writing, at: at, after: After(style, options));

        // A header naming no diagram is still drawn — as written, from this same reading, so it stands where it was written.
        var make = For(MermaidBlock.Of(reading).Diagram) ?? (static (r, s, f, o) => new UnknownDiagramBuilder(r, s, f, o));

        return make(reading, EditState.For(source) with { Raw = shown }, style, isReadOnly: !writing).Lay(room);
    }

    /// <summary>
    /// The same, for a diagram whose type is settled by the grammar reading it rather than by a keyword in a
    /// header — nomnoml, which is a class diagram written another way and so has no keyword to look up.
    /// </summary>
    public static Laid Lay(Make make, Nexaflow.Markdown.Mermaid.IMermaidGrammar? grammar, string source,
                           StyleFormat style, double room = double.PositiveInfinity, int at = 0,
                           DiagramRenderOptions? options = null) =>
        make(Read(source, at: at, grammar: grammar, after: After(style, options)),
             EditState.For(source), style, isReadOnly: true)
            .Lay(room);
}

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Flowchart;
using Nexaflow.Markdown.Mermaid.Swimlane;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Flowchart;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Swimlane;

/// <summary>
/// Draws a <c>swimlane-beta</c> diagram: a flowchart whose outermost subgraphs are lanes — a band each, running the whole length of
/// the chart with the lane's own name at the near end of it — so that what is drawn says who owns each step as well as what follows
/// what. A lane's cells come one to a rank, and a link handed to another lane goes across rather than on.
///
/// <para>
/// A swimlane is read as a flowchart and drawn by the flowchart's own builder, which is how Mermaid reads and draws it: the grammar,
/// the model, the shapes, the links and the styling are all a flowchart's, and only the way it is laid out differs. What the lanes
/// themselves ask for is <see cref="SwimlaneConfig"/>; everything else the front matter says is the flowchart's own.
/// </para>
/// </summary>
internal sealed class SwimlaneBuilder : FlowchartBuilder
{
    private SwimlaneBuilder(ContentReading reading, DiagramLaying laying) : base(reading, laying) { }

    /// <summary>Lays a swimlane's source out. Never null, and never throws.</summary>
    public static Laid Build(ContentReading reading, DiagramLaying laying) => new SwimlaneBuilder(reading, laying).Lay();

    /// <inheritdoc/>
    protected override (bool Sideways, bool Ordered)? Laning(FlowchartDiagram diagram)
    {
        var lanes = SwimlaneConfig.Read(diagram.Block.Config);

        return (lanes.Sideways, lanes.Ordered);
    }
}

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Er.Stages;

/// <summary>
/// Says which subgraph each line is written in, and which one each <c>subgraph</c> line opens. Which box an entity is drawn in
/// depends on every <c>subgraph</c> and <c>end</c> written above it, and so is a fact about the whole block rather than about
/// any one line (<see cref="MermaidNesting"/>).
/// </summary>
public sealed class ResolveGroups : IAstStage
{
    public string Name => "er:subgraphs";

    public ContentNode Run(ContentNode tree) =>
        MermaidNesting.Inside(tree, [ErKinds.Subgraph], ErKinds.Ends,
                              [ErKinds.Entity, ErKinds.Block, ErKinds.Relation, ErKinds.Direction,
                               ErKinds.ClassDef, ErKinds.CssClass, ErKinds.Style],
                              ErKinds.Fact, ErRoles.Inside, ErRoles.Opened,
                              stray: "This end closes a subgraph, and none is open here.",
                              unclosed: "This subgraph is never closed: end closes it.");
}

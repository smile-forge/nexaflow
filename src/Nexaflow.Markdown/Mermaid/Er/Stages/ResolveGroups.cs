using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Er.Stages;

/// <summary>
/// Gathers each subgraph with the lines written in it, so the tree says which box every entity is drawn in. That depends on
/// every <c>subgraph</c> and <c>end</c> written above it, and so is a fact about the whole block rather than about any one
/// line (<see cref="MermaidNesting.Nest"/>).
/// </summary>
public sealed class ResolveGroups : IAstStage
{
    public string Name => "er:subgraphs";

    public ContentNode Run(ContentNode tree) =>
        MermaidNesting.Nest(tree, [ErKinds.Subgraph], [ErKinds.Ends],
                            stray: "This end closes a subgraph, and none is open here.",
                            unclosed: "This subgraph is never closed: end closes it.");
}

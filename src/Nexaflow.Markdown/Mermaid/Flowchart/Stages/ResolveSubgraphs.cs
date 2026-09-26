using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Flowchart.Stages;

/// <summary>
/// Gathers each subgraph with the lines written in it, so the tree says which box every node is drawn in and which subgraph a
/// <c>direction</c> line lays out. That depends on every <c>subgraph</c> and <c>end</c> written above it, and so is a fact about the
/// whole block rather than about any one line (<see cref="MermaidNesting.Nest"/>).
/// </summary>
public sealed class ResolveSubgraphs : IAstStage
{
    public string Name => "flowchart:subgraphs";

    public ContentNode Run(ContentNode tree) =>
        MermaidNesting.Nest(tree, [FlowchartKinds.Opens], [FlowchartKinds.Ends],
                            stray: "This end closes a subgraph, and none is open here.",
                            unclosed: "This subgraph is never closed: end closes it.");
}

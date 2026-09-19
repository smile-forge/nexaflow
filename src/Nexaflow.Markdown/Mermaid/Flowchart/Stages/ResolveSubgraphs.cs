using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Flowchart.Stages;

/// <summary>
/// Says which subgraph each line is written in, and which one each <c>subgraph</c> line opens. Which box a node is drawn in
/// depends on every <c>subgraph</c> and <c>end</c> written above it, and so is a fact about the whole block rather than about any
/// one line (<see cref="MermaidNesting"/>) — and a <c>direction</c> line lays out the subgraph it is in, which is the same fact.
/// </summary>
public sealed class ResolveSubgraphs : IAstStage
{
    public string Name => "flowchart:subgraphs";

    public ContentNode Run(ContentNode tree) =>
        MermaidNesting.Inside(tree, [FlowchartKinds.Opens], [FlowchartKinds.Ends], [FlowchartKinds.Nodes, FlowchartKinds.Direction],
                              FlowchartKinds.Fact, FlowchartRoles.Inside, FlowchartRoles.Opened,
                              stray: "This end closes a subgraph, and none is open here.",
                              unclosed: "This subgraph is never closed: end closes it.");
}

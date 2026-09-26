using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Flowchart.Stages;

/// <summary>
/// Says where a node written on a line names a subgraph rather than a node of its own. Mermaid lets a link join a subgraph by its
/// id, written above the subgraph or below it, so whether a name is a subgraph's is a fact about the whole block rather than about
/// the line it is written on — every subgraph is read first, and then each node naming one says which (<see cref="GroupReferenceNode"/>).
/// </summary>
public sealed class ResolveJoins : IAstStage
{
    public string Name => "flowchart:joins";

    public ContentNode Run(ContentNode tree)
    {
        var boxes = new Dictionary<string, int>(StringComparer.Ordinal);
        var opened = 0;

        foreach (var node in tree.SelfAndDescendants().Where(node => node.Kind == FlowchartKinds.Opens))
        {
            if (Named(node.Inner(FlowchartKinds.Node)) is { Length: > 0 } id) boxes.TryAdd(id, opened);
            opened++;
        }

        if (boxes.Count == 0) return tree;

        var joined = new Dictionary<ContentNode, int>(ReferenceEqualityComparer.Instance);

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == FlowchartKinds.Nodes))
            foreach (var piece in line.Children.Where(child => child.Kind == FlowchartKinds.Node))
                if (Named(piece) is { Length: > 0 } id && boxes.TryGetValue(id, out var group))
                    joined[piece] = group;

        if (joined.Count == 0) return tree;

        return AstRewrite.Each(tree, node => joined.TryGetValue(node, out var group) ? new GroupReferenceNode(node, group) : node);
    }

    /// <summary>What a node is called.</summary>
    private static string? Named(ContentNode? piece) =>
        piece?.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words()?.Text;
}

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Er.Stages;

/// <summary>
/// Says where a relationship names a subgraph rather than an entity. Mermaid lets a subgraph be joined by its id, written above
/// the subgraph or below it, so whether a name is a subgraph's is a fact about the whole block rather than about the line it is
/// written on — every subgraph is read first, and then each end of a relationship naming one says which
/// (<see cref="GroupReferenceNode"/>).
/// </summary>
public sealed class ResolveJoins : IAstStage
{
    public string Name => "er:joins";

    public ContentNode Run(ContentNode tree)
    {
        var boxes = new Dictionary<string, int>(StringComparer.Ordinal);
        var relations = new List<ContentNode>();
        var opened = 0;

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { } stated) continue;

            if (stated.Kind == ErKinds.Relation) relations.Add(stated);
            if (stated.Kind != ErKinds.Subgraph) continue;

            if (Said(stated, ErRoles.Space) is { Length: > 0 } name) boxes.TryAdd(name, opened);
            opened++;
        }

        if (boxes.Count == 0) return tree;

        var joined = new Dictionary<ContentNode, int>(ReferenceEqualityComparer.Instance);

        foreach (var stated in relations)
            foreach (var named in stated.SelfAndDescendants().Where(node => node.Kind == ErKinds.Named))
                if (Said(named, ErRoles.Id) is { Length: > 0 } name && boxes.TryGetValue(name, out var group))
                    joined[named] = group;

        if (joined.Count == 0) return tree;

        return AstRewrite.Each(tree, node => joined.TryGetValue(node, out var group) ? new GroupReferenceNode(node, group) : node);
    }

    /// <summary>What a line says in a role, wherever it is written inside it.</summary>
    private static string? Said(ContentNode node, string role) =>
        node.SelfAndDescendants().FirstOrDefault(inner => inner.Kind == MermaidKinds.Words && inner.Role == role)?.Text;
}

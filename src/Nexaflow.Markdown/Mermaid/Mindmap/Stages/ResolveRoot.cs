using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Mindmap.Stages;

/// <summary>
/// Says where a node hangs off nothing. The first node written is the root, and every later one hangs off the nearest node
/// before it indented less; a node indented no further than the root has no parent, which Mermaid refuses, since a mindmap has
/// one root.
/// </summary>
public sealed class ResolveRoot : IAstStage
{
    public string Name => "mindmap:root";

    public ContentNode Run(ContentNode tree)
    {
        var lines = tree.SelfAndDescendants()
            .Where(line => line.Kind == MermaidKinds.Line && line.Stated()?.Kind == MindmapKinds.Node)
            .ToList();
        if (lines.Count == 0) return tree;

        var root = lines[0].Indent();
        var orphans = lines.Skip(1).Where(line => line.Indent() <= root).Select(line => line.Stated()!).ToHashSet();
        if (orphans.Count == 0) return tree;

        return AstRewrite.Each(tree, node =>
            orphans.Contains(node)
                ? node.Saying("A mindmap has one root, and every other node is indented under it.")
                : node);
    }
}

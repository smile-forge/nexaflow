using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Block.Stages;

/// <summary>
/// Says where a <c>class</c> or a <c>style</c> line names something nothing writes — a block laid out nowhere, or a class no
/// <c>classDef</c> declares. Mermaid allows the styling to be written above what it styles or below it, so whether what it
/// names exists is a fact about the whole block.
/// </summary>
public sealed class ResolveStyles : IAstStage
{
    public string Name => "block:styles";

    public ContentNode Run(ContentNode tree)
    {
        var blocks = Names(tree, [BlockKinds.Item, BlockKinds.Arrow], BlockRoles.Id);
        var classes = Names(tree, [BlockKinds.ClassDef], BlockRoles.Class);
        var wrong = new Dictionary<ContentNode, string>();

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind is BlockKinds.Class or BlockKinds.Style))
        {
            foreach (var name in Said(line, BlockRoles.Id))
                if (Text(name) is { Length: > 0 } id && !blocks.Contains(id))
                    wrong[name] = $"No block {id} is laid out in this diagram.";

            if (line.Kind != BlockKinds.Class) continue;

            foreach (var name in Said(line, BlockRoles.Class))
                if (Text(name) is { Length: > 0 } taken && !classes.Contains(taken))
                    wrong[name] = $"No classDef {taken} is written.";
        }

        if (wrong.Count == 0) return tree;

        return AstRewrite.Each(tree, node => wrong.TryGetValue(node, out var reason) ? node.Saying(reason) : node);
    }

    /// <summary>Everything named in a role by the lines of those kinds.</summary>
    private static HashSet<string> Names(ContentNode tree, IReadOnlyList<string> kinds, string role) =>
        [.. tree.SelfAndDescendants()
              .Where(node => kinds.Contains(node.Kind))
              .SelectMany(node => Said(node, role))
              .Select(Text)
              .OfType<string>()
              .Where(name => name.Length > 0)];

    /// <summary>The names a line writes in a role.</summary>
    private static IEnumerable<ContentNode> Said(ContentNode line, string role) =>
        line.SelfAndDescendants()
            .Where(node => node.Kind == MermaidKinds.Name
                           && node.Children.Any(child => child.Kind == MermaidKinds.Words && child.Role == role));

    private static string? Text(ContentNode name) =>
        name.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Words)?.Text;
}

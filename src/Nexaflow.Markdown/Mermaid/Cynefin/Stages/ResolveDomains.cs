using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Cynefin.Stages;

/// <summary>
/// Says which domain each item sits in: the one opened by the nearest domain line above it. An item written before any
/// domain is opened sits in none, and there is nowhere to draw it, so it says so.
/// </summary>
public sealed class ResolveDomains : IAstStage
{
    public string Name => "cynefin:domains";

    public ContentNode Run(ContentNode tree)
    {
        var sits = new Dictionary<ContentNode, string?>();
        string? domain = null;

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line))
        {
            switch (line.Stated())
            {
                case { Kind: CynefinKinds.Domain } opened:
                    domain = opened.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Key)?.Text.ToLowerInvariant();
                    break;

                case { Kind: CynefinKinds.Item } item:
                    sits[item] = domain;
                    break;
            }
        }

        if (sits.Count == 0) return tree;

        return AstRewrite.Each(tree, node =>
            sits.TryGetValue(node, out var said)
                ? said is null
                    ? node.Saying("An item sits in the domain opened above it: complex, then what is in it.")
                    : node.Saying(CynefinKinds.Fact, CynefinRoles.In, said)
                : node);
    }
}

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Venn.Stages;

/// <summary>
/// Works out what each set, union and item is drawn in. A <c>style</c> line names what it styles by the key the regions are known
/// by (<see cref="ResolveRegions"/>) and may be written above it or below, and several may style the one thing, each laid over
/// the last — so what each comes to is the whole block's to work out. It is said on each region, and on each item's line, as a
/// <see cref="StyledNode"/>.
/// </summary>
public sealed class ResolveStyles : IAstStage
{
    public string Name => "venn:styles";

    public ContentNode Run(ContentNode tree)
    {
        var styles = new Dictionary<string, MermaidStyle>(StringComparer.Ordinal);

        foreach (var said in tree.SelfAndDescendants().Where(node => node.Kind == VennKinds.Style))
            if (said.Said(VennRoles.Key) is { Length: > 0 } key)
                styles[key] = styles.GetValueOrDefault(key, MermaidStyle.None).With(said.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Properties));

        if (styles.Count == 0) return tree;

        return AstRewrite.Each(tree, node => Key(node) is { } key && styles.TryGetValue(key, out var style) ? new StyledNode(node, style) : node);
    }

    /// <summary>What a region is known by, or an item called — or null for anything else.</summary>
    private static string? Key(ContentNode node) => node.Kind switch
    {
        VennKinds.Region => node.Said(VennRoles.Key),
        VennKinds.Text => node.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Inner(MermaidKinds.Words)?.Text,
        _ => null,
    };
}

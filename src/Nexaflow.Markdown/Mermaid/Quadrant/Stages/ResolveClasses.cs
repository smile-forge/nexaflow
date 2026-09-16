using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Quadrant.Stages;

/// <summary>
/// Says where a point takes a class no <c>classDef</c> writes. Mermaid lets a class be defined above the points taking it or
/// below them, so whether one is written is a fact about the whole block rather than the point's line.
/// </summary>
public sealed class ResolveClasses : IAstStage
{
    public string Name => "quadrant:classes";

    public ContentNode Run(ContentNode tree)
    {
        var classes = tree.SelfAndDescendants()
            .Where(node => node.Kind == QuadrantKinds.Class)
            .Select(Named)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return AstRewrite.Each(tree, node =>
        {
            if (node.Kind != QuadrantKinds.Point || node.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name) is not { } taken) return node;

            var name = taken.Inner(MermaidKinds.Words)?.Text ?? string.Empty;
            return name.Length == 0 || classes.Contains(name)
                ? node
                : node.With([.. node.Children.Select(child => ReferenceEquals(child, taken) ? child.Saying($"No classDef {name} is written.") : child)]);
        });
    }

    private static string Named(ContentNode line) =>
        line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Inner(MermaidKinds.Words)?.Text ?? string.Empty;
}

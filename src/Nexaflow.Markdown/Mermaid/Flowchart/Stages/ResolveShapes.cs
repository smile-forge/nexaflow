using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Flowchart.Stages;

/// <summary>
/// Says where <c>@{ shape: … }</c> names a shape Mermaid has none by. Mermaid refuses to draw a flowchart asking for one, and a
/// node drawn as some other shape would say it was asked for that.
/// </summary>
public sealed class ResolveShapes : IAstStage
{
    public string Name => "flowchart:shapes";

    public ContentNode Run(ContentNode tree)
    {
        var wrong = tree.SelfAndDescendants()
            .Where(node => node.Kind == MermaidKinds.Property
                           && string.Equals(node.Children.FirstOrDefault(child => child.Role == Roles.Name)?.Text, "shape", StringComparison.OrdinalIgnoreCase))
            .Select(property => property.Children.FirstOrDefault(child => child.Role == MermaidRoles.Value))
            .OfType<ContentNode>()
            .Where(value => value.Text.Length > 0 && MermaidShapes.Named(value.Text) is null)
            .ToList();

        if (wrong.Count == 0) return tree;

        return AstRewrite.Each(tree, node => wrong.Any(value => ReferenceEquals(value, node))
                                   ? node.Saying($"Mermaid has no shape called {node.Text.Trim()}: rect, rounded, diamond, cyl, doc and the rest are.")
                                   : node);
    }
}

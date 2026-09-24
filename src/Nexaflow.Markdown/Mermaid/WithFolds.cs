using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// What a diagram's front matter says about folding (<see cref="NexaflowConfig"/>), read once and hung on the diagram.
///
/// <para>
/// A stage, because it is a fact about what the source amounts to. Whatever draws the diagram, and whatever answers a
/// press on one of its chips, asks the tree for it — so nothing after the reading ever looks at the front matter's
/// characters again, and the key an opening is kept under is the one the drawing used.
/// </para>
/// <para>
/// Hung only where the front matter says something about folding, which is almost never; a diagram that says nothing
/// is read as it was.
/// </para>
/// </summary>
public sealed class WithFolds : IAstStage
{
    public string Name => "mermaid:folds";

    public ContentNode Run(ContentNode tree)
    {
        if (Held(tree) is not null) return tree;

        var folds = NexaflowConfig.Read(MermaidBlock.Of(tree).Config);

        return folds.IsEmpty ? tree : tree.Holding(MermaidKinds.Folds, Roles.Derived, folds);
    }

    /// <summary>What the diagram <paramref name="root"/> is folded by — <see cref="NexaflowConfig.None"/> where it said nothing.</summary>
    public static NexaflowConfig Of(ContentNode root) => Held(root) ?? NexaflowConfig.None;

    /// <summary>
    /// What the diagram holding <paramref name="part"/> is folded by, or null where the tree it belongs to is not one
    /// that says anything about folding.
    /// </summary>
    public static NexaflowConfig? Holding(ContentPart part)
    {
        var root = part;
        while (root.Parent is { } up) root = up;

        return Held(root.Node);
    }

    private static NexaflowConfig? Held(ContentNode root)
    {
        foreach (var child in root.Children)
            if (child.IsDerived && child.Kind == MermaidKinds.Folds)
                foreach (var held in child.Children)
                    if (held.Held is NexaflowConfig folds) return folds;

        return null;
    }
}

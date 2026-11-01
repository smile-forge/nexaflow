using Nexaflow.Icons;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// Says which icon everything naming one is (<see cref="MermaidKinds.Icon"/>, as the diagram's grammar read it), and so what draws
/// it (<see cref="RenderedIconNode"/>) — the same for every diagram, whatever it writes an icon as (<see cref="MermaidIcons"/>). What
/// names no icon the app draws is left as it is written, which is what is drawn.
/// </summary>
/// <param name="defaultPack">The pack a bare name is an icon of, where the diagram names one — a tree view's <c>defaultIconPack</c>.</param>
public sealed class WithIcons(string? defaultPack = null) : IAstStage
{
    public string Name => "mermaid:icons";

    public ContentNode Run(ContentNode tree)
    {
        if (!tree.SelfAndDescendants().Any(node => node.Kind == MermaidKinds.Icon)) return tree;

        return AstRewrite.Each(tree, node =>
            node.Kind == MermaidKinds.Icon && MermaidIcons.Of(MermaidText.Bare(Said(node)), defaultPack) is { } icon ? new RenderedIconNode(node, icon) : node);
    }

    /// <summary>What names the icon: the words written for it, or what its key is set to.</summary>
    private static string? Said(ContentNode icon) => icon.Words()?.Text ?? icon.Inner(MermaidKinds.Setting)?.Text;
}

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Block.Stages;

/// <summary>
/// Says where a link has nothing to join. A link joins the blocks written either side of it on its own line, so whether it has
/// two ends is about the line as a whole rather than about the link.
/// </summary>
public sealed class ResolveLinks : IAstStage
{
    private const string Alone = "A link joins the blocks written either side of it, and one side of this one is empty: A --> B.";

    public string Name => "block:links";

    public ContentNode Run(ContentNode tree)
    {
        var wrong = new List<ContentNode>();

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == BlockKinds.Items))
        {
            var pieces = line.Children
                .Where(child => child.Kind is BlockKinds.Item or BlockKinds.Arrow or BlockKinds.Space or BlockKinds.Link)
                .ToList();

            for (var at = 0; at < pieces.Count; at++)
                if (pieces[at].Kind == BlockKinds.Link && !(Joined(pieces, at, back: true) && Joined(pieces, at, back: false)))
                    wrong.Add(pieces[at]);
        }

        if (wrong.Count == 0) return tree;

        return AstRewrite.Each(tree, node => wrong.Any(link => ReferenceEquals(link, node)) ? node.Saying(Alone) : node);
    }

    /// <summary>Whether a block is written on one side of a link, empty cells between them counting for nothing.</summary>
    private static bool Joined(IReadOnlyList<ContentNode> pieces, int at, bool back)
    {
        for (var next = back ? at - 1 : at + 1; next >= 0 && next < pieces.Count; next += back ? -1 : 1)
            if (pieces[next].Kind is BlockKinds.Item or BlockKinds.Arrow)
                return true;

        return false;
    }
}

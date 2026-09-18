using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Flowchart.Stages;

/// <summary>
/// Says where a link has nothing to join. A link joins the nodes written either side of it on its own line — the ones before it
/// carrying on from the link before that — so whether it has two ends is about the line as a whole rather than about the link.
/// </summary>
public sealed class ResolveLinks : IAstStage
{
    private const string Alone = "A link joins the nodes either side of it, and one side of this one is empty: A --> B.";

    public string Name => "flowchart:links";

    public ContentNode Run(ContentNode tree)
    {
        var wrong = new List<ContentNode>();

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == FlowchartKinds.Nodes))
        {
            var pieces = line.Children.Where(child => child.Kind is FlowchartKinds.Node or FlowchartKinds.Link).ToList();

            for (var at = 0; at < pieces.Count; at++)
                if (pieces[at].Kind == FlowchartKinds.Link && !(Joined(pieces, at, back: true) && Joined(pieces, at, back: false)))
                    wrong.Add(pieces[at]);
        }

        if (wrong.Count == 0) return tree;

        return AstRewrite.Each(tree, node => wrong.Any(link => ReferenceEquals(link, node)) ? node.Saying(Alone) : node);
    }

    /// <summary>Whether a node is written on one side of a link, the nodes a link before it reached counting for its own.</summary>
    private static bool Joined(IReadOnlyList<ContentNode> pieces, int at, bool back)
    {
        var next = back ? at - 1 : at + 1;

        return next >= 0 && next < pieces.Count && pieces[next].Kind == FlowchartKinds.Node;
    }
}

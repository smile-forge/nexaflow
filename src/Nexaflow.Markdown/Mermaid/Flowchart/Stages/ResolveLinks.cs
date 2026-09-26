using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Flowchart.Stages;

/// <summary>
/// Says what each link joins (<see cref="FlowchartLinkNode"/>), and where a link has nothing to join.
///
/// <para>
/// A link joins the nodes written either side of it on its own line — every node on its left to every node on its right, where
/// <c>&amp;</c> writes several, the ones before it being those the link before it reached — so what it joins, and whether it has
/// two ends at all, is about the line as a whole rather than about the link. A semicolon starts the line's next chain afresh.
/// </para>
/// <para>
/// Each join is a link of its own to a <c>linkStyle</c> line, which calls it by where it comes among every join in the block, and
/// an <c>id@{ curve: … }</c> line may be written above the link it names or below it — so what a link is drawn with is a fact
/// about the whole block.
/// </para>
/// </summary>
public sealed class ResolveLinks : IAstStage
{
    private const string Alone = "A link joins the nodes either side of it, and one side of this one is empty: A --> B.";

    public string Name => "flowchart:links";

    public ContentNode Run(ContentNode tree)
    {
        var wrong = new HashSet<ContentNode>(ReferenceEqualityComparer.Instance);
        var joined = new List<(ContentNode Link, List<(string From, string To)> Pairs)>();
        var styled = new List<(IReadOnlyList<int> Links, bool Every, MermaidStyle Style)>();
        var curves = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var node in tree.SelfAndDescendants())
        {
            switch (node.Kind)
            {
                case FlowchartKinds.Nodes:
                    Lined(node, wrong, joined);
                    break;

                case FlowchartKinds.LinkStyle:
                    styled.Add((Numbered(node), Every(node), MermaidStyle.None.With(node.Inner(MermaidKinds.Properties))));
                    break;

                case FlowchartKinds.Said:
                    if (ResolveMetadata.Id(node) is { Length: > 0 } id && ResolveMetadata.Set(node.Inner(MermaidKinds.Properties), "curve") is { } curve)
                        curves[id] = curve.Text;
                    break;
            }
        }

        if (wrong.Count == 0 && joined.Count == 0) return tree;

        // Every join numbered in the order it is written, which is the number a linkStyle line calls it by.
        var links = new Dictionary<ContentNode, FlowchartLinkNode>(ReferenceEqualityComparer.Instance);
        var number = 0;

        foreach (var (link, pairs) in joined)
        {
            var joins = new List<FlowchartJoin>(pairs.Count);

            foreach (var (from, to) in pairs)
            {
                var style = MermaidStyle.None;
                foreach (var (numbered, every, written) in styled)
                    if (every || numbered.Contains(number))
                        style = written.Over(style);

                joins.Add(new FlowchartJoin(from, to, style));
                number++;
            }

            var named = link.Children.FirstOrDefault(child => child.Role == FlowchartRoles.Link)?.Text;
            links[link] = new FlowchartLinkNode(link, joins, named is { Length: > 0 } && curves.TryGetValue(named, out var curve) ? curve : null);
        }

        // A link joining something has two ends, so nothing is wrong with it.
        return AstRewrite.Each(tree, node =>
            links.TryGetValue(node, out var link) ? link
            : wrong.Contains(node) ? node.Saying(Alone)
            : node);
    }

    /// <summary>What each link on a line joins, and which have nothing on one side to join.</summary>
    private static void Lined(ContentNode line, HashSet<ContentNode> wrong, List<(ContentNode Link, List<(string From, string To)> Pairs)> joined)
    {
        var sides = new List<List<string>> { new() };
        var joins = new List<ContentNode?>();

        foreach (var piece in line.Children)
        {
            switch (piece.Kind)
            {
                case FlowchartKinds.Node:
                    sides[^1].Add(piece.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name).Words()?.Text ?? string.Empty);
                    break;

                case FlowchartKinds.Link:
                    joins.Add(piece);
                    sides.Add([]);
                    break;

                default:
                    // A semicolon ends what was written before it; what follows it starts again.
                    if (piece.Role != Roles.Separator || piece.Text != ";") continue;

                    joins.Add(null);
                    sides.Add([]);
                    break;
            }
        }

        for (var at = 0; at < joins.Count; at++)
            if (joins[at] is { } link && sides[at].Count > 0 && sides[at + 1].Count > 0)
                joined.Add((link, [.. sides[at].SelectMany(from => sides[at + 1].Select(to => (from, to)))]));

        // Whether a link has two ends is read past any semicolon: a node either side of it, the one next to it in writing.
        var pieces = line.Children.Where(child => child.Kind is FlowchartKinds.Node or FlowchartKinds.Link).ToList();
        for (var at = 0; at < pieces.Count; at++)
            if (pieces[at].Kind == FlowchartKinds.Link && !(Joined(pieces, at - 1) && Joined(pieces, at + 1)))
                wrong.Add(pieces[at]);

        static bool Joined(IReadOnlyList<ContentNode> pieces, int next) =>
            next >= 0 && next < pieces.Count && pieces[next].Kind == FlowchartKinds.Node;
    }

    /// <summary>The links a <c>linkStyle</c> line numbers.</summary>
    private static IReadOnlyList<int> Numbered(ContentNode stated) =>
        [.. stated.SelfAndDescendants()
              .Where(node => node.Kind == MermaidKinds.Words && node.Role == FlowchartRoles.Index)
              .Select(node => MermaidNumber.Read(node.Text))
              .OfType<double>()
              .Where(at => at >= 0 && at == Math.Floor(at))
              .Select(at => (int)at)];

    /// <summary>Whether a <c>linkStyle</c> line styles every link, which is what <c>default</c> asks for.</summary>
    private static bool Every(ContentNode stated) =>
        stated.Children.Any(child => child.Kind == MermaidKinds.Key && child.Role == FlowchartRoles.Index);
}

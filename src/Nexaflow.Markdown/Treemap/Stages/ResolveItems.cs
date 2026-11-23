using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Treemap.Stages;

/// <summary>
/// What a treemap's items are: each gathered with the items written under it (<see cref="MermaidOutline.Gather"/>), and said on it
/// (<see cref="TreemapNode"/>) — the value written for it, what it and everything under it come to, whether it is a section, and
/// the style it is drawn with.
///
/// <para>
/// The rules are Mermaid's. An item hangs off the nearest section before it indented less — an item with a value holds nothing, so one written under it
/// stands beside it — and any number of items stand at the top.
/// What an item comes to is its own value and the totals of the items under it, as d3 sums a hierarchy. Its style is the
/// <c>default</c> class, then the class it takes; a class no <c>classDef</c> writes styles nothing, as in Mermaid.
/// </para>
/// </summary>
public sealed class ResolveItems : IAstStage
{
    public string Name => "treemap:items";

    public ContentNode Run(ContentNode tree)
    {
        var classes = Classes(tree);
        var every = classes.GetValueOrDefault(MermaidStyling.Every, MermaidStyle.None);

        var depths = Depths(tree);
        var gathered = MermaidOutline.Gather(tree, line => depths.TryGetValue(line, out var deep) ? deep : null, TreemapKinds.Node);

        return AstRewrite.Each(gathered, node => node.Kind == TreemapKinds.Node ? Resolved(node, classes, every) : node);
    }

    /// <summary>
    /// How deep each item's line stands in the tree, as Mermaid builds it: under the nearest section before it indented less, a section
    /// being an item with no value. Where an item stands is worked out here, ahead of gathering, since how far it is indented is not
    /// enough: one indented under an item with a value stands beside that item.
    /// </summary>
    private static Dictionary<ContentNode, int> Depths(ContentNode tree)
    {
        var depths = new Dictionary<ContentNode, int>(ReferenceEqualityComparer.Instance);
        var sections = new Stack<(int Indent, int Depth)>();

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line && node.Stated()?.Kind == TreemapKinds.Item))
        {
            var indent = line.Indent();
            while (sections.Count > 0 && sections.Peek().Indent >= indent) sections.Pop();

            var depth = sections.Count > 0 ? sections.Peek().Depth + 1 : 0;
            depths[line] = depth;

            if (line.Stated()!.Inner(MermaidKinds.Amount) is null) sections.Push((indent, depth));
        }

        return depths;
    }

    /// <summary>The styles the block's <c>classDef</c> lines write, by the classes they name.</summary>
    private static Dictionary<string, MermaidStyle> Classes(ContentNode tree)
    {
        var classes = new Dictionary<string, MermaidStyle>(StringComparer.Ordinal);

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == TreemapKinds.ClassDef))
        {
            var style = MermaidStyle.None.With(line.Inner(MermaidKinds.Properties));
            foreach (var name in line.SelfAndDescendants().Where(node => node.Kind == Kinds.Words && node.Role == TreemapRoles.Class))
                classes[name.Text] = style;
        }

        return classes;
    }

    /// <summary>An item gathered with what is under it, and what it comes to — its children already said, being rewritten first.</summary>
    private static TreemapNode Resolved(ContentNode node, IReadOnlyDictionary<string, MermaidStyle> classes, MermaidStyle every)
    {
        var item = node.Children[0].Stated()!;
        var amount = item.Inner(MermaidKinds.Amount);
        var value = amount?.Inner(Kinds.Number) is { Trouble: null, Width: > 0 } number ? TreemapValues.Read(number.Text) : null;

        var taken = item.SelfAndDescendants().FirstOrDefault(part => part.Kind == Kinds.Words && part.Role == TreemapRoles.Class)?.Text;
        var style = taken is not null && classes.TryGetValue(taken, out var given) ? given.Over(every) : every;

        return new TreemapNode(node)
        {
            Value = value,
            Total = (value ?? 0) + node.Children.OfType<TreemapNode>().Sum(child => child.Total),
            Section = amount is null,
            Style = style,
        };
    }
}

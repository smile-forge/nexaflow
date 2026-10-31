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
/// The rules are Mermaid's. An item hangs off the nearest item before it indented less, and any number of items stand at the top.
/// What an item comes to is its own value and the totals of the items under it, as d3 sums a hierarchy. Its style is the
/// <c>default</c> class, then the class it takes; a class no <c>classDef</c> writes is marked where it is named.
/// </para>
/// </summary>
public sealed class ResolveItems : IAstStage
{
    public string Name => "treemap:items";

    public ContentNode Run(ContentNode tree)
    {
        var classes = Classes(tree);
        var every = classes.GetValueOrDefault(MermaidStyling.Every, MermaidStyle.None);

        var gathered = MermaidOutline.Gather(tree, Depth, TreemapKinds.Node);

        return AstRewrite.Each(gathered, node => node.Kind switch
        {
            TreemapKinds.Node => Resolved(node, classes, every),
            TreemapKinds.Item => Unknown(node, classes),
            _ => node,
        });
    }

    /// <summary>An item's line, with the class it takes marked where no <c>classDef</c> writes it.</summary>
    private static ContentNode Unknown(ContentNode item, IReadOnlyDictionary<string, MermaidStyle> classes) =>
        AstRewrite.Each(item, node =>
            node.Kind == Kinds.Words && node.Role == TreemapRoles.Class && node.Text.Length > 0 && !classes.ContainsKey(node.Text)
                ? node.Saying($"No classDef {node.Text} is written.")
                : node);

    /// <summary>How deep a line stands — how far it is indented — where it writes an item; null for any other line.</summary>
    private static int? Depth(ContentNode line) => line.Stated()?.Kind == TreemapKinds.Item ? line.Indent() : null;

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

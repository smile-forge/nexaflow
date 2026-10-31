using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.TreeView.Stages;

/// <summary>
/// What a tree view's nodes are: each gathered with the nodes written under it (<see cref="MermaidOutline.Gather"/>), and said on it
/// (<see cref="TreeViewNode"/>) — whether it is a folder, and whether its row is picked out.
///
/// <para>
/// The rules are Mermaid's. A block written with a box drawing anywhere in it is read as one throughout: how deep a node stands is
/// where the branch in front of it stands, a step for each width of the drawing's first indented branch (four, where none is
/// indented), and a node with no branch in front of it stands at the top — so a node indented with no branch in front of it is wrong,
/// and is marked. Any other block nests by indentation, a tab counting as four, each node under the nearest one before it indented
/// less, and any number of them at the top.
/// </para>
/// </summary>
public sealed class ResolveNodes : IAstStage
{
    /// <summary>How many columns a tab stands for, as Mermaid expands one.</summary>
    private const int Tab = 4;

    private const string Unbranched = "In a tree drawn with ├── and └──, an indented node has a branch in front of it too.";

    public string Name => "treeview:nodes";

    public ContentNode Run(ContentNode tree)
    {
        var lines = tree.Children.Where(line => line.Stated()?.Kind == TreeViewKinds.Item).ToList();
        var drawn = lines.Any(line => Branch(line) is not null);
        var step = drawn ? Step(lines) : 1;

        var gathered = MermaidOutline.Gather(tree, line => line.Stated()?.Kind == TreeViewKinds.Item ? Depth(line, drawn, step) : null, TreeViewKinds.Node);

        return AstRewrite.Each(gathered, node => node.Kind switch
        {
            TreeViewKinds.Node => Resolved(node),
            MermaidKinds.Line when drawn && node.Stated()?.Kind == TreeViewKinds.Item && Branch(node) is null && node.Indent() > 0 => Told(node),
            _ => node,
        });
    }

    /// <summary>How deep an item's line stands: where its branch is, in steps — or how far it is indented, where nothing is drawn.</summary>
    private static int Depth(ContentNode line, bool drawn, int step) =>
        !drawn ? Columns(Lead(line)) : Branch(line) is { } column ? (int)Math.Round((double)column / step, MidpointRounding.AwayFromZero) + 1 : 0;

    /// <summary>
    /// How wide a step of the drawing is: the column of the first branch drawn further in than the edge — four, where none is.
    /// </summary>
    private static int Step(IEnumerable<ContentNode> lines) =>
        lines.Select(Branch).FirstOrDefault(column => column is > 0) ?? Tab;

    /// <summary>The column the branch in front of an item stands at, or null where none is drawn in front of it.</summary>
    private static int? Branch(ContentNode line)
    {
        var lead = Lead(line);
        var at = lead.IndexOfAny(TreeViewParser.Branches.ToCharArray());

        return at < 0 ? null : Columns(lead[..at]);
    }

    /// <summary>Everything in front of an item's label: the space it is indented by, and the box drawing.</summary>
    private static string Lead(ContentNode line)
    {
        var lead = line.Indented() ? line.Children[0].Text : string.Empty;
        return line.Stated()?.Children.FirstOrDefault(child => child.Kind == TreeViewKinds.Prefix) is { } prefix ? lead + prefix.Text : lead;
    }

    /// <summary>How many columns <paramref name="text"/> takes, a tab taking four.</summary>
    private static int Columns(string text) => text.Sum(character => character == '\t' ? Tab : 1);

    /// <summary>A line whose node is indented with no branch in front of it, in a tree drawn with them, told so.</summary>
    private static ContentNode Told(ContentNode line)
    {
        var stated = line.Stated()!;
        return line.With([.. line.Children.Select(child => ReferenceEquals(child, stated) ? stated.Saying(Unbranched) : child)]);
    }

    /// <summary>A node gathered with what is under it, and what it is.</summary>
    private static TreeViewNode Resolved(ContentNode node)
    {
        var item = node.Children[0].Stated()!;
        var label = item.Inner(TreeViewKinds.Label);

        return new TreeViewNode(node)
        {
            Folder = label?.Children.Any(child => child.Role == TreeViewRoles.Folder) == true,
            Highlighted = item.SelfAndDescendants().Any(part => part.Kind == Kinds.Words && part.Role == TreeViewRoles.Class && part.Text == TreeViewParser.Highlight),
        };
    }
}

using System.Text;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.TreeView;

/// <summary>
/// What a change to a tree view's nodes is written as: a node put in, taken out, moved beside or into another, a level in or out, up
/// or down among its siblings, made a folder or a file, given words beside it — the write half of <see cref="TreeViewParser"/> for
/// what is about nodes rather than words, written through the outline they make (<see cref="OutlineWriting"/>).
///
/// <para>
/// <strong>A node goes with everything under it</strong>, and keeps every character it says. What says where it stands is written
/// again: its indentation, or — in a tree drawn with a box drawing — the branch and bars in front of it, every one of them, since a
/// branch is <c>└──</c> only on the last of its siblings and a bar runs down only past a folder with more still to come.
/// </para>
/// </summary>
public static class TreeViewWriting
{
    /// <summary>A node put in: a label still to write, in quotes so it is a node before anything is written in it.</summary>
    private static readonly string Fresh = new(TreeViewParser.Opening, 2);

    /// <summary>The tree view's nodes as an outline, its leads spelled as the tree is drawn.</summary>
    public static OutlineWriting Outline(ContentPart root) => OutlineWriting.Of(root, TreeViewKinds.Node, Lead, Spelling);

    /// <summary>Every node, in the order written.</summary>
    public static IEnumerable<ContentPart> Items(ContentPart root) => root.SelfAndDescendants().Where(part => part.Kind == TreeViewKinds.Node);

    /// <summary>The node <paramref name="part"/> is in — the node itself, or anything written in it — or null.</summary>
    public static ContentPart? ItemOf(ContentPart? part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind == TreeViewKinds.Node) return at;

        return null;
    }

    /// <summary>The label written on <paramref name="item"/>'s own line.</summary>
    public static ContentPart? Label(ContentPart item) => item.Children[0].Stated()?.Inner(TreeViewKinds.Label);

    /// <summary>Whether <paramref name="item"/> is a folder: its label written with a slash after it.</summary>
    public static bool Folder(ContentPart item) => Label(item) is { } label && TreeViewParser.Folder(label);

    /// <summary>What is said beside <paramref name="item"/>, where anything is.</summary>
    public static ContentPart? Description(ContentPart item) => item.Children[0].Stated()?.Inner(TreeViewKinds.Description);

    /// <summary>A node put in beside <paramref name="beside"/> — after everything, where that is null — and the caret in its label.</summary>
    public static ContentChange Added(ContentPart root, ContentPart? beside, OutlineSide side) =>
        Outline(root).Inserted(Fresh, beside, side, 1);

    /// <summary>
    /// A node put in under <paramref name="item"/>, after what is already there — and, where the tree marks its folders with a slash,
    /// the slash after <paramref name="item"/>'s label, since what holds others is a folder.
    /// </summary>
    public static ContentChange AddedInside(ContentPart root, ContentPart item)
    {
        if (Folder(item) || !Items(root).Any(Folder) || Label(item) is not { } label)
            return Outline(root).Inserted(Fresh, item, OutlineSide.Inside, 1);

        var line = item.Children[0];
        var said = Body(line);
        var at = (label.Words()?.End ?? label.End) - line.Start - Lead(line);

        return Outline(root).Inserted(Fresh, item, OutlineSide.Inside, 1,
                                      new Dictionary<ContentPart, string> { [item] = said[..at] + TreeViewParser.Slash + said[at..] });
    }

    /// <summary><paramref name="items"/> taken out, with everything under them — the caret where <paramref name="caret"/> says, where it says.</summary>
    public static ContentChange? Removed(ContentPart root, IReadOnlyList<ContentPart> items, OutlineCaret? caret = null) =>
        Outline(root).Removed(items, caret);

    /// <summary><paramref name="items"/> moved beside <paramref name="onto"/>, or under it; null where that is onto one of them, or into one.</summary>
    public static ContentChange? Moved(ContentPart root, IReadOnlyList<ContentPart> items, ContentPart onto, OutlineSide side, int caret) =>
        Outline(root).Moved(items, onto, side, caret);

    /// <summary><paramref name="items"/> a level in, under the node before them — null where none is before them.</summary>
    public static ContentChange? Deeper(ContentPart root, IReadOnlyList<ContentPart> items, int caret) => Outline(root).Deeper(items, caret);

    /// <summary><paramref name="items"/> a level out, after the node they are under — null where they are under none.</summary>
    public static ContentChange? Shallower(ContentPart root, IReadOnlyList<ContentPart> items, int caret) => Outline(root).Shallower(items, caret);

    /// <summary><paramref name="items"/> up past the node before them among their siblings.</summary>
    public static ContentChange? Raised(ContentPart root, IReadOnlyList<ContentPart> items, int caret) => Outline(root).Raised(items, caret);

    /// <summary><paramref name="items"/> down past the node after them among their siblings.</summary>
    public static ContentChange? Lowered(ContentPart root, IReadOnlyList<ContentPart> items, int caret) => Outline(root).Lowered(items, caret);

    /// <summary>
    /// <paramref name="item"/> made a folder — the slash written after its label — or a file, the slash taken away; null where it is
    /// that already. The caret stays by what it was by.
    /// </summary>
    public static ContentChange? Foldered(ContentPart item, bool folder, int caret)
    {
        if (Label(item) is not { } label || TreeViewParser.Folder(label) == folder) return null;

        if (folder)
        {
            var at = label.Words()?.End ?? label.End;
            return ContentChange.Write(at, 0, TreeViewParser.Slash, caret >= at ? caret + 1 : caret);
        }

        var slash = label.Children.First(child => child.Role == TreeViewRoles.Folder);
        return ContentChange.Write(slash.Start, slash.Length, string.Empty, caret > slash.Start ? caret - 1 : caret);
    }

    /// <summary>
    /// Words beside <paramref name="item"/> still to write — <c>##</c> after what its line says — and the caret where they go; null
    /// where it can have none (<see cref="Describable"/>).
    /// </summary>
    public static ContentChange? Described(ContentPart item)
    {
        if (!Describable(item) || item.Children[0].Stated() is not { } stated) return null;

        var written = " " + TreeViewParser.DescriptionMark + " ";
        return ContentChange.Write(stated.End, 0, written, stated.End + written.Length);
    }

    /// <summary>
    /// Whether words can be put beside <paramref name="item"/>: it has none yet, and its line ends in no comment — the words run to the
    /// end of the line, so a comment there would be taken into them.
    /// </summary>
    public static bool Describable(ContentPart item) =>
        Description(item) is null && item.Children[0].Stated() is { } stated && !stated.Children.Any(child => child.Kind == Kinds.Comment);

    /// <summary>Where the caret stands at the end of <paramref name="item"/>'s label, once the outline is written again — null where it has none.</summary>
    public static OutlineCaret? AfterLabel(ContentPart item) =>
        Label(item) is { } label && label.Words() is { } words ? new OutlineCaret(item, words.End - label.Start) : null;

    // ── Leads ───────────────────────────────────────────────────────────────

    /// <summary>The lead of a node's line: everything in front of its label — its indentation, and the box drawing in front of it.</summary>
    private static int Lead(ContentPart line) =>
        line.Stated()?.Inner(TreeViewKinds.Label) is { } label ? label.Start - line.Start : 0;

    /// <summary>What a node's line says after its lead, less the line ending closing it.</summary>
    private static string Body(ContentPart line)
    {
        var said = line.Print();
        var ends = line.Children.LastOrDefault() is { Kind: Kinds.Space } space && space.Print() is "\n" or "\r\n" ? space.Length : 0;

        return said[Lead(line)..(said.Length - ends)];
    }

    /// <summary>
    /// The leads spelled as the tree is drawn: where it is drawn with a box drawing, the bars and branch every node's place in it
    /// calls for — in the drawing's own weight and width, and with nothing in front of the nodes at the top where it writes them
    /// so — and otherwise indentation, as deep as the tree is already indented.
    /// </summary>
    private static Func<OutlinePlace, string>? Spelling(OutlineWriting outline)
    {
        var leads = outline.Leads.ToList();
        var branches = TreeViewParser.Branches.ToCharArray();
        if (!leads.Any(lead => lead.IndexOfAny(branches) >= 0)) return null;

        var heavy = leads.Any(lead => lead.IndexOfAny(['┃', '┣', '┗', '━']) >= 0);
        var step = leads.Select(lead => lead.IndexOfAny(branches) is var at and > 0 ? Columns(lead[..at]) : 0).FirstOrDefault(column => column > 0);
        if (step < 2) step = 4;

        var bare = leads[0].IndexOfAny(branches) < 0;
        var (tee, elbow, bar, dash) = heavy ? ('┣', '┗', '┃', '━') : ('├', '└', '│', '─');

        return place =>
        {
            if (bare && place.Depth == 0) return string.Empty;

            var lead = new StringBuilder();
            for (var level = bare ? 1 : 0; level < place.Depth; level++)
                lead.Append(place.Last[level] ? new string(' ', step) : bar + new string(' ', step - 1));

            return lead.Append(place.Last[place.Depth] ? elbow : tee).Append(dash, Math.Max(1, step - 2)).Append(' ').ToString();
        };
    }

    /// <summary>How many columns <paramref name="text"/> takes, a tab taking four.</summary>
    private static int Columns(string text) => text.Sum(character => character == '\t' ? 4 : 1);
}

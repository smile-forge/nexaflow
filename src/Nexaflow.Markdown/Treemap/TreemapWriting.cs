using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Treemap;

/// <summary>
/// What a change to a treemap's items is written as: an item put in, taken out, moved into a section or beside another, a level in
/// or out, up or down among its siblings — the write half of <see cref="TreemapParser"/> for what is about items rather than words,
/// written through the outline they make (<see cref="OutlineWriting"/>).
///
/// <para>
/// <strong>An item goes with everything under it</strong>, and keeps every character it says; only its indentation changes as it
/// moves, which is what says what it is under. A leaf put in is worth what the leaf beside it is, so it is drawn the size of its
/// neighbour rather than too small to see; a leaf given an item under it becomes a section, handing its value to that item, so
/// what the map adds up to is the same.
/// </para>
/// </summary>
public static class TreemapWriting
{
    /// <summary>What a leaf put in is worth where there is no leaf beside it to go by.</summary>
    private const string One = "1";

    /// <summary>The treemap's items as an outline.</summary>
    public static OutlineWriting Outline(ContentPart root) => OutlineWriting.Of(root, TreemapKinds.Node, Lead);

    /// <summary>Every item, in the order written.</summary>
    public static IEnumerable<ContentPart> Items(ContentPart root) => root.SelfAndDescendants().Where(part => part.Kind == TreemapKinds.Node);

    /// <summary>The item <paramref name="part"/> is in — the item itself, or anything written in it — or null.</summary>
    public static ContentPart? ItemOf(ContentPart? part)
    {
        for (var at = part; at is not null; at = at.Parent)
            if (at.Kind == TreemapKinds.Node) return at;

        return null;
    }

    /// <summary>Whether <paramref name="item"/> is a section: written with no value, or holding items under it.</summary>
    public static bool Holds(ContentPart item) =>
        item.Node is TreemapNode { Section: true } || item.Children.Any(child => child.Kind == TreemapKinds.Node);

    /// <summary>The value written in <paramref name="item"/>'s own line, where one is — the number, or the hole where it is still to write.</summary>
    public static ContentPart? Value(ContentPart item) => item.Children[0].Stated()?.Inner(MermaidKinds.Amount);

    /// <summary>
    /// A leaf put in beside <paramref name="beside"/> — after everything, where that is null — worth what the leaf beside it is, and the
    /// caret in its label.
    /// </summary>
    public static ContentChange Added(ContentPart root, ContentPart? beside, OutlineSide side) =>
        Outline(root).Inserted(Fresh(Worth(beside)), beside, side, 1);

    /// <summary>
    /// A leaf put in under <paramref name="item"/>, after the items already there. A leaf given one becomes a section and hands it its
    /// value, so what the map adds up to does not change.
    /// </summary>
    public static ContentChange AddedInside(ContentPart root, ContentPart item)
    {
        if (Holds(item) || Value(item) is not { } value || Written(value) is not { } worth)
            return Outline(root).Inserted(Fresh(Worth(item)), item, OutlineSide.Inside, 1);

        return Outline(root).Inserted(Fresh(worth), item, OutlineSide.Inside, 1, new Dictionary<ContentPart, string> { [item] = Unvalued(item, value) });
    }

    /// <summary><paramref name="items"/> taken out, with everything under them — the caret where <paramref name="caret"/> says, where it says.</summary>
    public static ContentChange? Removed(ContentPart root, IReadOnlyList<ContentPart> items, OutlineCaret? caret = null) =>
        Outline(root).Removed(items, caret);

    /// <summary>
    /// <paramref name="items"/> let go on <paramref name="onto"/>: into it after what it holds, where it is a section; beside it in the
    /// section it is in, where it is a leaf. Null where that is onto one of them, or into one.
    /// </summary>
    public static ContentChange? Moved(ContentPart root, IReadOnlyList<ContentPart> items, ContentPart onto, int caret) =>
        Outline(root).Moved(items, onto, Holds(onto) ? OutlineSide.Inside : OutlineSide.After, caret);

    /// <summary><paramref name="items"/> a level in, into the section before them — null where none is before them.</summary>
    public static ContentChange? Deeper(ContentPart root, IReadOnlyList<ContentPart> items, int caret) => Outline(root).Deeper(items, caret);

    /// <summary><paramref name="items"/> a level out, after the section they are in — null where they are in none.</summary>
    public static ContentChange? Shallower(ContentPart root, IReadOnlyList<ContentPart> items, int caret) => Outline(root).Shallower(items, caret);

    /// <summary><paramref name="items"/> up past the item before them among their siblings.</summary>
    public static ContentChange? Raised(ContentPart root, IReadOnlyList<ContentPart> items, int caret) => Outline(root).Raised(items, caret);

    /// <summary><paramref name="items"/> down past the item after them among their siblings.</summary>
    public static ContentChange? Lowered(ContentPart root, IReadOnlyList<ContentPart> items, int caret) => Outline(root).Lowered(items, caret);

    /// <summary>Where the caret stands at the end of <paramref name="item"/>'s label, once the outline is written again — null where it has none.</summary>
    public static OutlineCaret? AfterLabel(ContentPart item)
    {
        var line = item.Children[0];
        if (line.Stated()?.Inner(TreemapKinds.Label)?.Words() is not { } words) return null;

        return new OutlineCaret(item, words.End - line.Start - Lead(line));
    }

    /// <summary>The lead of an item's line: the space it is indented by.</summary>
    private static int Lead(ContentPart line) =>
        line.Children.FirstOrDefault() is { Kind: Kinds.Space } space && space.Start == line.Start ? space.Length : 0;

    /// <summary>A leaf still to be called anything, worth <paramref name="worth"/>.</summary>
    private static string Fresh(string worth) => $"{TreemapParser.Opening}{TreemapParser.Opening}{TreemapParser.Colon} {worth}";

    /// <summary>
    /// What a leaf put in beside <paramref name="beside"/> is worth: the value written for it, or for the first leaf in it — one, where
    /// none is written.
    /// </summary>
    private static string Worth(ContentPart? beside) =>
        beside?.SelfAndDescendants().Where(part => part.Kind == TreemapKinds.Node).Select(Value).OfType<ContentPart>().Select(Written).OfType<string>().FirstOrDefault()
        ?? One;

    /// <summary>The number written in a value, where it is one — as it is written, so it keeps the reader's way of writing numbers.</summary>
    private static string? Written(ContentPart value) =>
        value.Inner(Kinds.Number) is { Trouble: null, Length: > 0 } number ? number.Text : null;

    /// <summary>What <paramref name="item"/>'s line says after its indentation, less the colon and the value after its label.</summary>
    private static string Unvalued(ContentPart item, ContentPart value)
    {
        var line = item.Children[0];
        var said = line.Print();
        var from = Lead(line);
        var label = line.Stated()!.Inner(TreemapKinds.Label)!;

        var ends = line.Children.LastOrDefault() is { Kind: Kinds.Space } space && space.Print() is "\n" or "\r\n" ? space.Length : 0;
        var body = said[from..(said.Length - ends)];
        var start = line.Start + from;

        return body[..(label.End - start)] + body[(value.End - start)..];
    }
}

using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Treemap;

/// <summary>
/// What a change to a treemap's boxes is written as — a box split in two, boxes merged into one, boxes put in a new group, a group's
/// boxes let out into the group around it, boxes moved into a group, a box put in or taken out — the write half of
/// <see cref="TreemapParser"/> for what is about items rather than words, written through the outline they make
/// (<see cref="OutlineWriting"/>).
///
/// <para>
/// <strong>What the map adds up to is kept wherever a change can keep it.</strong> A box split in two is two boxes worth half each;
/// boxes merged are one box worth them all; a box made a group hands its value to a box in it, and a group losing its last box that
/// way takes its value back. Where a tile is drawn follows from what it is worth, so these are what a reader sees happen to tiles,
/// whatever order their lines are written in.
/// </para>
/// <para>
/// An item goes with everything under it, and keeps every character it says but the value a change gives it; only its indentation
/// changes as it moves, which is what says what it is in. A box put in is worth what a box beside it is, so it is drawn the size of
/// its neighbours rather than too small to see. A value a change works out is written as the values beside it are — grouped with
/// commas where one of them is.
/// </para>
/// </summary>
public static class TreemapWriting
{
    /// <summary>What a box put in is worth where there is no box to go by.</summary>
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

    /// <summary>The group <paramref name="item"/> is in — the item it is written under — or <paramref name="root"/>, where it is in none.</summary>
    public static ContentPart HolderOf(ContentPart root, ContentPart item) => ItemOf(item.Parent) ?? root;

    /// <summary>Whether <paramref name="item"/> is a section: written with no value, or holding items under it.</summary>
    public static bool Holds(ContentPart item) =>
        item.Node is TreemapNode { Section: true } || item.Children.Any(child => child.Kind == TreemapKinds.Node);

    /// <summary>The value written in <paramref name="item"/>'s own line, where one is — the number, or the hole where it is still to write.</summary>
    public static ContentPart? Value(ContentPart item) => item.Children[0].Stated()?.Inner(MermaidKinds.Amount);

    /// <summary>Whether <paramref name="item"/> is a box with a value to halve — what splitting it needs.</summary>
    public static bool Splits(ContentPart item) => !Holds(item) && WorthOf(item) is > 0;

    /// <summary>Whether <paramref name="items"/> are two or more boxes, each with a value to add to the rest — what merging them needs.</summary>
    public static bool Merges(IReadOnlyList<ContentPart> items) => items.Count > 1 && items.All(Mergeable);

    /// <summary>A box put in after everything, worth what the first box written is, and the caret in its label.</summary>
    public static ContentChange Added(ContentPart root) => Outline(root).Inserted(Fresh(Like(root)), null, OutlineSide.After, 1);

    /// <summary>A box put in <paramref name="group"/> after the items already there, worth what the first box in it is, and the caret in its label.</summary>
    public static ContentChange AddedInside(ContentPart root, ContentPart group) =>
        Outline(root).Inserted(Fresh(Like(group)), group, OutlineSide.Inside, 1);

    /// <summary>
    /// <paramref name="item"/> split in two: it worth half what it was and a box after it, worth the other half, with the caret in the
    /// new box's label — so the group it is in adds up to what it did. Null where it is not a box with a value to halve.
    /// </summary>
    public static ContentChange? Split(ContentPart root, ContentPart item)
    {
        if (Holds(item) || Number(item) is not { } number || TreemapValues.Read(number.Text) is not (> 0 and var worth)) return null;

        var (kept, given) = Halves(worth);
        var grouped = number.Text.Contains(TreemapValues.Grouped, StringComparison.Ordinal);

        return Outline(root).Inserted(Fresh(Spelled(given, grouped)), item, OutlineSide.After, 1,
                                      new Dictionary<ContentPart, string> { [item] = Restated(item, number, Spelled(kept, grouped)) });
    }

    /// <summary>
    /// <paramref name="items"/> merged into <paramref name="into"/>, one of them: it worth them all, the rest taken out, and the caret
    /// at the end of its label. Null where they are not boxes that merge (<see cref="Merges"/>), or it is not one of them.
    /// </summary>
    public static ContentChange? Merged(ContentPart root, IReadOnlyList<ContentPart> items, ContentPart into)
    {
        if (!Merges(items) || !items.Any(item => ReferenceEquals(item, into)) || Number(into) is not { } number) return null;

        var total = items.Sum(item => WorthOf(item)!.Value);
        var grouped = items.Any(item => Number(item)!.Text.Contains(TreemapValues.Grouped, StringComparison.Ordinal));

        return Outline(root).Removed([.. items.Where(item => !ReferenceEquals(item, into))], AfterLabel(into),
                                     new Dictionary<ContentPart, string> { [into] = Restated(into, number, Spelled(total, grouped)) });
    }

    /// <summary>
    /// <paramref name="items"/> put in a new group where the first of them stands, the caret in its label. A box on its own becomes the
    /// group itself, called what it was, and hands its value to a box in it with the caret in that box's label — so it is drawn the size
    /// it was, ready to split.
    /// </summary>
    public static ContentChange? Grouped(ContentPart root, IReadOnlyList<ContentPart> items)
    {
        if (items is [var only] && !Holds(only) && Value(only) is { } value && Written(value) is { } worth)
            return Outline(root).Inserted(Fresh(worth), only, OutlineSide.Inside, 1, new Dictionary<ContentPart, string> { [only] = Unvalued(only, value) });

        return Outline(root).Grouped(Unnamed, items, 1);
    }

    /// <summary>The items in <paramref name="group"/> let out into the group around it where it stood, and its own line taken out.</summary>
    public static ContentChange? Ungrouped(ContentPart root, ContentPart group) => Holds(group) ? Outline(root).Ungrouped(group) : null;

    /// <summary><paramref name="items"/> taken out, with everything under them — the caret where <paramref name="caret"/> says, where it says.</summary>
    public static ContentChange? Removed(ContentPart root, IReadOnlyList<ContentPart> items, OutlineCaret? caret = null) =>
        Outline(root).Removed(items, caret);

    /// <summary>
    /// <paramref name="item"/>, a box with nothing written in its label, taken back into the box beside it — as Backspace joins a line
    /// with nothing on it to the one before — so a split taken back is the box it was: into the box before it in its group, or else the
    /// one after, which takes its worth. Where it is the only item in its group, the group takes its worth and is a box again; anywhere
    /// else it is taken out, the caret at the end of the label before it.
    /// </summary>
    public static ContentChange? MergedBack(ContentPart root, ContentPart item)
    {
        var holder = HolderOf(root, item);
        var siblings = ItemsIn(holder).ToList();
        var at = siblings.FindIndex(each => ReferenceEquals(each, item));

        var into = siblings.Take(at).LastOrDefault(Mergeable) ?? siblings.Skip(at + 1).FirstOrDefault(Mergeable);
        if (into is not null && Merged(root, [item, into], into) is { } merged) return merged;

        if (siblings.Count == 1 && !ReferenceEquals(holder, root) && Number(item) is { } number)
            return Outline(root).Removed([item], AfterLabel(holder), new Dictionary<ContentPart, string> { [holder] = Valued(holder, number.Text) });

        var before = Items(root).TakeWhile(each => each.Start < item.Start).LastOrDefault();
        return Removed(root, [item], before is null ? null : AfterLabel(before));
    }

    /// <summary>
    /// <paramref name="items"/> moved into <paramref name="holder"/> — a group, after what it holds, or the treemap itself, after
    /// everything at its top — with everything under them. Null where they are all in it already, or it is one of them or in one.
    /// </summary>
    public static ContentChange? MovedInto(ContentPart root, IReadOnlyList<ContentPart> items, ContentPart holder, int caret)
    {
        if (items.All(item => ReferenceEquals(HolderOf(root, item), holder))) return null;
        if (!ReferenceEquals(holder, root)) return Outline(root).Moved(items, holder, OutlineSide.Inside, caret);

        return ItemsIn(root).LastOrDefault(top => !items.Any(item => ReferenceEquals(item, top))) is { } last
            ? Outline(root).Moved(items, last, OutlineSide.After, caret)
            : null;
    }

    /// <summary>Where the caret stands at the end of <paramref name="item"/>'s label, once the outline is written again — null where it has none.</summary>
    public static OutlineCaret? AfterLabel(ContentPart item)
    {
        var line = item.Children[0];
        if (line.Stated()?.Inner(TreemapKinds.Label)?.Words() is not { } words) return null;

        return new OutlineCaret(item, words.End - line.Start - Lead(line));
    }

    /// <summary>The items written directly in <paramref name="holder"/> — a group, or the treemap itself — in the order written.</summary>
    private static IEnumerable<ContentPart> ItemsIn(ContentPart holder) =>
        holder.Children.Where(part => part.Kind == TreemapKinds.Node && part.Node is TreemapNode);

    /// <summary>Whether <paramref name="item"/> is a box whose value reads — one that can be merged with another.</summary>
    private static bool Mergeable(ContentPart item) => !Holds(item) && WorthOf(item) is not null;

    /// <summary>The number written as <paramref name="item"/>'s value, where one is written that reads.</summary>
    private static ContentPart? Number(ContentPart item) =>
        Value(item)?.Inner(Kinds.Number) is { Trouble: null, Length: > 0 } number ? number : null;

    /// <summary>What <paramref name="item"/>'s value says it is worth, where one is written that reads.</summary>
    private static double? WorthOf(ContentPart item) => Number(item) is { } number ? TreemapValues.Read(number.Text) : null;

    /// <summary>
    /// <paramref name="worth"/> in two: a whole number of two or more into whole numbers, the larger half kept — any other in two equal
    /// halves, so neither is drawn as nothing.
    /// </summary>
    private static (double Kept, double Given) Halves(double worth) =>
        worth >= 2 && worth == Math.Floor(worth) ? (Math.Ceiling(worth / 2), Math.Floor(worth / 2)) : (worth / 2, worth / 2);

    /// <summary><paramref name="worth"/> written as a value is — with commas between thousands where <paramref name="grouped"/> says.</summary>
    private static string Spelled(double worth, bool grouped) =>
        Math.Round(worth, 10).ToString(grouped ? "#,0.##########" : "0.##########", CultureInfo.InvariantCulture);

    /// <summary>The lead of an item's line: the space it is indented by.</summary>
    private static int Lead(ContentPart line) =>
        line.Children.FirstOrDefault() is { Kind: Kinds.Space } space && space.Start == line.Start ? space.Length : 0;

    /// <summary>A label with nothing in it yet.</summary>
    private static string Unnamed => new(TreemapParser.Opening, 2);

    /// <summary>A box still to be called anything, worth <paramref name="worth"/>.</summary>
    private static string Fresh(string worth) => $"{Unnamed}{TreemapParser.Colon} {worth}";

    /// <summary>
    /// What a box put in among what <paramref name="beside"/> holds is worth: the value written for the first box in it — one, where none
    /// is written.
    /// </summary>
    private static string Like(ContentPart beside) =>
        beside.SelfAndDescendants().Where(part => part.Kind == TreemapKinds.Node).Select(Value).OfType<ContentPart>().Select(Written).OfType<string>().FirstOrDefault()
        ?? One;

    /// <summary>The number written in a value, where it is one — as it is written, so it keeps the reader's way of writing numbers.</summary>
    private static string? Written(ContentPart value) =>
        value.Inner(Kinds.Number) is { Trouble: null, Length: > 0 } number ? number.Text : null;

    /// <summary>What <paramref name="item"/>'s line says after its indentation, and where in the block that starts.</summary>
    private static (string Body, int Start) Body(ContentPart item)
    {
        var line = item.Children[0];
        var said = line.Print();
        var from = Lead(line);
        var ends = line.Children.LastOrDefault() is { Kind: Kinds.Space } space && space.Print() is "\n" or "\r\n" ? space.Length : 0;

        return (said[from..(said.Length - ends)], line.Start + from);
    }

    /// <summary>What <paramref name="item"/>'s line says after its indentation, less the colon and the value after its label.</summary>
    private static string Unvalued(ContentPart item, ContentPart value)
    {
        var (body, start) = Body(item);
        var label = item.Children[0].Stated()!.Inner(TreemapKinds.Label)!;

        return body[..(label.End - start)] + body[(value.End - start)..];
    }

    /// <summary>What <paramref name="item"/>'s line says after its indentation, its value given after its label as <paramref name="worth"/>.</summary>
    private static string Valued(ContentPart item, string worth)
    {
        var (body, start) = Body(item);
        var at = item.Children[0].Stated()!.Inner(TreemapKinds.Label)!.End - start;

        return body[..at] + $"{TreemapParser.Colon} {worth}" + body[at..];
    }

    /// <summary>What <paramref name="item"/>'s line says after its indentation, the number written as its value saying <paramref name="worth"/> instead.</summary>
    private static string Restated(ContentPart item, ContentPart number, string worth)
    {
        var (body, start) = Body(item);
        return body[..(number.Start - start)] + worth + body[(number.End - start)..];
    }
}

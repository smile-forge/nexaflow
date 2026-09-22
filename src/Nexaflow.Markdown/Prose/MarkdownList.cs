using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;

using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// A list, as the items it is written as — and each item as the blocks written inside it.
///
/// <para>
/// An item's marker is neither stripped nor invented. It is the source that falls before the first block
/// inside the item, kept as trivia where the writer typed it, so a writer who numbered every line <c>1.</c>
/// gets back what they wrote while a builder that wants to draw <c>3.</c> counts items and never reads a
/// character.
/// </para>
/// <para>
/// A list holds lists, and that costs nothing: a nested list is one more block inside an item, with its own
/// source, read by this same reader when the pipeline reaches it.
/// </para>
/// </summary>
public static class MarkdownList
{
    /// <summary><paramref name="source"/> as the items written in it.</summary>
    public static ContentNode Read(string? source, MarkdownPipeline? pipeline = null)
    {
        var text = source ?? string.Empty;
        if (text.Length == 0) return ContentNode.Branch(Kinds.Sequence, [], Roles.Body);

        var read = new Cut(text);
        var parts = new List<ContentNode>();

        MarkdownNumbering? counting = null;

        try
        {
            var document = Markdig.Markdown.Parse(text, pipeline ?? MarkdownParser.Pipeline);

            counting = Counting(document);
            Items(document, parts, read);
        }
        catch
        {
            parts.Add(ContentNode.Shown(read.Rest()));
        }

        read.Gap(parts, read.Length);

        if (counting is not null)
            parts.Add(ContentNode.Holding(MarkdownKinds.Numbering, Roles.Derived, counting));

        return MarkdownParser.Checked(Kinds.Sequence, parts, text, Roles.Body);
    }

    /// <summary>
    /// Every item, in the order they were written, wherever in the reading they turn up.
    ///
    /// <para>
    /// Walked rather than taken off the top, because a list's own source is not always a list on its own: one
    /// written inside a quote carries the quote's <c>&gt;</c> at the head of every line but the first, and read
    /// back by itself that is one item followed by a quotation. The items are all still there and still in
    /// order, so they are gathered wherever they are found — and an item is never walked into, which is what
    /// keeps a list written inside an item inside that item.
    /// </para>
    /// </summary>
    private static void Items(ContainerBlock blocks, List<ContentNode> parts, Cut read)
    {
        foreach (var block in blocks)
            if (block is ListItemBlock item) One(item, parts, read);
            else if (block is ContainerBlock inner) Items(inner, parts, read);
    }

    /// <summary>
    /// How this list counts itself, where it counts at all.
    ///
    /// <para>
    /// Nobody can read it off the markers afterwards: <c>i.</c> is the roman numeral one in a list that
    /// started <c>i. ii. iii.</c> and the ninth letter in one that started <c>a. b. c.</c>, and which it is
    /// depends on what was written above it rather than on the characters. So the reader is asked while it
    /// still knows, and the answer is hung on the list as the derived fact it is.
    /// </para>
    /// </summary>
    private static MarkdownNumbering? Counting(ContainerBlock blocks)
    {
        foreach (var block in blocks)
        {
            if (block is ListBlock list)
                return list.IsOrdered ? new MarkdownNumbering(list.BulletType, Started(list.OrderedStart)) : null;

            if (block is ContainerBlock inner && Counting(inner) is { } found) return found;
        }

        return null;
    }

    /// <summary>
    /// What the first item is numbered. The reader hands back what was written there, which for a lettered
    /// or roman list is a letter — so it is counted back to a number, and anything that will not count is a
    /// list starting at one.
    /// </summary>
    private static int Started(string? written)
    {
        var said = written?.Trim();

        if (string.IsNullOrEmpty(said)) return 1;
        if (int.TryParse(said, out var number)) return Math.Max(number, 1);

        var letter = char.ToLowerInvariant(said[0]);

        return said.Length == 1 && letter >= 'a' && letter <= 'z' ? letter - 'a' + 1 : 1;
    }

    /// <summary>One item: its marker, the box it can be ticked by, and the blocks written inside it.</summary>
    private static void One(ListItemBlock item, List<ContentNode> parts, Cut read)
    {
        var from = read.Starts(item);
        var to = read.Closes(item, from);
        if (to == from) return;

        read.Gap(parts, from);

        var inside = new List<ContentNode>();

        // The marker falls in front of whatever comes first inside the item, so it arrives as the trivia it is.
        Ticked(item, inside, read);
        MarkdownParser.Split(item, inside, read);
        read.Gap(inside, to);

        parts.Add(ContentNode.Branch(MarkdownKinds.Item, inside));
    }

    /// <summary>
    /// The box an item can be ticked by, where somebody wrote one.
    ///
    /// <para>
    /// It belongs to the item rather than to the paragraph its characters sit in, and that is not a nicety:
    /// read on its own, <c>[x] done</c> is a bracket and a word, because a tick is only a tick at the head of
    /// an item. So it is taken here, where that is known, and what is left is an ordinary paragraph.
    /// </para>
    /// <para>
    /// The marks are kept rather than the answer they amount to. What is drawn is a box; what is there is three
    /// characters, and ticking one writes over them.
    /// </para>
    /// </summary>
    private static void Ticked(ListItemBlock item, List<ContentNode> parts, Cut read)
    {
        if (item.Count == 0 || item[0] is not LeafBlock { Inline: not null } leaf) return;
        if (leaf.Inline.FirstChild is not TaskList tick) return;

        var from = read.Starts(tick);
        var to = read.Ends(tick);
        if (to <= from) return;

        read.Gap(parts, from);

        parts.Add(ContentNode.Branch(MarkdownKinds.Task,
            [read.Take(to, tick.Checked ? MarkdownRoles.Done : MarkdownRoles.Todo, Kinds.Token)]));
    }
}

/// <summary>
/// How a list counts itself: which alphabet or numerals its markers are drawn from, and the number the
/// first item is.
///
/// <para>
/// Both are facts a builder needs and neither can be read off a marker: a list numbered <c>1. 1. 1.</c>
/// still counts, one starting at <c>7.</c> still starts there when it is renumbered, and <c>i.</c> is a
/// roman numeral or the ninth letter depending on what stands above it.
/// </para>
/// </summary>
/// <param name="Bullet">The character the reader took the marker's alphabet from — <c>1</c>, <c>a</c>, <c>A</c>, <c>i</c> or <c>I</c>.</param>
/// <param name="Start">What the first item is numbered.</param>
public sealed record MarkdownNumbering(char Bullet, int Start);

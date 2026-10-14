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
    /// <param name="at">Where <paramref name="source"/>'s first character stands in the document holding it.</param>
    /// <param name="lead">
    /// What stands in front of <paramref name="source"/> on the line it starts on. A list written inside an item starts part-way
    /// along its line and its later items carry that indentation, so it is read as it reads inside the item — see
    /// <see cref="MarkdownLines"/>.
    /// </param>
    public static ContentNode Read(string? source, int at = 0, string? lead = null)
    {
        var text = source ?? string.Empty;
        if (text.Length == 0) return ContentNode.Branch(Kinds.Sequence, [], Roles.Body, at);

        var read = new Cut(text, at);
        var parts = new List<ContentNode>();
        var scanned = MarkdownScanner.Scan(MarkdownLines.Of(text, lead), document: false);

        var counting = Counting(scanned);
        Items(scanned, parts, read);
        read.Gap(parts, read.Length);
        Facts(scanned, counting, parts);

        return MarkdownParser.Checked(Kinds.Sequence, parts, text, Roles.Body, at);
    }

    /// <summary>
    /// A list the scan found, read where it stands up to <paramref name="to"/> — a list written in a grid table's cell, whose lines
    /// are slices of the table's — as the items its own source is read into.
    /// </summary>
    internal static ContentNode Read(ScannedBlock list, Cut read, int to)
    {
        var stands = read.Here;
        int? at = read.Stretches(read.At, to).Skip(1).Any() ? null : stands;
        var parts = new List<ContentNode>();

        var counting = Counting(list);
        Items(list, parts, read);
        read.Gap(parts, to);
        Facts(list, counting, parts);

        return ContentNode.Branch(Kinds.Sequence, parts, Roles.Body, at);
    }

    /// <summary>
    /// Every item, in the order they were written, wherever in the reading they turn up — and an item is never walked into,
    /// which is what keeps a list written inside an item inside that item.
    /// </summary>
    private static void Items(ScannedBlock blocks, List<ContentNode> parts, Cut read)
    {
        foreach (var block in blocks.Children)
            if (block is ScannedItem item) One(item, parts, read);
            else if (block.Children.Count > 0) Items(block, parts, read);
    }

    /// <summary>What the scan knows of the list that none of its characters say: how it counts, and whether it is loose.</summary>
    private static void Facts(ScannedBlock blocks, MarkdownNumbering? counting, List<ContentNode> parts)
    {
        if (counting is not null) parts.Add(ContentNode.Holding(MarkdownKinds.Numbering, Roles.Derived, counting));
        if (Listed(blocks) is { Loose: true }) parts.Add(ContentNode.Holding(MarkdownKinds.Loose, Roles.Derived, true));
    }

    /// <summary>The list the scan found, wherever in the reading it turns up.</summary>
    private static ScannedList? Listed(ScannedBlock blocks)
    {
        if (blocks is ScannedList itself) return itself;

        foreach (var block in blocks.Children)
        {
            if (block is ScannedList list) return list;
            if (block.Children.Count > 0 && Listed(block) is { } found) return found;
        }

        return null;
    }

    /// <summary>
    /// How this list counts itself, where it counts at all.
    ///
    /// <para>
    /// Nobody can read it off the markers afterwards: <c>i.</c> is the roman numeral one in a list that
    /// started <c>i. ii. iii.</c> and the ninth letter in one that started <c>a. b. c.</c>, and which it is
    /// depends on what was written above it rather than on the characters. So the scan is asked while it
    /// still knows, and the answer is hung on the list as the derived fact it is.
    /// </para>
    /// </summary>
    private static MarkdownNumbering? Counting(ScannedBlock blocks)
    {
        return Listed(blocks) is { Marker.Ordered: true } list ? new MarkdownNumbering(list.Marker.Bullet, Started(list.Marker)) : null;
    }

    /// <summary>
    /// What the first item is numbered: its number, its letter's place in the alphabet, or its roman numeral's value — and
    /// anything that will not count is a list starting at one.
    /// </summary>
    private static int Started(ScannedMarker marker)
    {
        var said = marker.Number;

        switch (marker.Bullet)
        {
            case '1':
                return int.TryParse(said, out var number) ? number : 1;

            case 'i' or 'I':
            {
                var value = 0;
                var largest = 0;

                for (var at = said.Length - 1; at >= 0; at--)
                {
                    var digit = char.ToLowerInvariant(said[at]) switch { 'i' => 1, 'v' => 5, 'x' => 10, _ => 0 };
                    value += digit < largest ? -digit : digit;
                    largest = Math.Max(largest, digit);
                }

                return Math.Max(value, 1);
            }

            default:
                return said.Length == 1 && char.IsAsciiLetter(said[0]) ? char.ToLowerInvariant(said[0]) - 'a' + 1 : 1;
        }
    }

    /// <summary>One item: its marker, the box it can be ticked by, and the blocks written inside it.</summary>
    private static void One(ScannedItem item, List<ContentNode> parts, Cut read)
    {
        var from = Math.Clamp(item.Start, read.At, read.Length);
        var to = read.Closing(item.End, from);
        if (to == from) return;

        read.Gap(parts, from);

        var inside = new List<ContentNode>();

        // The box belongs to the item rather than to the paragraph its characters sit in: read on its own, [x] done is a
        // bracket and a word, because a tick is only a tick at the head of an item. The marks are kept rather than the
        // answer they amount to: what is drawn is a box, what is there is three characters, and ticking one writes over them.
        if (item.Box is { } box && box >= read.At && box + 3 <= to)
        {
            read.Gap(inside, box);
            inside.Add(Task(read, box + 3, item.Done));
        }

        // The marker falls in front of whatever comes first inside the item, so it arrives as the trivia it is.
        MarkdownParser.Split(item.Children, inside, read);
        read.Gap(inside, to);

        parts.Add(ContentNode.Branch(MarkdownKinds.Item, inside));
    }

    /// <summary>
    /// A task's box, <c>[x]</c>: its brackets, and between them the mark saying whether it is done — the one character a
    /// press on the box rewrites, so what a press means is settled here rather than by anything reading the brackets again.
    /// </summary>
    private static ContentNode Task(Cut read, int end, bool done)
    {
        var mark = done ? MarkdownRoles.Done : MarkdownRoles.Todo;
        var at = read.At;

        if (end - at != 3) return ContentNode.Branch(MarkdownKinds.Task, [read.Take(end, mark, Kinds.Token)]);

        return ContentNode.Branch(MarkdownKinds.Task,
            [read.Take(at + 1, Roles.Open, Kinds.Token), read.Take(at + 2, mark, Kinds.Token), read.Take(end, Roles.Close, Kinds.Token)]);
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
/// <param name="Bullet">The alphabet the markers are drawn from — <c>1</c>, <c>a</c>, <c>A</c>, <c>i</c> or <c>I</c>.</param>
/// <param name="Start">What the first item is numbered.</param>
public sealed record MarkdownNumbering(char Bullet, int Start);

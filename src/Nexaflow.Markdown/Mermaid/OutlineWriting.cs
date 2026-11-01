using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>Where an item goes beside another: in front of it, after it and everything under it, or under it after its last child.</summary>
public enum OutlineSide
{
    Before,
    After,
    Inside,
}

/// <summary>
/// Where an item stands as an outline is written again: how deep, and — for the item and each item it is under, from the top down —
/// whether it is the last of its siblings, which is what a lead drawn as a tree's branches is spelled from.
/// </summary>
public readonly record struct OutlinePlace(int Depth, IReadOnlyList<bool> Last);

/// <summary>Where the caret goes once an outline is written again: <paramref name="Into"/> characters into what <paramref name="Item"/> says after its lead.</summary>
public readonly record struct OutlineCaret(ContentPart Item, int Into);

/// <summary>
/// What a change to an outline's structure is written as — an item put in, taken out, moved beside another, under another, a level in
/// or out, up or down among its siblings, items put under a new one, an item's own taken out from under it — for a diagram that nests
/// by how deep its lines stand and has gathered them so
/// (<see cref="MermaidOutline.Gather"/>).
///
/// <para>
/// <strong>An item goes with everything under it</strong>, and the lines written between its children — a comment, a blank line —
/// go with the item written after them. What an item says after its lead is never touched: only where its line stands and the lead
/// in front of it, which is spelled again for every item from where it now stands, so the outline still nests as it is drawn. The
/// lead is the diagram's to spell (<see cref="OutlinePlace"/>) — indentation by default, as deep as the outline is already indented
/// at each level.
/// </para>
/// <para>
/// The outline is written as one stretch of the block, from its first item's line to its last's, of which only what changed is
/// written over.
/// </para>
/// </summary>
public sealed class OutlineWriting
{
    private readonly ContentPart _root;
    private readonly string _text;
    private readonly string _newline;
    private readonly List<Entry> _forest;
    private readonly Dictionary<(ContentNode Node, int Start), Entry> _entries = [];
    private readonly Func<OutlinePlace, string> _spelled;
    private readonly int _start;
    private readonly int _end;

    private OutlineWriting(ContentPart root, string kind, Func<ContentPart, int> lead, Func<OutlineWriting, Func<OutlinePlace, string>?>? spelling)
    {
        _root = root;
        _text = root.Print();
        _newline = _text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        var waiting = string.Empty;
        _forest = Read(root, null, 0, kind, lead, ref waiting);

        if (_forest.Count > 0)
        {
            _start = _forest[0].Line!.Start;
            var last = Preorder(_forest).Last();
            _end = last.Line!.Start + last.Lead.Length + last.Body.Length;
        }
        else
        {
            // With no item written yet, the outline goes after everything the block holds.
            _start = _end = root.Start + _text.Length;
        }

        _spelled = spelling?.Invoke(this) ?? Indented();
    }

    /// <summary>
    /// The outline written in <paramref name="root"/>, each item a <paramref name="kind"/> gathered with what is under it.
    /// </summary>
    /// <param name="lead">How long the lead of an item's line is — the space before what it says, and whatever else the diagram writes there.</param>
    /// <param name="spelling">How the diagram spells a lead, given the outline as written; indentation as deep as the outline's own, where it says nothing or answers nothing.</param>
    public static OutlineWriting Of(ContentPart root, string kind, Func<ContentPart, int> lead,
                                    Func<OutlineWriting, Func<OutlinePlace, string>?>? spelling = null) =>
        new(root, kind, lead, spelling);

    /// <summary>The line ending the block is written with.</summary>
    public string Newline => _newline;

    /// <summary>Whether the outline has an item at all.</summary>
    public bool Any => _forest.Count > 0;

    /// <summary>The leads written in front of the items, top to bottom — what a diagram spelling its own reads the outline's habits from.</summary>
    public IEnumerable<string> Leads => Preorder(_forest).Select(entry => entry.Lead);

    // ── Changes ─────────────────────────────────────────────────────────────

    /// <summary>
    /// <paramref name="items"/> taken out, with everything under them — the caret where <paramref name="caret"/> says, or where the item
    /// after them now starts, or the end of the one before them. Where <paramref name="bodies"/> says, items left say something else as
    /// well: the one they were merged into saying what they were all worth, say.
    /// </summary>
    public ContentChange? Removed(IReadOnlyList<ContentPart> items, OutlineCaret? caret = null, IReadOnlyDictionary<ContentPart, string>? bodies = null)
    {
        var tops = Tops(items);
        if (tops.Count == 0) return null;

        var order = Preorder(_forest).ToList();
        var after = order.SkipWhile(entry => entry != tops[^1]).Skip(1).FirstOrDefault(entry => !Under(entry, tops));
        var before = order.TakeWhile(entry => entry != tops[0]).LastOrDefault(entry => !Under(entry, tops));

        foreach (var top in tops) Siblings(top).Remove(top);
        Say(bodies);

        return Written(placed => caret is { } at && Find(at.Item) is { } focus ? placed[focus] + at.Into
                               : after is not null ? placed[after]
                               : before is not null ? placed[before] + before.Body.Length
                               : 0);
    }

    /// <summary>
    /// <paramref name="items"/> moved beside <paramref name="onto"/> — in front of it, after it, or under it after its last child — with
    /// everything under them; null where that is onto one of them, or anything under one. The caret stays where it was in what an item
    /// says, or goes to the start of the first of them. Where <paramref name="bodies"/> says, items say something else as well: one they
    /// left with nothing under it saying what it is worth, say.
    /// </summary>
    public ContentChange? Moved(IReadOnlyList<ContentPart> items, ContentPart onto, OutlineSide side, int caret, IReadOnlyDictionary<ContentPart, string>? bodies = null)
    {
        var tops = Tops(items);
        if (tops.Count == 0 || Find(onto) is not { } target || Under(target, tops)) return null;

        return Placed(tops, target, side, caret, bodies);
    }

    /// <summary><paramref name="items"/> a level in: under the item before them, after its last child — null where there is none to go under.</summary>
    public ContentChange? Deeper(IReadOnlyList<ContentPart> items, int caret)
    {
        if (Siblings(Tops(items)) is not (var tops, var siblings)) return null;

        var at = siblings.IndexOf(tops[0]);
        return at > 0 ? Placed(tops, siblings[at - 1], OutlineSide.Inside, caret) : null;
    }

    /// <summary><paramref name="items"/> a level out: after the item they are under — null where they are under none.</summary>
    public ContentChange? Shallower(IReadOnlyList<ContentPart> items, int caret)
    {
        if (Siblings(Tops(items)) is not (var tops, _) || tops[0].Parent is not { } parent) return null;

        return Placed(tops, parent, OutlineSide.After, caret);
    }

    /// <summary>Whether <paramref name="items"/> can go a level in — there is an item before them to go under — without writing it.</summary>
    public bool CanGoDeeper(IReadOnlyList<ContentPart> items) => Siblings(Tops(items)) is (var tops, var siblings) && siblings.IndexOf(tops[0]) > 0;

    /// <summary>Whether <paramref name="items"/> can go a level out — they are under an item — without writing it.</summary>
    public bool CanGoShallower(IReadOnlyList<ContentPart> items) => Siblings(Tops(items)) is (var tops, _) && tops[0].Parent is not null;

    /// <summary><paramref name="items"/> up past the sibling before them — null where they are the first.</summary>
    public ContentChange? Raised(IReadOnlyList<ContentPart> items, int caret)
    {
        if (Siblings(Tops(items)) is not (var tops, var siblings)) return null;

        var at = siblings.IndexOf(tops[0]);
        return at > 0 ? Placed(tops, siblings[at - 1], OutlineSide.Before, caret) : null;
    }

    /// <summary><paramref name="items"/> down past the sibling after them — null where they are the last.</summary>
    public ContentChange? Lowered(IReadOnlyList<ContentPart> items, int caret)
    {
        if (Siblings(Tops(items)) is not (var tops, var siblings)) return null;

        var at = siblings.IndexOf(tops[^1]);
        return at + 1 < siblings.Count ? Placed(tops, siblings[at + 1], OutlineSide.After, caret) : null;
    }

    /// <summary>
    /// An item saying <paramref name="body"/> put in beside <paramref name="beside"/> — after everything else, where that is null — the
    /// caret <paramref name="into"/> characters into it. Where <paramref name="bodies"/> says, items written already say something else
    /// as well: a leaf given a child giving it its value, say.
    /// </summary>
    public ContentChange Inserted(string body, ContentPart? beside, OutlineSide side, int into,
                                  IReadOnlyDictionary<ContentPart, string>? bodies = null)
    {
        var fresh = new Entry(null, null, string.Empty, body, string.Empty);

        if (beside is not null && Find(beside) is { } target) Put([fresh], target, side);
        else _forest.Add(fresh);

        Say(bodies);

        return Written(placed => placed[fresh] + into);
    }

    /// <summary>
    /// An item saying <paramref name="body"/> put in where the first of <paramref name="items"/> stands, with them all under it in the
    /// order written and everything under them — the caret <paramref name="into"/> characters into it. Null where none of them is written.
    /// </summary>
    public ContentChange? Grouped(string body, IReadOnlyList<ContentPart> items, int into)
    {
        var tops = Tops(items);
        if (tops.Count == 0) return null;

        var fresh = new Entry(null, null, string.Empty, body, string.Empty);
        Put([fresh], tops[0], OutlineSide.Before);
        Put(tops, fresh, OutlineSide.Inside);

        return Written(placed => placed[fresh] + into);
    }

    /// <summary>
    /// <paramref name="item"/> taken out with what it says, the items under it standing where it stood a level out, and the lines written
    /// in front of it in front of the first of them — the caret where <paramref name="caret"/> says, or at the start of the first of them.
    /// Null where it is not written.
    /// </summary>
    public ContentChange? Ungrouped(ContentPart item, OutlineCaret? caret = null)
    {
        if (Find(item) is not { } entry) return null;

        var siblings = Siblings(entry);
        var at = siblings.IndexOf(entry);
        var under = entry.Children.ToList();

        foreach (var child in under) child.Parent = entry.Parent;
        if (under.Count > 0) under[0].Before = entry.Before + under[0].Before;

        siblings.RemoveAt(at);
        siblings.InsertRange(at, under);

        return Written(placed => caret is { } held && Find(held.Item) is { } focus ? placed[focus] + held.Into
                               : under.Count > 0 ? placed[under[0]]
                               : 0);
    }

    /// <summary>What the items <paramref name="bodies"/> names say instead, where it names any.</summary>
    private void Say(IReadOnlyDictionary<ContentPart, string>? bodies)
    {
        foreach (var (item, said) in bodies ?? new Dictionary<ContentPart, string>())
            if (Find(item) is { } entry) entry.Body = said;
    }

    // ── The outline ─────────────────────────────────────────────────────────

    /// <summary>An item as it is written: its line, its lead, what it says after it, the lines written in front of it, and what is under it.</summary>
    private sealed class Entry(ContentPart? item, ContentPart? line, string lead, string body, string before)
    {
        public ContentPart? Item { get; } = item;
        public ContentPart? Line { get; } = line;
        public string Lead { get; } = lead;
        public string Body { get; set; } = body;
        public string Before { get; set; } = before;
        public Entry? Parent { get; set; }
        public List<Entry> Children { get; } = [];
    }

    /// <summary>The items gathered in <paramref name="container"/>, each with the lines written in front of it since the item before.</summary>
    private List<Entry> Read(ContentPart container, Entry? parent, int depth, string kind, Func<ContentPart, int> lead, ref string waiting)
    {
        var entries = new List<Entry>();
        var top = parent is null;

        foreach (var child in parent is null ? container.Children : container.Children.Skip(1))
        {
            if (child.Derived) continue;

            if (child.Kind != kind)
            {
                // What is written before the first item, and after the last, is the block's rather than the outline's.
                if (!top || entries.Count > 0) waiting += child.Print();
                continue;
            }

            var line = child.Children[0];
            var said = line.Print();
            var leading = Math.Clamp(lead(line), 0, said.Length);
            var ends = Ending(line);

            var entry = new Entry(child, line, said[..leading], said[leading..Math.Max(leading, said.Length - ends)],
                                  top && entries.Count == 0 ? string.Empty : waiting) { Parent = parent };
            waiting = string.Empty;

            _entries[(child.Node, child.Start)] = entry;
            entry.Children.AddRange(Read(child, entry, depth + 1, kind, lead, ref waiting));
            entries.Add(entry);
        }

        if (top) waiting = string.Empty;
        return entries;
    }

    /// <summary>How long the line ending closing <paramref name="line"/> is — nought where it closes none.</summary>
    private static int Ending(ContentPart line) =>
        line.Children.LastOrDefault() is { Kind: Kinds.Space } space && space.Print() is "\n" or "\r\n" ? space.Length : 0;

    private Entry? Find(ContentPart item) => _entries.GetValueOrDefault((item.Node, item.Start));

    /// <summary>The entries <paramref name="items"/> name, less any under another of them — which go with it — in the order written.</summary>
    private List<Entry> Tops(IReadOnlyList<ContentPart> items)
    {
        var chosen = items.Select(Find).OfType<Entry>().ToHashSet();
        return [.. Preorder(_forest).Where(entry => chosen.Contains(entry) && !Above(entry).Any(chosen.Contains))];
    }

    /// <summary>The entries, and the siblings they are among — null where they are not all among the same ones.</summary>
    private (List<Entry> Tops, List<Entry> Siblings)? Siblings(List<Entry> tops)
    {
        if (tops.Count == 0) return null;

        var siblings = Siblings(tops[0]);
        return tops.All(top => top.Parent == tops[0].Parent) ? (tops, siblings) : null;
    }

    private List<Entry> Siblings(Entry entry) => entry.Parent?.Children ?? _forest;

    /// <summary>The entries <paramref name="entry"/> is under, nearest first.</summary>
    private static IEnumerable<Entry> Above(Entry entry)
    {
        for (var at = entry.Parent; at is not null; at = at.Parent) yield return at;
    }

    /// <summary>Whether <paramref name="entry"/> is one of <paramref name="tops"/>, or under one.</summary>
    private static bool Under(Entry entry, List<Entry> tops) => tops.Contains(entry) || Above(entry).Any(tops.Contains);

    private static IEnumerable<Entry> Preorder(IEnumerable<Entry> entries)
    {
        foreach (var entry in entries)
        {
            yield return entry;
            foreach (var under in Preorder(entry.Children)) yield return under;
        }
    }

    /// <summary><paramref name="tops"/> moved beside <paramref name="target"/>, the caret kept in what it was in, and what <paramref name="bodies"/> names saying what it says.</summary>
    private ContentChange Placed(List<Entry> tops, Entry target, OutlineSide side, int caret, IReadOnlyDictionary<ContentPart, string>? bodies = null)
    {
        // The item the caret is in, and how far into what it says — where it is in one at all.
        var holding = Preorder(_forest).FirstOrDefault(entry => Body(entry) is var (from, to) && from <= caret && caret <= to);
        var into = holding is not null ? caret - Body(holding).From : 0;

        Put(tops, target, side);
        Say(bodies);

        return Written(placed => holding is not null ? placed[holding] + into : placed[tops[0]]);
    }

    /// <summary><paramref name="entries"/> taken from where they are and put beside <paramref name="target"/>.</summary>
    private void Put(List<Entry> entries, Entry target, OutlineSide side)
    {
        foreach (var entry in entries)
            if (entry.Item is not null) Siblings(entry).Remove(entry);

        var (into, at) = side switch
        {
            OutlineSide.Inside => (target.Children, target.Children.Count),
            OutlineSide.Before => (Siblings(target), Siblings(target).IndexOf(target)),
            _ => (Siblings(target), Siblings(target).IndexOf(target) + 1),
        };

        var parent = side == OutlineSide.Inside ? target : target.Parent;
        foreach (var entry in entries) entry.Parent = parent;

        into.InsertRange(at, entries);
    }

    /// <summary>Where what <paramref name="entry"/> says stands in the block as it is written now.</summary>
    private static (int From, int To) Body(Entry entry) =>
        entry.Line is { } line ? (line.Start + entry.Lead.Length, line.Start + entry.Lead.Length + entry.Body.Length) : (-1, -1);

    // ── Writing it ──────────────────────────────────────────────────────────

    /// <summary>
    /// The outline as it now stands, written over the stretch it was written in — only what changed — and the caret where
    /// <paramref name="caret"/> puts it, given where what each item says now starts in the stretch.
    /// </summary>
    private ContentChange Written(Func<IReadOnlyDictionary<Entry, int>, int> caret)
    {
        var printed = new System.Text.StringBuilder();
        var placed = new Dictionary<Entry, int>();

        Print(_forest, 0, []);

        var now = printed.ToString();
        var was = _text[(_start - _root.Start)..(_end - _root.Start)];
        var at = _start + caret(placed);

        // The first item written goes on a line of its own after the last line the block holds, which keeps its line ending.
        if (was.Length == 0 && _start == _root.Start + _text.Length)
        {
            var ended = _text.EndsWith('\n');
            var lead = ended || _text.Length == 0 ? string.Empty : _newline;

            return new ContentChange([new ContentWrite(_start, 0, lead + now + (ended ? _newline : string.Empty))], at + lead.Length);
        }

        // Nothing left of the outline: the line ending in front of it goes too, so no empty line is left where it was.
        if (now.Length == 0)
        {
            var from = _start - _root.Start;
            var back = from >= 2 && _text[(from - 2)..from] == "\r\n" ? 2 : from >= 1 && _text[from - 1] == '\n' ? 1 : 0;
            return new ContentChange([new ContentWrite(_start - back, _end - _start + back, string.Empty)], _start - back);
        }

        var same = 0;
        while (same < was.Length && same < now.Length && was[same] == now[same]) same++;

        var tail = 0;
        while (tail < was.Length - same && tail < now.Length - same && was[^(tail + 1)] == now[^(tail + 1)]) tail++;

        return new ContentChange([new ContentWrite(_start + same, was.Length - same - tail, now[same..^tail])], at);

        void Print(List<Entry> entries, int depth, IReadOnlyList<bool> above)
        {
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var place = new OutlinePlace(depth, [.. above, index == entries.Count - 1]);

                if (printed.Length > 0 || placed.Count > 0) printed.Append(_newline);
                printed.Append(entry.Before).Append(_spelled(place));
                placed[entry] = printed.Length;
                printed.Append(entry.Body);

                Print(entry.Children, depth + 1, place.Last);
            }
        }
    }

    /// <summary>
    /// Indentation as deep as the outline's own: the top items' lead, and as much again as the first item under one is indented past it
    /// at each level — four spaces where nothing is yet, or a tab where the outline is indented with tabs.
    /// </summary>
    private Func<OutlinePlace, string> Indented()
    {
        var top = _forest.FirstOrDefault()?.Lead ?? string.Empty;
        var under = Preorder(_forest).FirstOrDefault(entry => entry.Parent is not null && entry.Lead.Length > top.Length && entry.Lead.StartsWith(top, StringComparison.Ordinal));

        var step = under is not null ? under.Lead[top.Length..] : top.Contains('\t') ? "\t" : "    ";
        return place => top + string.Concat(Enumerable.Repeat(step, place.Depth));
    }
}

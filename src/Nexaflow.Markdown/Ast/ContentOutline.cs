namespace Nexaflow.Markdown.Ast;

/// <summary>
/// What a stage hangs on a piece it promotes into its reading's outline (<see cref="Kinds.Outline"/>): how high it ranks — one is the
/// highest, as a first-level heading is — what its words call it, before the outline tells repeats apart (<see cref="ContentOutline"/>),
/// and what it says, as an outline shows it.
/// </summary>
/// <param name="Rank">How high it ranks: a piece heads everything after it up to the next ranked as high or higher.</param>
/// <param name="Called">What its words call it — never empty — which a link names it by where nothing written before it is called the same.</param>
/// <param name="Title">What it says, as plain words on one line.</param>
public sealed record OutlineMark(int Rank, string Called, string Title)
{
    /// <summary>The mark hung on <paramref name="node"/>, or null where none is.</summary>
    public static OutlineMark? Of(ContentNode node)
    {
        foreach (var child in node.Children)
            if (child.IsDerived && child.Kind == Kinds.Outline)
                foreach (var held in child.Children)
                    if (held.Held is OutlineMark mark) return mark;

        return null;
    }
}

/// <summary>One piece a unit's stages promoted into the outline, where it stands in the unit.</summary>
/// <param name="Offset">Where the piece starts, counted in its unit.</param>
/// <param name="Block">Which of the unit's parts holds it.</param>
/// <param name="Before">How many blocks the unit shows before the part holding it.</param>
/// <param name="Rank">How high it ranks (<see cref="OutlineMark.Rank"/>).</param>
/// <param name="Called">What its words call it (<see cref="OutlineMark.Called"/>).</param>
/// <param name="Title">What it says (<see cref="OutlineMark.Title"/>).</param>
/// <param name="Heads">
/// Whether it is one of the reading's own blocks rather than a piece inside one — a heading in a quote is named, but heads nothing.
/// </param>
public sealed record OutlineEntry(int Offset, int Block, int Before, int Rank, string Called, string Title, bool Heads);

/// <summary>
/// A heading of an outline as a list of them hands one on — an outline panel's row: what it says and how high it ranks, to show, and
/// the name it answers to, by which the outline finds it again wherever the reading has moved it.
/// </summary>
/// <param name="Title">What it says.</param>
/// <param name="Rank">How high it ranks.</param>
/// <param name="Name">The name it answers to, which no other heading of the reading does.</param>
public sealed record OutlineHeading(string Title, int Rank, string Name);

/// <summary>
/// What one unit's blocks say to the outline: how many characters it stands for, how many blocks it shows — every part not derived and
/// not trivia — and the pieces in it promoted, in the order written.
///
/// <para>
/// Made from a unit's worked-over parts and a function of them alone, so a unit kept through a change keeps what it says. What the
/// outline works out about it — the names it settles on — the outline keeps on it.
/// </para>
/// </summary>
public sealed class OutlineUnit : IContentWidth
{
    private OutlineUnit(int width, int blocks, IReadOnlyList<OutlineEntry> entries)
    {
        this.Width = width;
        this.Blocks = blocks;
        this.Entries = entries;
        this.Names = new string?[entries.Count];
        this.Tried = new string[]?[entries.Count];

        foreach (var entry in entries)
            if (entry.Heads) this.Least = Math.Min(this.Least, entry.Rank);
    }

    /// <summary>
    /// What <paramref name="parts"/> say — a unit's parts, <paramref name="width"/> characters together, standing
    /// <paramref name="start"/> characters along from where the unit starts.
    /// </summary>
    public static OutlineUnit Of(IReadOnlyList<ContentNode> parts, int width, int start = 0)
    {
        var entries = new List<OutlineEntry>();
        var shown = 0;

        for (var block = 0; block < parts.Count; block++)
        {
            var part = parts[block];
            if (part.IsDerived) continue;

            Marked(part, part, block, shown, start, entries);
            if (part.Role != Roles.Trivia) shown++;
        }

        return new OutlineUnit(width, shown, entries);
    }

    /// <summary>How many characters it stands for.</summary>
    public int Width { get; }

    /// <summary>How many blocks it shows.</summary>
    public int Blocks { get; }

    /// <inheritdoc/>
    public int Spread => this.Blocks;

    /// <summary>The highest any of its blocks ranks — the least rank — or <see cref="int.MaxValue"/> where none heads anything.</summary>
    public int Least { get; } = int.MaxValue;

    /// <summary>The pieces promoted in it, in the order written.</summary>
    public IReadOnlyList<OutlineEntry> Entries { get; }

    /// <summary>Where it stands among the outline's units: keys only ever rise along the outline, whatever is put between two.</summary>
    internal long Key { get; set; }

    /// <summary>The name the outline settled on for each entry — null until it has.</summary>
    internal string?[] Names { get; }

    /// <summary>Every name each entry tried, the one it settled on last: a change to who holds any of them can change its name.</summary>
    internal string[]?[] Tried { get; }

    /// <summary>Gathers the pieces under <paramref name="node"/> that a stage promoted, each held by the unit's part <paramref name="block"/>.</summary>
    private static void Marked(ContentNode node, ContentNode part, int block, int before, int start, List<OutlineEntry> entries)
    {
        if (OutlineMark.Of(node) is { } mark && node.Offset is { } at)
            entries.Add(new OutlineEntry(at - start, block, before, mark.Rank, mark.Called, mark.Title, ReferenceEquals(node, part)));

        foreach (var child in node.Children)
            if (!child.IsDerived && !child.IsLeaf) Marked(child, part, block, before, start, entries);
    }
}

/// <summary>A section of an outline: the name its heading settled on, how high it ranks, where it starts and ends, and how many blocks it holds.</summary>
/// <param name="Name">The name its heading answers to.</param>
/// <param name="Rank">How high its heading ranks.</param>
/// <param name="Start">Where its heading starts.</param>
/// <param name="End">Where the next heading as high or higher starts, or where the outline ends.</param>
/// <param name="Behind">How many blocks it holds after its heading — what folding it hides.</param>
public sealed record OutlineSection(string Name, int Rank, int Start, int End, int Behind)
{
    /// <summary>Whether it holds anything after its heading, for folding it to hide.</summary>
    public bool Holds => this.Behind > 0;
}

/// <summary>
/// What a reading's blocks come to across its units — the same question in every language, so answered once: the pieces its language
/// promoted, ranked and named, in an index kept in step with the units.
///
/// <para>
/// <strong>Sections are an answer, not something hung on a tree.</strong> A heading stands over the blocks up to the next ranked as high
/// or higher, wherever in the reading that is: the index sums each unit's characters and blocks and gathers the highest rank in it, so
/// where a section ends, how many blocks it holds and which headings hold a place are each found down one path.
/// </para>
/// <para>
/// <strong>Names are numbered over the whole reading, in order</strong>, GitHub's way: a name already taken — by a heading before it,
/// including one written that way — is numbered <c>-1</c>, <c>-2</c>… Every entry remembers the names it tried. Units changed set off only
/// the entries after them that tried a name now taken or freed, in order, so a heading typed in renames only the headings that share
/// its name, and a heading anywhere costs nothing to every unit that holds no heading like it.
/// </para>
/// </summary>
public sealed class ContentOutline
{
    /// <summary>How far apart units put at the end stand, so many can be put between two before the keys are spread out again.</summary>
    private const long Gap = 1L << 32;

    /// <summary>A unit standing past every other, which a run of entries to the end is read up to.</summary>
    private static readonly OutlineUnit Past = Standing(long.MaxValue);

    private ContentIndex<OutlineUnit> _units = ContentIndex<OutlineUnit>.Empty;

    /// <summary>The entries holding each name — whatever its case, as a link names it.</summary>
    private readonly Dictionary<string, SortedSet<Spot>> _holding = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The entries that tried each name, whether or not they settled on it.</summary>
    private readonly Dictionary<string, SortedSet<Spot>> _trying = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The outline of <paramref name="tree"/>, a reading worked over whole: one unit, its parts counted from where it starts.</summary>
    public static ContentOutline Of(ContentNode tree)
    {
        var outline = new ContentOutline();
        outline.Replaced(0, 0, [OutlineUnit.Of(tree.Children, tree.Width, tree.Offset ?? 0)]);

        return outline;
    }

    /// <summary>How many units it holds.</summary>
    public int Count => _units.Count;

    /// <summary>How many characters its units stand for, together.</summary>
    public int Width => _units.Width;

    /// <summary>The unit at <paramref name="index"/>, and where it starts.</summary>
    public (OutlineUnit Unit, int Start) this[int index] => _units[index];

    /// <summary>Which unit holds <paramref name="offset"/> (<see cref="ContentIndex{T}.IndexAt"/>).</summary>
    public int IndexAt(int offset) => _units.IndexAt(offset);

    /// <summary>
    /// Replaces the units from <paramref name="from"/> up to <paramref name="to"/> with <paramref name="units"/>, numbering again every name
    /// the change reaches — and says which of the units it kept now answer to a name they did not.
    /// </summary>
    public IReadOnlyCollection<OutlineUnit> Replaced(int from, int to, IReadOnlyList<OutlineUnit> units)
    {
        var freed = new HashSet<string>(StringComparer.Ordinal);

        for (var index = from; index < to; index++)
        {
            var unit = _units[index].Item;

            for (var entry = 0; entry < unit.Entries.Count; entry++)
                if (Let(new Spot(unit, entry)) is { } name) freed.Add(name);
        }

        _units = _units.Replaced(from, to, units);
        Keyed(from, units.Count);

        var waiting = new SortedSet<Spot>(Spot.Order);

        foreach (var unit in units)
            for (var entry = 0; entry < unit.Entries.Count; entry++)
            {
                (unit.Names[entry], unit.Tried[entry]) = (null, null);
                waiting.Add(new Spot(unit, entry));
            }

        // Whatever tried a name the change let go of, after where it was, may take it now.
        var after = new Spot(from > 0 ? _units[from - 1].Item : null, int.MaxValue);
        foreach (var name in freed) Reach(name, after, waiting);

        return this.Numbered(waiting);
    }

    /// <summary>The name entry <paramref name="entry"/> of unit <paramref name="unit"/> answers to.</summary>
    public string Name(int unit, int entry) => _units[unit].Item.Names[entry]!;

    /// <summary>The unit and entry answering to <paramref name="name"/>, or null where none does.</summary>
    public (int Unit, int Entry)? Named(string name) =>
        _holding.TryGetValue(name, out var holders) && holders.Count > 0 ? (this.IndexOf(holders.Min.Unit!), holders.Min.Entry) : null;

    /// <summary>Every heading of one of the reading's own blocks, in the order written, as a list of them hands one on.</summary>
    public IEnumerable<OutlineHeading> Headings()
    {
        for (var index = 0; index < this.Count; index++)
        {
            var unit = _units[index].Item;

            for (var entry = 0; entry < unit.Entries.Count; entry++)
                if (unit.Entries[entry] is { Heads: true } heading) yield return new OutlineHeading(heading.Title, heading.Rank, unit.Names[entry]!);
        }
    }

    /// <summary>
    /// The first entry starting from <paramref name="from"/> up to <paramref name="to"/> that answers to <paramref name="name"/> — or, where
    /// none does, the first there whose words call it so: a link into a section names what is under it as it is called there, whatever a
    /// heading of the same words before the section made it answer to.
    /// </summary>
    public (int Unit, int Entry)? Within(int from, int to, string name)
    {
        if (this.Named(name) is { } named && this.StartOf(named.Unit, named.Entry) is var start && start >= from && start < to) return named;
        if (this.Count == 0 || from >= this.Width) return null;

        for (var index = _units.IndexAt(from); index < this.Count && _units[index].Start < to; index++)
        {
            var (unit, begins) = _units[index];

            for (var entry = 0; entry < unit.Entries.Count; entry++)
                if (begins + unit.Entries[entry].Offset is var at && at >= from && at < to
                    && string.Equals(unit.Entries[entry].Called, name, StringComparison.OrdinalIgnoreCase))
                    return (index, entry);
        }

        return null;
    }

    /// <summary>
    /// The heading of one of the reading's own blocks whose titles, from the outermost heading holding it down to its own, are
    /// <paramref name="titles"/> — each matched whatever its case and the space round it — or null where none's are.
    /// </summary>
    public (int Unit, int Entry)? Titled(IReadOnlyList<string> titles)
    {
        if (titles.Count == 0) return null;

        foreach (var (unit, entry, path) in this.TitlePaths())
            if (path.Count == titles.Count && path.SequenceEqual(titles.Select(title => title.Trim()), StringComparer.OrdinalIgnoreCase))
                return (unit, entry);

        return null;
    }

    /// <summary>
    /// Every heading of one of the reading's own blocks, in the order written, with its title path: the titles of the headings holding
    /// it, outermost first, then its own — what a heading is named by where repeats under different headings must stay apart.
    /// </summary>
    public IEnumerable<(int Unit, int Entry, IReadOnlyList<string> Titles)> TitlePaths()
    {
        var open = new List<(int Rank, string Title)>();

        for (var index = 0; index < this.Count; index++)
        {
            var unit = _units[index].Item;

            for (var entry = 0; entry < unit.Entries.Count; entry++)
            {
                if (unit.Entries[entry] is not { Heads: true } heading) continue;

                while (open.Count > 0 && open[^1].Rank >= heading.Rank) open.RemoveAt(open.Count - 1);
                open.Add((heading.Rank, heading.Title));

                yield return (index, entry, [.. open.Select(held => held.Title)]);
            }
        }
    }

    /// <summary>The unit and entry of the piece promoted starting at <paramref name="offset"/> — the outermost, where several do — or null.</summary>
    public (int Unit, int Entry)? EntryAt(int offset)
    {
        if (this.Count == 0) return null;

        var index = _units.IndexAt(offset);
        var (unit, start) = _units[index];

        for (var entry = 0; entry < unit.Entries.Count; entry++)
            if (unit.Entries[entry].Offset == offset - start) return (index, entry);

        return null;
    }

    /// <summary>Where the piece entry <paramref name="entry"/> of unit <paramref name="unit"/> stands for starts.</summary>
    public int StartOf(int unit, int entry) => _units[unit].Start + _units[unit].Item.Entries[entry].Offset;

    /// <summary>The section entry <paramref name="entry"/> of unit <paramref name="unit"/> heads, or null where it heads none.</summary>
    public OutlineSection? Section(int unit, int entry)
    {
        var (held, start) = _units[unit];
        if (held.Entries[entry] is not { Heads: true } heading) return null;

        var (endUnit, endEntry) = this.End(unit, entry, heading.Rank);
        var end = endUnit < this.Count ? _units[endUnit].Start + _units[endUnit].Item.Entries[endEntry].Offset : this.Width;

        return new OutlineSection(held.Names[entry]!, heading.Rank, start + heading.Offset, end,
                                  this.Shown(endUnit, endEntry) - this.Shown(unit, entry) - 1);
    }

    /// <summary>
    /// The sections holding the block that starts at <paramref name="offset"/>, nearest first — not one its own heading starts, which
    /// holds only what comes after it.
    /// </summary>
    public IEnumerable<OutlineSection> Holding(int offset)
    {
        if (this.Count == 0) yield break;

        var unit = _units.IndexAt(offset);
        var local = offset - _units[unit].Start;
        var entries = _units[unit].Item.Entries;

        var before = 0;
        while (before < entries.Count && entries[before].Offset < local) before++;

        // Each holding section is the nearest heading before ranked higher than the last one found.
        for (var under = int.MaxValue; unit >= 0;)
        {
            entries = _units[unit].Item.Entries;
            var found = -1;

            for (var entry = before - 1; entry >= 0 && found < 0; entry--)
                if (entries[entry] is { Heads: true } heading && heading.Rank < under) found = entry;

            if (found < 0)
            {
                unit = _units.LastBelow(unit, under);
                before = unit >= 0 ? _units[unit].Item.Entries.Count : 0;
                continue;
            }

            var section = this.Section(unit, found)!;
            yield return section;

            (under, before) = (section.Rank, found);
        }
    }

    // ── Sections ────────────────────────────────────────────────────────────

    /// <summary>Where the section the entry heads ends: the next heading ranked at most <paramref name="rank"/>, or past the last unit.</summary>
    private (int Unit, int Entry) End(int unit, int entry, int rank)
    {
        var entries = _units[unit].Item.Entries;

        for (var next = entry + 1; next < entries.Count; next++)
            if (entries[next] is { Heads: true } heading && heading.Rank <= rank) return (unit, next);

        for (var index = _units.NextAtMost(unit + 1, rank); index < this.Count; index = _units.NextAtMost(index + 1, rank))
        {
            entries = _units[index].Item.Entries;

            for (var next = 0; next < entries.Count; next++)
                if (entries[next] is { Heads: true } heading && heading.Rank <= rank) return (index, next);
        }

        return (this.Count, 0);
    }

    /// <summary>How many blocks the reading shows before the one holding the entry — all of them, past the last unit.</summary>
    private int Shown(int unit, int entry) =>
        unit < this.Count ? _units.Placed(unit).Spread + _units[unit].Item.Entries[entry].Before : _units.Spread;

    // ── Names ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Settles the name of every entry <paramref name="waiting"/> holds, in order, putting in line each entry after one that changed
    /// which name it holds that tried either name — and says which units held before now answer to a name they did not.
    /// </summary>
    private HashSet<OutlineUnit> Numbered(SortedSet<Spot> waiting)
    {
        var renamed = new HashSet<OutlineUnit>();

        while (waiting.Count > 0)
        {
            var spot = waiting.Min;
            waiting.Remove(spot);

            var (unit, entry) = (spot.Unit!, spot.Entry);
            var was = this.Let(spot);
            var called = unit.Entries[entry].Called;
            var tried = new List<string>();

            var name = called;
            for (var count = 1; this.Taken(name, spot); count++)
            {
                tried.Add(name);
                name = $"{called}-{count}";
            }

            tried.Add(name);
            (unit.Names[entry], unit.Tried[entry]) = (name, [.. tried]);

            Held(_holding, name, spot);
            foreach (var each in tried) Held(_trying, each, spot);

            if (was == name) continue;
            if (was is not null) renamed.Add(unit);

            if (was is not null) this.Reach(was, spot, waiting);
            this.Reach(name, spot, waiting);
        }

        return renamed;
    }

    /// <summary>Whether an entry before <paramref name="spot"/> holds <paramref name="name"/>.</summary>
    private bool Taken(string name, Spot spot) =>
        _holding.TryGetValue(name, out var holders) && holders.Count > 0 && Spot.Order.Compare(holders.Min, spot) < 0;

    /// <summary>Puts in line every entry after <paramref name="after"/> that tried <paramref name="name"/>.</summary>
    private void Reach(string name, Spot after, SortedSet<Spot> waiting)
    {
        if (!_trying.TryGetValue(name, out var tried) || tried.Count == 0) return;
        if (Spot.Order.Compare(after, tried.Max) >= 0) return;

        foreach (var spot in tried.GetViewBetween(after, new Spot(Past, 0)))
            if (Spot.Order.Compare(spot, after) > 0) waiting.Add(spot);
    }

    /// <summary>Lets go of every name the entry at <paramref name="spot"/> held or tried, and says the one it held.</summary>
    private string? Let(Spot spot)
    {
        var (unit, entry) = (spot.Unit!, spot.Entry);
        if (unit.Names[entry] is not { } name) return null;

        _holding[name].Remove(spot);
        foreach (var each in unit.Tried[entry]!) _trying[each].Remove(spot);

        return name;
    }

    private static void Held(Dictionary<string, SortedSet<Spot>> names, string name, Spot spot)
    {
        if (!names.TryGetValue(name, out var spots)) names[name] = spots = new SortedSet<Spot>(Spot.Order);
        spots.Add(spot);
    }

    // ── Where units stand ───────────────────────────────────────────────────

    /// <summary>Gives the <paramref name="count"/> units put in at <paramref name="from"/> keys between those of the units either side.</summary>
    private void Keyed(int from, int count)
    {
        if (count == 0) return;

        var left = from > 0 ? _units[from - 1].Item.Key : 0;
        var right = from + count < this.Count ? _units[from + count].Item.Key : long.MaxValue;

        var step = right == long.MaxValue ? Gap : (right - left) / (count + 1);
        if (step < 1 || (right == long.MaxValue && left > long.MaxValue - (Gap * (count + 1))))
        {
            var key = 0L;
            foreach (var (unit, _) in _units.From(0)) unit.Key = key += Gap;

            return;
        }

        for (var at = 0; at < count; at++) _units[from + at].Item.Key = left + (step * (at + 1));
    }

    /// <summary>A unit with nothing in it standing at <paramref name="key"/>.</summary>
    private static OutlineUnit Standing(long key)
    {
        var unit = OutlineUnit.Of([], 0);
        unit.Key = key;

        return unit;
    }

    /// <summary>Where <paramref name="unit"/> stands among the units, found by its key.</summary>
    private int IndexOf(OutlineUnit unit)
    {
        var (low, high) = (0, this.Count - 1);

        while (low <= high)
        {
            var middle = (low + high) / 2;
            var key = _units[middle].Item.Key;

            if (key == unit.Key) return middle;
            if (key < unit.Key) low = middle + 1;
            else high = middle - 1;
        }

        throw new InvalidOperationException("An entry's unit is not among the outline's.");
    }

    /// <summary>An entry: its unit and which of the unit's entries it is, ordered as the outline reads them.</summary>
    private readonly record struct Spot(OutlineUnit? Unit, int Entry)
    {
        public static readonly IComparer<Spot> Order = Comparer<Spot>.Create(static (a, b) =>
            (a.Unit?.Key ?? 0).CompareTo(b.Unit?.Key ?? 0) is var units and not 0 ? units : a.Entry.CompareTo(b.Entry));
    }
}

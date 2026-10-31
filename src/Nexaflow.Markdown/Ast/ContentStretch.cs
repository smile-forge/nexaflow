namespace Nexaflow.Markdown.Ast;

/// <summary>
/// A stretch of a long reading a windowed parse is asked for: read <paramref name="Window"/> from its first character, and once
/// <paramref name="SoftMaxLength"/> characters are behind, stop at the next place where nothing is left open — the end of whatever
/// the reading is inside — saying where that was (<see cref="ContentParse.Stopped"/>) and what it needs to carry on from there
/// (<see cref="ContentParse.Resume"/>).
///
/// <para>
/// <strong>A window, not the document.</strong> The parser is handed the characters from where the stretch starts, a little past
/// its length, and every offset it hands back is from the window's first character — so what it reads is a unit with offsets of
/// its own, and nothing it read is rewritten when what is written before it moves. Where the window ends before anywhere to stop
/// and before the end of the reading (<paramref name="Ends"/>), the parser says it needs more (<see cref="ContentParse.NeedsMore"/>)
/// and is handed a window twice as long: a parser never learns how long the reading is, or whether it is a document or a body
/// written five deep inside one.
/// </para>
/// <para>
/// Where to stop is the parser's to say, because only it knows what is open: a markdown list, a fence, an RTF group. A container
/// longer than <paramref name="SoftMaxLength"/> is read whole, so a stretch never ends part-way through one.
/// </para>
/// </summary>
/// <param name="Window">The reading's characters from where the stretch starts. Every offset the parse hands back is its own.</param>
/// <param name="From">Where the window starts in the reading — nought for the first stretch, which is all a parser may ask of it.</param>
/// <param name="Ends">Whether the window runs to the end of the reading, so its end is the end of what is written.</param>
/// <param name="SoftMaxLength">How far to read before looking for somewhere to stop.</param>
/// <param name="Resume">What the stretch before this one stopped in (<see cref="ContentParse.Resume"/>), or null for the first.</param>
public sealed record ContentStretch(string Window, int From, bool Ends, int SoftMaxLength, object? Resume = null);

/// <summary>
/// One unit of a reading, as it was read: how many characters it stands for, the state it was read from and the one it stopped
/// in, and its tree — every offset in it from the unit's own start, so it reads the same wherever the unit stands.
/// </summary>
/// <param name="Width">How many characters it stands for: from where it starts to where the next starts.</param>
/// <param name="Begun">What it was read from: the unit before's <see cref="Ended"/>.</param>
/// <param name="Ended">What it stopped in (<see cref="ContentParse.Resume"/>).</param>
/// <param name="Last">Whether it read to the end of the reading.</param>
/// <param name="Tree">What it read to: a block in the reading's language, standing for its characters and no others, from nought.</param>
public sealed record ContentUnit(int Width, object? Begun, object? Ended, bool Last, ContentNode Tree) : IContentWidth
{
    /// <summary>How long a window is asked for first, past the length the parse is asked to read before stopping.</summary>
    private const int Slack = 4096;

    /// <summary>Its tree where it was last placed, and where that was.</summary>
    private Placing? _placed;

    private sealed record Placing(int Start, ContentNode Tree);

    /// <summary>
    /// Its tree, standing where the unit starts in the reading: <see cref="Tree"/> moved along by <paramref name="start"/> — worked
    /// out once for each place it is asked to stand, and kept while it stands there.
    /// </summary>
    public ContentNode Placed(int start)
    {
        if (start == 0) return this.Tree;
        if (_placed is { } placed && placed.Start == start) return placed.Tree;

        var tree = this.Tree.FurtherOn(start);
        _placed = new Placing(start, tree);

        return tree;
    }

    /// <summary>
    /// The unit of <paramref name="characters"/> starting at <paramref name="from"/>, read by <paramref name="parse"/> from
    /// <paramref name="begun"/>: handed a window a little past <paramref name="softMaxLength"/>, and one twice as long each time it
    /// says it needs more.
    /// </summary>
    public static ContentUnit Read(Func<ContentStretch, ContentParse> parse, IContentCharacters characters, int from, int softMaxLength,
                                   object? begun)
    {
        var left = characters.Length - from;
        var length = (int)Math.Min(left, (long)Math.Max(softMaxLength, 1) + Slack);

        while (true)
        {
            var ends = length == left;
            var read = parse(new ContentStretch(characters.Slice(from, length), from, ends, softMaxLength, begun));

            if (read.NeedsMore && !ends)
            {
                length = (int)Math.Min(left, 2L * length);
                continue;
            }

            if (read.Stopped is null && !ends)
                throw new InvalidOperationException($"A windowed parse read to the end of a window short of the end, from {from}, and neither stopped nor said it needs more.");

            return new ContentUnit(read.Stopped ?? length, begun, read.Resume, read.Stopped is null, read.Tree);
        }
    }
}

/// <summary>
/// A long reading, read a unit at a time (<see cref="ContentStretch"/>): the units read so far, in order and indexed by how wide
/// each is (<see cref="ContentIndex{T}"/>), and the one tree they come to.
///
/// <para>
/// <strong>The tree prints as the whole reading</strong>, read or not: every unit's own parts in order, each placed where the
/// index says it starts, then what is still unread as one leaf of trivia standing for those characters without copying them
/// (<see cref="Kinds.Unreached"/>), which nothing draws. What a unit hangs under its block as derived parts is said by the last
/// unit taken, which has read the most.
/// </para>
/// <para>
/// <strong>Immutable.</strong> Reading another unit, or reading again where something changed, hands back another, sharing every
/// unit it did not read — so a reading carried on away from the thread that lays it never disturbs the one being laid.
/// </para>
/// </summary>
public sealed class ContentUnits
{
    private readonly ContentIndex<ContentUnit> _units;

    /// <summary>Nothing read yet of a reading in <paramref name="language"/>.</summary>
    public ContentUnits(string language) : this(language, ContentIndex<ContentUnit>.Empty) { }

    private ContentUnits(string language, ContentIndex<ContentUnit> units)
    {
        this.Language = language;
        _units = units;
    }

    /// <summary>The language the reading is written in, which its tree says.</summary>
    public string Language { get; }

    /// <summary>How many units are read.</summary>
    public int Count => _units.Count;

    /// <summary>The unit at <paramref name="index"/>, and where it starts in the reading.</summary>
    public (ContentUnit Unit, int Start) this[int index] => _units[index];

    /// <summary>Whether the last unit read reached the end of the reading.</summary>
    public bool Done => this.Count > 0 && _units[this.Count - 1].Item.Last;

    /// <summary>Where what has been read ends: where the next unit starts.</summary>
    public int Reached => _units.Width;

    /// <summary>The unit holding <paramref name="offset"/> — the last, at or past the end of what is read — or -1 for none.</summary>
    public int IndexAt(int offset) => _units.IndexAt(offset);

    /// <summary>The units from <paramref name="index"/> on, each with where it starts.</summary>
    public IEnumerable<(ContentUnit Unit, int Start)> From(int index) => _units.From(index);

    /// <summary>The same, with <paramref name="unit"/> after the last read; it must not follow one that read to the end.</summary>
    public ContentUnits Added(ContentUnit unit)
    {
        if (this.Done) throw new InvalidOperationException("A unit cannot follow one that read to the end of the reading.");

        return new(this.Language, _units.Added(unit));
    }

    /// <summary>The same, with the next unit of <paramref name="characters"/> read by <paramref name="parse"/> — or itself, once everything is read.</summary>
    public ContentUnits ReadOn(Func<ContentStretch, ContentParse> parse, IContentCharacters characters, int softMaxLength) =>
        this.Done ? this : this.Added(ContentUnit.Read(parse, characters, this.Reached, softMaxLength, this.Count == 0 ? null : _units[this.Count - 1].Item.Ended));

    /// <summary>
    /// The reading of <paramref name="characters"/> as one tree: the own parts of the first <paramref name="count"/> units — all
    /// of them, where it says none — each standing where it starts, what is not among them held as a leaf standing for those
    /// characters, and what the last of them said about the whole.
    /// </summary>
    public ContentNode Tree(IContentCharacters characters, int? count = null)
    {
        var taken = Math.Min(count ?? this.Count, this.Count);
        var parts = new List<ContentNode>();
        var (reached, last) = (0, (ContentNode?)null);

        foreach (var (unit, start) in _units.From(0))
        {
            if (taken-- <= 0) break;

            last = unit.Placed(start);
            foreach (var part in last.Children)
                if (!part.IsDerived) parts.Add(part);

            reached = start + unit.Width;
        }

        if (reached < characters.Length)
            parts.Add(ContentNode.Leaf(Kinds.Unreached, characters, reached, characters.Length - reached, Roles.Trivia, offset: reached));

        if (last is not null)
            foreach (var part in last.Children)
                if (part.IsDerived) parts.Add(part);

        return new BlockNode(this.Language, parts, offset: characters.Length > 0 ? 0 : null);
    }

    /// <summary>
    /// The reading once the characters from <paramref name="changedFrom"/> to <paramref name="changedTo"/> have been written over,
    /// moving everything after them along by <paramref name="by"/> — read again only where it has to be, from
    /// <paramref name="edited"/>, the characters as they are now.
    ///
    /// <para>
    /// The units before the change are kept as they are. A change on the first line of the unit it falls in is read from the unit
    /// before, because the first line of a block can change what the block before it is — a <c>===</c> under a paragraph makes it a
    /// heading — and nothing further back; one further into its unit is read from the unit's own start, since what the unit before
    /// stopped in is the same. So writing just after a long fence never reads the fence again. It is read on only until a unit stops
    /// where one of the old units after the change started, moved
    /// along, and stops in exactly the state that unit started in: from there every old unit reads as it did, and is kept as it
    /// is — its offsets are its own, so nothing in it moves. A change that leaves a fence open, or an RTF group, simply reads on
    /// further, as far as it reaches. What was not read before is still not read.
    /// </para>
    /// </summary>
    /// <param name="enough">
    /// How far reading again has to go before it may stop without having come back into step: past there, what was read before is
    /// let go and left unread, for whatever reads the rest to read again — so a change that moves everything after it, a heading
    /// renamed, costs what is needed and not the whole reading.
    /// </param>
    /// <returns>
    /// The reading, how many of its units are the old ones before the change, read as they were, and how many after those were read
    /// again.
    /// </returns>
    public (ContentUnits Units, int Kept, int Read) Edited(int changedFrom, int changedTo, int by, IContentCharacters edited,
                                                 Func<ContentStretch, ContentParse> parse, int softMaxLength, int enough = int.MaxValue)
    {
        if (this.Count == 0) return (new ContentUnits(this.Language), 0, 0);

        var holding = _units.IndexAt(changedFrom);
        var kept = holding > 0 && Opening(edited, _units[holding].Start, changedFrom) ? holding - 1 : holding;
        var (first, from) = _units[kept];
        var begun = first.Begun;
        var next = Math.Max(_units.IndexAt(changedTo), kept);
        var reread = new List<ContentUnit>();

        while (true)
        {
            // Asked to stop where the next old unit past the change starts, moved along — a block starts there still, unless the
            // change reached it — so a unit stops in step with the old ones rather than a few characters off them for ever.
            while (next < this.Count && (_units[next].Start < changedTo || _units[next].Start + by <= from)) next++;

            var length = next < this.Count ? _units[next].Start + by - from : softMaxLength;
            var read = ContentUnit.Read(parse, edited, from, length, begun);
            reread.Add(read);

            if (read.Last) return (new(this.Language, _units.Replaced(kept, this.Count, reread)), kept, reread.Count);

            var stop = from + read.Width;

            if (next < this.Count && _units[next] is var (old, start) && start + by == stop && Equals(old.Begun, read.Ended))
                return (new(this.Language, _units.Replaced(kept, next, reread)), kept, reread.Count);

            if ((!this.Done && stop >= this.Reached + by) || stop >= enough)
                return (new(this.Language, _units.Replaced(kept, this.Count, reread)), kept, reread.Count);

            (from, begun) = (stop, read.Ended);
        }
    }

    /// <summary>Whether <paramref name="at"/> is on the first line of what starts at <paramref name="start"/>: no line break stands between them.</summary>
    private static bool Opening(IContentCharacters characters, int start, int at)
    {
        foreach (var chunk in characters.Chunks(start, Math.Min(at, characters.Length)))
            if (chunk.Span.IndexOfAny('\n', '\r') >= 0) return false;

        return true;
    }
}

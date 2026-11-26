namespace Nexaflow.Markdown.Ast;

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
/// <para>
/// <strong>Read where the reader is, where it can start anywhere.</strong> A reading in a language whose every line start is
/// somewhere to start reading — text, code — need not read everything before what it reads (<see cref="Skips"/>): what it has not
/// come to is held as stretches skipped (<see cref="ContentUnit.Skipped"/>), each only a width, read into where the reader comes
/// (<see cref="ReadInto"/>) and let go of again far from them (<see cref="LetGo"/>). So a file the size of the disk costs the
/// stretches read near the reader, and a jump to its end reads its end. A change into a stretch skipped reads nothing.
/// </para>
/// </summary>
public sealed class ContentUnits
{
    private readonly ContentIndex<ContentUnit> _units;

    /// <summary>Nothing read yet of a reading in <paramref name="language"/>.</summary>
    public ContentUnits(string language) : this(language, ContentIndex<ContentUnit>.Empty, null) { }

    private ContentUnits(string language, ContentIndex<ContentUnit> units, IReadingSyntax? syntax)
    {
        this.Language = language;
        _units = units;
        this.Syntax = syntax;
    }

    /// <summary>
    /// Whether the reading can start at any line start, and so leaves what the reader has not come to skipped rather than reading everything
    /// before what it reads.
    /// </summary>
    public bool Skips { get; private init; }

    /// <summary>
    /// A reading of <paramref name="length"/> characters in <paramref name="language"/> that can start at any line start: <paramref name="first"/>,
    /// read from the start, and the rest skipped until something reads into it.
    /// </summary>
    public static ContentUnits Skipping(string language, ContentUnit first, int length, IReadingSyntax? syntax)
    {
        var units = ContentIndex<ContentUnit>.Empty.Added(first);
        if (!first.Last && length > first.Width) units = units.Added(ContentUnit.Skipping(length - first.Width, last: true));

        return new ContentUnits(language, units, syntax) { Skips = true };
    }

    /// <summary>Whether any of it is skipped, not read.</summary>
    public bool Skipped
    {
        get
        {
            foreach (var (unit, _) in _units.From(0))
                if (unit.Skipped) return true;

            return false;
        }
    }

    /// <summary>
    /// The reading with the stretch skipped at <paramref name="index"/> read where <paramref name="offset"/> is: from the start of the line it is
    /// on — a line with something written on it, as every unit starts, looking back no further than <paramref name="softMaxLength"/> characters
    /// nor past where the stretch starts — a unit at a time, at most <paramref name="most"/> of them, up to where the stretch ends; what is left
    /// either side stays skipped. A unit read past where the stretch ends — into a unit read before that starts on a line with nothing written,
    /// which a reading stops past — reads the units it reached again with it. Itself, where the unit at <paramref name="index"/> is read.
    /// </summary>
    public ContentUnits ReadInto(int index, int offset, Func<ContentStretch, ContentParse> parse, IContentCharacters characters, int softMaxLength, int most)
    {
        var (skipped, from) = _units[index];
        if (!skipped.Skipped) return this;

        var (end, endsLast, replaced) = (from + skipped.Width, skipped.Last, index + 1);

        // Near the end of the reading, the last stretch's worth of it is read — as reading on from the start ends on a stretch, not on the last
        // line alone — and a place at the very end of a stretch reads the line that ends there.
        var place = skipped.Last && offset > end - softMaxLength ? Math.Max(from, end - softMaxLength) : offset;
        var at = LineStart(characters, from, Math.Clamp(place, from, Math.Max(from, end - 1)), softMaxLength);
        var parts = new List<ContentUnit>();

        if (at > from) parts.Add(ContentUnit.Skipping(at - from, last: false));

        var (reached, read) = (at, 0);

        while (true)
        {
            // Asked to stop no later than where the stretch ends — a line start, where a reading that starts at any line start stops — and
            // handed the characters past it, so it reads to there without taking it for the end of the reading.
            for (; read < most && reached < end; read++)
            {
                var unit = ContentUnit.Read(parse, characters, reached, Math.Min(softMaxLength, end - reached), null);
                parts.Add(unit);
                reached += unit.Width;
            }

            if (reached <= end) break;

            // Read past the end into units read before: those it reached go, and are read again to where the last of them ended.
            while (replaced < this.Count && reached > end)
            {
                var (next, start) = _units[replaced++];
                (end, endsLast) = (start + next.Width, next.Last);
            }

            if (reached >= end) break;

            most = int.MaxValue;
        }

        if (reached < end) parts.Add(ContentUnit.Skipping(end - reached, endsLast));

        return this.With(_units.Replaced(index, replaced, parts), this.Syntax);
    }

    /// <summary>The reading with every stretch skipped read: all of it, for whatever has to have all of it.</summary>
    public ContentUnits ReadAllSkipped(Func<ContentStretch, ContentParse> parse, IContentCharacters characters, int softMaxLength)
    {
        var units = this;

        for (var index = 0; index < units.Count; index++)
            if (units[index].Unit.Skipped) units = units.ReadInto(index, units[index].Start, parse, characters, softMaxLength, int.MaxValue);

        return units;
    }

    /// <summary>
    /// The reading with the unit at <paramref name="index"/> let go of — skipped again, joined to any stretch skipped either side — so a reading
    /// far from where the reader is holds no more than its width. Itself, where the reading cannot start anywhere.
    /// </summary>
    public ContentUnits LetGo(int index)
    {
        if (!this.Skips) return this;

        var (first, last) = (index, index);
        while (first > 0 && _units[first - 1].Item.Skipped) first--;
        while (last + 1 < this.Count && _units[last + 1].Item.Skipped) last++;

        var width = 0;
        for (var at = first; at <= last; at++) width += _units[at].Item.Width;

        return this.With(_units.Replaced(first, last + 1, [ContentUnit.Skipping(width, _units[last].Item.Last)]), this.Syntax);
    }

    /// <summary>The same language, and whether it skips, holding <paramref name="units"/> and keeping <paramref name="syntax"/>.</summary>
    private ContentUnits With(ContentIndex<ContentUnit> units, IReadingSyntax? syntax) => new(this.Language, units, syntax) { Skips = this.Skips };

    /// <summary>
    /// Where the line holding <paramref name="offset"/> starts — or, where that line has nothing written on it, the last line before it that
    /// does, since a reading stops past blank lines, never before one — looking back no further than <paramref name="softMaxLength"/> characters
    /// nor past <paramref name="from"/>; and <paramref name="from"/> itself, where no such line starts that near.
    /// </summary>
    private static int LineStart(IContentCharacters characters, int from, int offset, int softMaxLength)
    {
        var low = Math.Max(from, offset - Math.Max(softMaxLength, 1));
        if (offset <= low) return from;

        var looked = characters.Slice(low, offset - low + 1 <= characters.Length - low ? offset - low + 1 : offset - low);

        for (var cut = Math.Min(offset - low, looked.Length) - 1; cut >= 0; cut--)
        {
            if (looked[cut] is not ('\n' or '\r')) continue;

            // The \r of a \r\n is not the end of the break; a line with nothing written on it is not where a unit starts.
            var after = low + cut + 1;
            if (after < characters.Length && characters[after] is '\n' or '\r') continue;

            return after;
        }

        return from;
    }

    /// <summary>
    /// What the reading's language keeps of all of it beside its units — code's syntax tree — carried through every change, or null for a
    /// language that keeps nothing.
    /// </summary>
    public IReadingSyntax? Syntax { get; }

    /// <summary>The same reading keeping <paramref name="syntax"/> of all of it beside its units.</summary>
    public ContentUnits WithSyntax(IReadingSyntax? syntax) => this.With(_units, syntax);

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

    /// <summary>The reading of the body held by the holder starting at <paramref name="at"/> in the reading, or null where none is read.</summary>
    public ContentBody? Body(int at)
    {
        if (this.Count == 0) return null;

        var (unit, start) = _units[_units.IndexAt(at)];
        return unit.Body(at - start);
    }

    /// <summary>The same reading holding <paramref name="body"/> as the reading of the body held by the holder starting at <paramref name="at"/>.</summary>
    public ContentUnits Holding(int at, ContentBody body)
    {
        var index = _units.IndexAt(at);
        var (unit, start) = _units[index];

        return this.With(_units.Replaced(index, index + 1, [unit.Holding(at - start, body)]), this.Syntax);
    }

    /// <summary>
    /// The reading of the body held by the holder starting at <paramref name="at"/> — written in the language called
    /// <paramref name="language"/>, read by <paramref name="reader"/> from <paramref name="characters"/>, the characters this reading reads —
    /// and the reading holding it; or null where nothing starting there holds a body in that language.
    /// </summary>
    public (ContentUnits Units, ContentBody Body)? Reading(int at, string language, ContentReader reader, IContentCharacters characters)
    {
        if (this.Count == 0) return null;

        var (unit, start) = _units[_units.IndexAt(at)];

        if (HolderAt(unit.Tree, at - start, language) is not { } holder
            || holder.Children.FirstOrDefault(child => child.Kind == Kinds.Nested) is not { Offset: { } offset } held) return null;

        var body = ContentBody.Read(language, ContentSpace.Of(characters, start + offset, held), offset - (at - start), held.Width, reader);
        return (this.Holding(at, body), body);
    }

    /// <summary>The same, with <paramref name="unit"/> after the last read; it must not follow one that read to the end.</summary>
    public ContentUnits Added(ContentUnit unit)
    {
        if (this.Done) throw new InvalidOperationException("A unit cannot follow one that read to the end of the reading.");

        return this.With(_units.Added(unit), this.Syntax);
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

        // A reading of one unit is that unit's tree, as its language read it — standing nowhere where there is nothing to read.
        if (taken == 1 && this.Count == 1 && _units[0].Item is { Last: true } whole)
            return characters.Length > 0 || whole.Tree.Offset is null ? whole.Tree : whole.Tree.With(whole.Tree.Children, null);
        var parts = new List<ContentNode>();
        var (reached, last) = (0, (ContentNode?)null);

        foreach (var (unit, start) in _units.From(0))
        {
            if (taken-- <= 0) break;

            // A stretch skipped stands for its characters, as what is not read yet does.
            if (unit.Skipped)
            {
                parts.Add(ContentNode.Leaf(Kinds.Unreached, characters, start, unit.Width, Roles.Trivia, offset: start));
                reached = start + unit.Width;
                continue;
            }

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
    /// <para>
    /// <strong>The bodies read inside go with their holders</strong> (<see cref="ContentUnit.Bodies"/>). A unit kept keeps its own.
    /// One written inside — where the reading's language says the body still ends where it did — is written into, and reads again
    /// only the units of its own the change reached; and one whose unit is read again is carried to the holder that comes back in
    /// the same language at the same place, the change mapped through, unchanged where the change missed it and written into where
    /// it did not. Where no holder comes back, it is let go, to be read afresh when something asks for it.
    /// </para>
    /// </summary>
    /// <param name="bounds">
    /// What the reading's language says about a body it holds when something is written inside it, so a change wholly inside one
    /// reads nothing again where the body still ends where it did — or null, for a language that says nothing.
    /// </param>
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
                                                 Func<ContentStretch, ContentParse> parse, int softMaxLength, int enough = int.MaxValue,
                                                 IBodyBounds? bounds = null)
    {
        // What the language keeps of the whole reading is told of the change, whatever of the units is read again.
        var syntax = this.Syntax?.Edited(changedFrom, changedTo, by, edited);

        if (this.Count == 0) return (new ContentUnits(this.Language).WithSyntax(syntax), 0, 0);

        var holding = _units.IndexAt(changedFrom);

        // Written inside a body another reading reads, which its holder's language says still ends where it did: nothing is read
        // again, and the unit holding it only stands for more or fewer characters.
        if (bounds is not null && this.Grown(holding, changedFrom, changedTo, by, edited, bounds) is { } grown)
            return (this.With(_units.Replaced(holding, holding + 1, [grown]), syntax), holding, 0);

        // Written into a stretch skipped: nothing is read — what the change reached is skipped, as wide as it is now.
        if (this.Skips && this.SkippedOver(changedFrom, changedTo, by, syntax) is { } skipped) return (skipped, holding, 0);

        // Never back into a stretch skipped: what it holds was never read, so nothing read there can change.
        var kept = holding > 0 && !_units[holding - 1].Item.Skipped && Opening(edited, _units[holding].Start, changedFrom) ? holding - 1 : holding;
        var (first, from) = _units[kept];
        var begun = first.Begun;
        var next = Math.Max(_units.IndexAt(changedTo), kept);
        var reread = new List<ContentUnit>();

        while (true)
        {
            // Asked to stop where the next old unit past the change starts, moved along — a block starts there still, unless the
            // change reached it — so a unit stops in step with the old ones rather than a few characters off them for ever.
            while (next < this.Count && (_units[next].Start < changedTo || _units[next].Start + by <= from)) next++;

            // A reading read as one unit reads to its end, wherever the change was.
            var length = next < this.Count && softMaxLength != int.MaxValue ? _units[next].Start + by - from : softMaxLength;
            var read = ContentUnit.Read(parse, edited, from, length, begun);
            reread.Add(read);

            if (read.Last) return this.Reread(kept, this.Count, reread, changedFrom, changedTo, by, edited, syntax);

            var stop = from + read.Width;

            if (next < this.Count && _units[next] is var (old, start) && start + by == stop && Equals(old.Begun, read.Ended))
                return this.Reread(kept, next, reread, changedFrom, changedTo, by, edited, syntax);

            // Read on into a stretch skipped: what is left of it stays skipped — or, read past the whole of it, reading carries on as past one read.
            if (next < this.Count && _units[next] is var (passed, at) && passed.Skipped && stop > at + by)
            {
                var ends = at + by + passed.Width;

                if (stop < ends)
                {
                    reread.Add(ContentUnit.Skipping(ends - stop, passed.Last));
                    return this.Reread(kept, next + 1, reread, changedFrom, changedTo, by, edited, syntax);
                }

                next++;
                (from, begun) = (stop, read.Ended);
                continue;
            }

            // Far enough past the change, a reading that skips leaves the rest skipped rather than letting it go unread.
            if (this.Skips && stop >= enough && stop < edited.Length)
            {
                reread.Add(ContentUnit.Skipping(edited.Length - stop, last: true));
                return this.Reread(kept, this.Count, reread, changedFrom, changedTo, by, edited, syntax);
            }

            if ((!this.Done && stop >= this.Reached + by) || stop >= enough)
                return this.Reread(kept, this.Count, reread, changedFrom, changedTo, by, edited, syntax);

            (from, begun) = (stop, read.Ended);
        }
    }

    /// <summary>
    /// Where the characters from <paramref name="from"/> to <paramref name="to"/> written over touch a stretch skipped: the reading with every
    /// unit the change reached skipped as one stretch, joined to any skipped either side and as wide as it is now — or null where the change
    /// reached only units read.
    /// </summary>
    private ContentUnits? SkippedOver(int from, int to, int by, IReadingSyntax? syntax)
    {
        var (low, high) = (_units.IndexAt(from), _units.IndexAt(Math.Max(from, to - 1)));
        if (low < 0) return null;

        var touched = false;
        for (var at = low; at <= high && !touched; at++) touched = _units[at].Item.Skipped;
        if (!touched) return null;

        while (low > 0 && _units[low - 1].Item.Skipped) low--;
        while (high + 1 < this.Count && _units[high + 1].Item.Skipped) high++;

        var width = by;
        for (var at = low; at <= high; at++) width += _units[at].Item.Width;

        return this.With(_units.Replaced(low, high + 1, [ContentUnit.Skipping(width, _units[high].Item.Last)]), syntax);
    }

    /// <summary>
    /// The reading with the units from <paramref name="kept"/> up to <paramref name="next"/> replaced by <paramref name="reread"/>,
    /// the bodies they held carried to the units read again — and how many units are kept before them and how many were read.
    /// </summary>
    private (ContentUnits Units, int Kept, int Read) Reread(int kept, int next, List<ContentUnit> reread, int from, int to, int by,
                                                           IContentCharacters edited, IReadingSyntax? syntax)
    {
        var (start, left) = (_units[kept].Start, next - kept);

        foreach (var (unit, at) in _units.From(kept))
        {
            if (left-- <= 0) break;

            foreach (var (held, body) in unit.Bodies)
            {
                var was = at + held;
                if (from < to && from <= was && was < to) continue;

                var now = was < from ? was : was + by;
                var (index, local) = (0, now - start);

                while (index < reread.Count - 1 && local >= reread[index].Width) local -= reread[index++].Width;

                if (Carried(body, reread[index].Tree, local, was, now, from, to, by, edited) is { } carried)
                    reread[index] = reread[index].Holding(local, carried);
            }
        }

        return (this.With(_units.Replaced(kept, next, reread), syntax), kept, reread.Count);
    }

    /// <summary>
    /// <paramref name="body"/>, read while its holder started at <paramref name="was"/>, carried through the characters from
    /// <paramref name="from"/> to <paramref name="to"/> being written over to the holder starting at <paramref name="local"/> in
    /// <paramref name="tree"/> — <paramref name="now"/> in the reading: kept as it is where the change missed it, written into where it
    /// did not — or null where nothing in the same language comes back there, or its body no longer starts where it did.
    /// </summary>
    private static ContentBody? Carried(ContentBody body, ContentNode tree, int local, int was, int now, int from, int to, int by,
                                        IContentCharacters edited)
    {
        if (HolderAt(tree, local, body.Language) is not { } holder
            || holder.Children.FirstOrDefault(child => child.Kind == Kinds.Nested) is not { Offset: { } offset } held
            || offset - local != body.Lead) return null;

        var (begins, ends) = (was + body.Lead, was + body.Lead + body.Width);
        var touched = from == to ? begins <= from && from < ends : from < ends && to > begins;
        if (!touched && held.Width == body.Width) return body;

        var space = ContentSpace.Of(edited, now + body.Lead, held);
        var own = body.Space;

        // What the body holds before the change reads as it did — as far as the body still reaches: a change can end it sooner.
        var start = Math.Min(own.OwnAt(Math.Clamp(from - begins, 0, body.Width)), space.Length);

        // What the body holds past the change reads as it did only where the body still ends where it did, moved along.
        var end = to <= ends && ends + by == now + body.Lead + held.Width ? own.OwnAt(Math.Clamp(to - begins, 0, body.Width)) : own.Length;

        return body.Edited(start, Math.Max(start, end), space.Length - own.Length, space, body.Lead, held.Width);
    }

    /// <summary>The piece of <paramref name="node"/> starting at <paramref name="at"/> that holds a body written in <paramref name="language"/>, or null.</summary>
    private static ContentNode? HolderAt(ContentNode node, int at, string language)
    {
        if (node.IsDerived) return null;

        if (node.Offset is { } offset)
        {
            if (at < offset || at >= offset + node.Width) return null;
            if (offset == at && ContentNested.Language(node) == language) return node;
        }

        foreach (var child in node.Children)
            if (HolderAt(child, at, language) is { } found) return found;

        return null;
    }

    /// <summary>
    /// The unit at <paramref name="index"/> once the characters from <paramref name="from"/> to <paramref name="to"/> are written
    /// over, where that is wholly inside the characters one body it holds has in its own language and <paramref name="bounds"/> says
    /// the body still ends where it did: the piece written in standing for what it holds now, and everything after it in the unit
    /// moved along — or null, where the change is not inside one body or the language will not say.
    /// </summary>
    private ContentUnit? Grown(int index, int from, int to, int by, IContentCharacters edited, IBodyBounds bounds)
    {
        var (unit, start) = _units[index];
        var (low, high) = (from - start, to - start);
        if (high > unit.Width) return null;

        var path = new List<ContentNode>();
        if (!Holding(unit.Tree, low, high, path) || path.Count < 2) return null;

        var (holder, body) = (path[^2], path[^1]);
        // The line ending closing the body's last line is the closing delimiter's line's, and no part of what the body holds — as
        // the body was read: written in where it starts, the delimiter's line is written in, whatever is written there.
        var (begins, ends) = (body.Offset!.Value, body.Offset.Value + ContentNested.OwnEnd(body));
        if (low < begins || high > ends) return null;

        // The piece written in: the body itself, or the one line of it the change falls in where a container's marks stand between.
        if (!body.IsLeaf)
        {
            // Never across the line ending closing a line: past it is the next line, the container's marks first.
            var line = body.Children.FirstOrDefault(part => part.Role != Roles.Trivia && part.Offset is { } at && at <= low
                                                            && high <= at + part.Width - Closing(part));
            if (line is null || !line.IsLeaf) return null;

            path.Add(line);
        }

        var written = edited.Slice(from, to - from + by);
        var lines = Lines(edited, start + begins, start + ends + by, from, from + written.Length);
        if (!bounds.Holds(holder, body, written, lines)) return null;

        var leaf = path[^1];
        var stood = leaf.Offset!.Value;
        var grown = leaf.Stands
            ? ContentNode.Leaf(leaf.Kind, edited, start + stood, leaf.Width + by, leaf.Role, stood)
            : ContentNode.Leaf(leaf.Kind, edited.Slice(start + stood, leaf.Width + by), leaf.Role, leaf.Trouble, stood);

        // Up the path again, each piece holding the one grown and everything after it moved along.
        for (var at = path.Count - 2; at >= 0; at--)
        {
            var parent = path[at];
            var children = new ContentNode[parent.Children.Count];
            var after = false;

            for (var child = 0; child < children.Length; child++)
            {
                var part = parent.Children[child];

                if (ReferenceEquals(part, path[at + 1])) { children[child] = grown; after = true; }
                else children[child] = after ? part.FurtherOn(by) : part;
            }

            grown = parent.With(children);
        }

        // The body written into is written into its reading, if it has one; every holder after it is moved along.
        var (lead, bodies) = (holder.Offset!.Value, new List<(int At, ContentBody Body)>(unit.Bodies.Count));

        foreach (var (held, reading) in unit.Bodies)
        {
            if (held != lead)
            {
                bodies.Add((held > lead ? held + by : held, reading));
                continue;
            }

            var opens = held + reading.Lead;
            var (ownFrom, ownTo) = (reading.Space.OwnAt(low - opens), reading.Space.OwnAt(high - opens));
            var space = reading.Space.Grown(edited, start + opens, ownFrom, by);

            bodies.Add((held, reading.Edited(ownFrom, ownTo, by, space, reading.Lead, reading.Width + by)));
        }

        return new ContentUnit(unit.Width + by, unit.Begun, unit.Ended, unit.Last, grown) { Bodies = bodies };
    }

    /// <summary>How long the line ending closing <paramref name="line"/> is: two, one, or none.</summary>
    private static int Closing(ContentNode line)
    {
        var width = line.Width;

        return width >= 2 && line.CharacterAt(width - 1) == '\n' && line.CharacterAt(width - 2) == '\r' ? 2
             : width >= 1 && line.CharacterAt(width - 1) == '\n' ? 1
             : 0;
    }

    /// <summary>
    /// The way down <paramref name="node"/> to the body another reading reads (<see cref="Kinds.Nested"/>) holding the whole of
    /// <paramref name="low"/> to <paramref name="high"/>, collected into <paramref name="path"/> — false where none holds it.
    /// </summary>
    private static bool Holding(ContentNode node, int low, int high, List<ContentNode> path)
    {
        if (node.IsDerived || node.Offset is not { } at || low < at || high > at + node.Width) return false;

        path.Add(node);
        if (node.Kind == Kinds.Nested) return true;

        foreach (var child in node.Children)
            if (Holding(child, low, high, path)) return true;

        path.RemoveAt(path.Count - 1);
        return false;
    }

    /// <summary>Every line from <paramref name="low"/> to <paramref name="high"/>, whole — no further back than <paramref name="first"/> nor on past <paramref name="last"/>.</summary>
    private static string Lines(IContentCharacters characters, int first, int last, int low, int high)
    {
        var start = low;
        while (start > first && characters[start - 1] != '\n') start--;

        var end = Math.Max(high, start);
        while (end < last && characters[end] != '\n') end++;

        return characters.Slice(start, end - start);
    }

    /// <summary>Whether <paramref name="at"/> is on the first line of what starts at <paramref name="start"/>: no line break stands between them.</summary>
    private static bool Opening(IContentCharacters characters, int start, int at)
    {
        foreach (var chunk in characters.Chunks(start, Math.Min(at, characters.Length)))
            if (chunk.Span.IndexOfAny('\n', '\r') >= 0) return false;

        return true;
    }
}

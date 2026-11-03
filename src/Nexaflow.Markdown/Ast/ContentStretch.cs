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

    /// <summary>A copy, with nothing placed yet: a copy may hold another tree.</summary>
    private ContentUnit(ContentUnit original)
    {
        this.Width = original.Width;
        this.Begun = original.Begun;
        this.Ended = original.Ended;
        this.Last = original.Last;
        this.Tree = original.Tree;
        this.Bodies = original.Bodies;
    }

    /// <summary>
    /// The readings of the bodies it holds written in other languages, each at the place its holder starts in the unit, in order —
    /// those read so far: a body is read when something asks for it (<see cref="ContentBody"/>).
    /// </summary>
    public IReadOnlyList<(int At, ContentBody Body)> Bodies { get; init; } = [];

    /// <summary>The reading of the body held by the holder starting at <paramref name="at"/> in the unit, or null where none is read.</summary>
    public ContentBody? Body(int at)
    {
        foreach (var (held, body) in this.Bodies)
            if (held == at) return body;

        return null;
    }

    /// <summary>The same unit holding <paramref name="body"/> as the reading of the body held by the holder starting at <paramref name="at"/>.</summary>
    public ContentUnit Holding(int at, ContentBody body)
    {
        var bodies = new List<(int At, ContentBody Body)>(this.Bodies.Count + 1);
        var placed = false;

        foreach (var (held, kept) in this.Bodies)
        {
            if (!placed && held >= at)
            {
                bodies.Add((at, body));
                placed = true;
                if (held == at) continue;
            }

            bodies.Add((held, kept));
        }

        if (!placed) bodies.Add((at, body));

        return this with { Bodies = bodies };
    }

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

            // Read as one unit, a reading is held as it was read; read a unit at a time, what a body holds stands for its characters.
            var tree = softMaxLength == int.MaxValue ? read.Tree : Standing(read.Tree, characters, from);

            return new ContentUnit(read.Stopped ?? length, begun, read.Resume, read.Stopped is null, tree);
        }
    }

    /// <summary>
    /// <paramref name="node"/> with every body standing for its characters in <paramref name="characters"/> rather than holding a copy of
    /// them: each leaf the parser made in the body role (<see cref="Roles.Body"/>) — a body another reading reads (<see cref="Kinds.Nested"/>),
    /// code read as written, a block's characters held as they were written. The unit was read from <paramref name="from"/>, and its offsets
    /// are its own. A language's own node, and a leaf with something to say, carry more than their characters and are kept as they were
    /// made. The characters are the version the unit was read from, which a unit kept through later changes still reads exactly as.
    /// </summary>
    private static ContentNode Standing(ContentNode node, IContentCharacters characters, int from)
    {
        if (node.IsLeaf)
            return node.Role == Roles.Body && !node.IsDerived && !node.Stands && node.Offset is { } at
                   && node.Trouble is null && node.Held is null && node.GetType() == typeof(ContentNode)
                ? ContentNode.Leaf(node.Kind, characters, from + at, node.Width, node.Role, at)
                : node;

        ContentNode[]? made = null;

        for (var index = 0; index < node.Children.Count; index++)
        {
            var child = node.Children[index];
            var now = Standing(child, characters, from);

            if (made is null && !ReferenceEquals(now, child))
            {
                made = new ContentNode[node.Children.Count];
                for (var before = 0; before < index; before++) made[before] = node.Children[before];
            }

            if (made is not null) made[index] = now;
        }

        return made is null ? node : node.With(made);
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
    public ContentUnits(string language) : this(language, ContentIndex<ContentUnit>.Empty, null) { }

    private ContentUnits(string language, ContentIndex<ContentUnit> units, IReadingSyntax? syntax)
    {
        this.Language = language;
        _units = units;
        this.Syntax = syntax;
    }

    /// <summary>
    /// What the reading's language keeps of all of it beside its units — code's syntax tree — carried through every change, or null for a
    /// language that keeps nothing.
    /// </summary>
    public IReadingSyntax? Syntax { get; }

    /// <summary>The same reading keeping <paramref name="syntax"/> of all of it beside its units.</summary>
    public ContentUnits WithSyntax(IReadingSyntax? syntax) => new(this.Language, _units, syntax);

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

        return new(this.Language, _units.Replaced(index, index + 1, [unit.Holding(at - start, body)]), this.Syntax);
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

        return new(this.Language, _units.Added(unit), this.Syntax);
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
            return (new(this.Language, _units.Replaced(holding, holding + 1, [grown]), syntax), holding, 0);

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

            // A reading read as one unit reads to its end, wherever the change was.
            var length = next < this.Count && softMaxLength != int.MaxValue ? _units[next].Start + by - from : softMaxLength;
            var read = ContentUnit.Read(parse, edited, from, length, begun);
            reread.Add(read);

            if (read.Last) return this.Reread(kept, this.Count, reread, changedFrom, changedTo, by, edited, syntax);

            var stop = from + read.Width;

            if (next < this.Count && _units[next] is var (old, start) && start + by == stop && Equals(old.Begun, read.Ended))
                return this.Reread(kept, next, reread, changedFrom, changedTo, by, edited, syntax);

            if ((!this.Done && stop >= this.Reached + by) || stop >= enough)
                return this.Reread(kept, this.Count, reread, changedFrom, changedTo, by, edited, syntax);

            (from, begun) = (stop, read.Ended);
        }
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

        return (new(this.Language, _units.Replaced(kept, next, reread), syntax), kept, reread.Count);
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

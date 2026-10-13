namespace Nexaflow.Markdown.Ast;

/// <summary>
/// A stretch of a large document a windowed reading is asked for: read from <paramref name="From"/>, and once
/// <paramref name="SoftMaxLength"/> characters are behind, stop at the next place where nothing is left open — the end of
/// whatever the reading is inside — saying where that was (<see cref="ContentParse.Stopped"/>) and what it needs to carry on
/// from there (<see cref="ContentParse.Resume"/>).
///
/// <para>
/// Where to stop is the parser's to say, because only it knows what is open: a markdown list, a fence, an RTF group. A
/// container longer than <paramref name="SoftMaxLength"/> is read whole, so a stretch never ends part-way through one.
/// </para>
/// </summary>
/// <param name="Source">The whole document. Every offset is its own.</param>
/// <param name="From">Where to start: nought, or where the stretch before this one stopped.</param>
/// <param name="SoftMaxLength">How far to read before looking for somewhere to stop.</param>
/// <param name="Resume">What the stretch before this one stopped in (<see cref="ContentParse.Resume"/>), or null for the first.</param>
public sealed record ContentStretch(string Source, int From, int SoftMaxLength, object? Resume = null);

/// <summary>One stretch of a document, as it was read.</summary>
/// <param name="From">Where it starts.</param>
/// <param name="Stopped">Where it stopped — where the next one starts — or null where it read to the end.</param>
/// <param name="Begun">What it was read from: the stretch before's <see cref="Ended"/>.</param>
/// <param name="Ended">What it stopped in (<see cref="ContentParse.Resume"/>).</param>
/// <param name="Tree">What it read to: a block in the document's language, standing for its characters and no others, as they stood when it was read.</param>
public sealed record ContentChunk(int From, int? Stopped, object? Begun, object? Ended, ContentNode Tree)
{
    private ContentNode? _placed;

    /// <summary>How far what was written or taken away before it has moved it since it was read — what every offset in its tree moves by.</summary>
    public int Moved { get; private init; }

    /// <summary>
    /// Its tree, standing where it stands now: <see cref="Tree"/> as it was read, moved along by whatever has been written before
    /// it since — worked out once, when something first asks.
    /// </summary>
    public ContentNode Placed => Moved == 0 ? Tree : _placed ??= Tree.FurtherOn(Moved);

    /// <summary>Where it ends in a document <paramref name="length"/> characters long.</summary>
    public int End(int length) => Stopped ?? length;

    /// <summary>
    /// The same stretch, <paramref name="by"/> characters further on: what was written before it changed, and it reads exactly as
    /// it did. Nothing in it is read again, or moved until it is asked for (<see cref="Placed"/>).
    /// </summary>
    public ContentChunk Shifted(int by) =>
        by == 0 ? this : new ContentChunk(From + by, Stopped + by, Begun, Ended, Tree) { Moved = Moved + by };

    /// <summary>The stretch of <paramref name="source"/> from <paramref name="from"/> read by <paramref name="parse"/>.</summary>
    public static ContentChunk Read(Func<ContentStretch, ContentParse> parse, string source, int from, int softMaxLength, object? begun)
    {
        var read = parse(new ContentStretch(source, from, softMaxLength, begun));

        return new ContentChunk(from, read.Stopped, begun, read.Resume, read.Tree);
    }
}

/// <summary>
/// A large document read a stretch at a time (<see cref="ContentStretch"/>): the chunks read so far, in order, and the one
/// tree they come to.
///
/// <para>
/// <strong>The tree prints as the whole document</strong>, read or not: every chunk's own parts in order, then what is still
/// unread as one leaf of trivia (<see cref="Kinds.Unreached"/>), which nothing draws. What a chunk hangs under its block as
/// derived parts is said by the last chunk taken, which has read the most.
/// </para>
/// <para>
/// Only data: what reads a chunk, and when, is the engine's (<see cref="ContentChunk.Read"/> runs on any thread, and a chunk
/// read is handed here whole).
/// </para>
/// </summary>
/// <param name="source">The whole document.</param>
/// <param name="language">The language it is written in, which its tree says.</param>
public sealed class ContentChunks(string source, string language)
{
    private readonly List<ContentChunk> _chunks = [];

    /// <summary>The whole document.</summary>
    public string Source => source;

    /// <summary>The language it is written in, which its tree says.</summary>
    public string Language => language;

    /// <summary>The chunks read so far, in the order they stand.</summary>
    public IReadOnlyList<ContentChunk> Chunks => _chunks;

    /// <summary>Whether the last chunk read reached the end of the document.</summary>
    public bool Done => _chunks.Count > 0 && _chunks[^1].Stopped is null;

    /// <summary>Where what has been read ends: where the next chunk starts.</summary>
    public int Reached => _chunks.Count == 0 ? 0 : _chunks[^1].End(source.Length);

    /// <summary>The stretch to read next, asking a parser for about <paramref name="softMaxLength"/> characters — or null once everything is read.</summary>
    public ContentStretch? Next(int softMaxLength) =>
        Done ? null : new ContentStretch(source, Reached, softMaxLength, _chunks.Count == 0 ? null : _chunks[^1].Ended);

    /// <summary>Puts <paramref name="chunk"/> after the last one read; it must start where that one stopped.</summary>
    public void Add(ContentChunk chunk)
    {
        if (Done || chunk.From != Reached)
            throw new InvalidOperationException($"A chunk from {chunk.From} does not follow what is read, which ends at {Reached}.");

        _chunks.Add(chunk);
    }

    /// <summary>
    /// The document as one tree: the own parts of the first <paramref name="count"/> chunks — all of them, where it says none —
    /// what is not among them held as written, and what the last of them said about the whole.
    /// </summary>
    public ContentNode Tree(int? count = null)
    {
        var taken = Math.Min(count ?? _chunks.Count, _chunks.Count);
        var parts = new List<ContentNode>();

        for (var at = 0; at < taken; at++)
            foreach (var part in _chunks[at].Placed.Children)
                if (!part.IsDerived) parts.Add(part);

        var reached = taken == 0 ? 0 : _chunks[taken - 1].End(source.Length);
        if (reached < source.Length)
            parts.Add(ContentNode.Leaf(Kinds.Unreached, source[reached..], Roles.Trivia, offset: reached));

        if (taken > 0)
            foreach (var part in _chunks[taken - 1].Placed.Children)
                if (part.IsDerived) parts.Add(part);

        return new BlockNode(language, parts, offset: source.Length > 0 ? 0 : null);
    }

    /// <summary>
    /// The document as it reads once <paramref name="edited"/> has replaced it, read again only where it has to be.
    ///
    /// <para>
    /// The chunks before the change are kept as they are. The change is read from the chunk before the one it falls in, because
    /// the first line of a block can change what the block before it is — a <c>===</c> under a paragraph makes it a heading — and
    /// nothing further back. It is read on only until a stretch stops where one of the old chunks after the change started,
    /// moved along by the change, and stops in exactly the state that chunk started in: from there every old chunk reads as it
    /// did, and is kept, moved along (<see cref="ContentChunk.Shifted"/>). A change that leaves a fence open, or an RTF group,
    /// simply reads on further, as far as it reaches. What was not read before is still not read.
    /// </para>
    /// </summary>
    /// <param name="enough">
    /// How far reading again has to go before it may stop without having come back into step: past there, what was read before is
    /// let go and left unread, for whatever reads the rest to read again — so a change that moves everything after it, a heading
    /// renamed, costs what is needed and not the whole document.
    /// </param>
    /// <returns>The document, and how many of its chunks are the old ones before the change, read as they were.</returns>
    public (ContentChunks Chunks, int Kept) Edited(string edited, Func<ContentStretch, ContentParse> parse, int softMaxLength,
                                                   int enough = int.MaxValue)
    {
        var into = new ContentChunks(edited, language);
        if (_chunks.Count == 0) return (into, 0);

        // Where the change starts, and where it ends in what was written before it: the stretch the two sources do not share.
        var shortest = Math.Min(source.Length, edited.Length);
        var same = 0;
        while (same < shortest && source[same] == edited[same]) same++;

        var tail = 0;
        while (tail < shortest - same && source[^(tail + 1)] == edited[^(tail + 1)]) tail++;

        var (changed, by) = (source.Length - tail, edited.Length - source.Length);

        var holding = _chunks.Count - 1;
        while (holding > 0 && _chunks[holding].From > same) holding--;

        var kept = Math.Max(holding - 1, 0);
        for (var at = 0; at < kept; at++) into._chunks.Add(_chunks[at]);

        var (from, begun, next) = (_chunks[kept].From, _chunks[kept].Begun, kept);

        while (true)
        {
            // Asked to stop where the next old chunk past the change starts, moved along — a block starts there still, unless the
            // change reached it — so a stretch stops in step with the old ones rather than a few characters off them for ever.
            while (next < _chunks.Count && (_chunks[next].From < changed || _chunks[next].From + by <= from)) next++;

            var length = next < _chunks.Count ? _chunks[next].From + by - from : softMaxLength;
            var read = ContentChunk.Read(parse, edited, from, length, begun);
            into._chunks.Add(read);

            if (read.Stopped is not { } stop) return (into, kept);

            if (next < _chunks.Count && _chunks[next].From + by == stop && Equals(_chunks[next].Begun, read.Ended))
            {
                for (var at = next; at < _chunks.Count; at++) into._chunks.Add(_chunks[at].Shifted(by));
                return (into, kept);
            }

            if ((!Done && stop >= Reached + by) || stop >= enough) return (into, kept);

            (from, begun) = (stop, read.Ended);
        }
    }

    /// <summary>The same chunks, in a list of their own — for reading on from them away from whatever holds this one.</summary>
    public ContentChunks Copy()
    {
        var copy = new ContentChunks(source, language);
        copy._chunks.AddRange(_chunks);

        return copy;
    }
}

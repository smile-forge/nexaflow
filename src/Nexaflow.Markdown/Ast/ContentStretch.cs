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
/// <param name="Whole">
/// What reading the whole document came to — the last stretch's <see cref="ContentParse.Resume"/> — where that is known, for a
/// language whose stretches read what the whole document says, as markdown's words read its link definitions. Null until it is.
/// </param>
public sealed record ContentStretch(string Source, int From, int SoftMaxLength, object? Resume = null, object? Whole = null);

/// <summary>One stretch of a document, as it was read.</summary>
/// <param name="From">Where it starts.</param>
/// <param name="Stopped">Where it stopped — where the next one starts — or null where it read to the end.</param>
/// <param name="Begun">What it was read from: the stretch before's <see cref="Ended"/>.</param>
/// <param name="Ended">What it stopped in (<see cref="ContentParse.Resume"/>).</param>
/// <param name="Whole">What the whole document came to when it was read (<see cref="ContentStretch.Whole"/>), or null where that was not known.</param>
/// <param name="Tree">What it read to: a block in the document's language, standing for its characters and no others.</param>
public sealed record ContentChunk(int From, int? Stopped, object? Begun, object? Ended, object? Whole, ContentNode Tree)
{
    /// <summary>Where it ends in a document <paramref name="length"/> characters long.</summary>
    public int End(int length) => Stopped ?? length;

    /// <summary>The stretch of <paramref name="source"/> from <paramref name="from"/> read by <paramref name="parse"/>.</summary>
    public static ContentChunk Read(Func<ContentStretch, ContentParse> parse, string source, int from, int softMaxLength,
                                    object? begun, object? whole)
    {
        var read = parse(new ContentStretch(source, from, softMaxLength, begun, whole));

        return new ContentChunk(from, read.Stopped, begun, read.Resume, whole, read.Tree);
    }
}

/// <summary>
/// A large document read a stretch at a time (<see cref="ContentStretch"/>): the chunks read so far, in order, and the one
/// tree they come to.
///
/// <para>
/// <strong>The tree prints as the whole document</strong>, read or not: every chunk's own parts in order, then what is still
/// unread as one leaf of trivia (<see cref="Kinds.Unreached"/>), which nothing draws. What a chunk works out about the
/// whole document — hung under its block as derived parts — is said by the last chunk read, which knows the most.
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

    /// <summary>
    /// What reading the whole document came to — what its last chunk ended in — once it has all been read; null before, and
    /// where the language carries nothing past its end.
    /// </summary>
    public object? Whole => Done ? _chunks[^1].Ended : null;

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
    /// The chunks that were read without knowing what the whole document came to, or knowing something else — each to be read
    /// again with <see cref="Whole"/> (<see cref="Settle"/>). None before everything is read.
    /// </summary>
    public IEnumerable<int> Unsettled()
    {
        if (Whole is not { } whole) yield break;

        for (var at = 0; at < _chunks.Count; at++)
            if (!Equals(_chunks[at].Whole, whole)) yield return at;
    }

    /// <summary>
    /// Puts <paramref name="chunk"/> — the one at <paramref name="at"/>, read again knowing what the whole document came to — in
    /// that one's place. Knowing more about the document changes what a stretch reads to, never where it stops or what it
    /// stops in.
    /// </summary>
    public void Settle(int at, ContentChunk chunk)
    {
        var was = _chunks[at];

        if (chunk.From != was.From || chunk.Stopped != was.Stopped || !Equals(chunk.Ended, was.Ended))
            throw new InvalidOperationException($"The chunk from {was.From} read again stopped somewhere else.");

        _chunks[at] = chunk;
    }

    /// <summary>The document as one tree: every chunk's own parts, what is still unread, and what the last chunk read said about the whole.</summary>
    public ContentNode Tree()
    {
        var parts = new List<ContentNode>();

        foreach (var chunk in _chunks)
            foreach (var part in chunk.Tree.Children)
                if (!part.IsDerived) parts.Add(part);

        if (Reached < source.Length)
            parts.Add(ContentNode.Leaf(Kinds.Unreached, source[Reached..], Roles.Trivia, offset: Reached));

        if (_chunks.Count > 0)
            foreach (var part in _chunks[^1].Tree.Children)
                if (part.IsDerived) parts.Add(part);

        return new BlockNode(language, parts, offset: source.Length > 0 ? 0 : null);
    }
}

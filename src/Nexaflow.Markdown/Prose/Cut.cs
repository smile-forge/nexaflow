using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The source a reading is cut from, where it stands, and how much of it has been accounted for.
///
/// <para>
/// Every piece of every markdown tree comes through here, so one place can answer the question the whole
/// design rests on: was this character copied, or made up? Every reader hands over places — the scan's blocks, the
/// matching's words — and the characters are cut out of the source at them, never taken from the reading.
/// </para>
/// <para>
/// What falls between two places is not lost either. A bullet, a quote's <c>&gt;</c>, the pipes round a cell:
/// none of it is stripped and none of it is invented, because it is kept as the trivia it is, exactly where
/// the writer put it. That is the whole of why a reading prints back as what it was read from.
/// </para>
/// <para>
/// And every piece says where it was cut from, counted in the document rather than in the stretch handed over:
/// a block's words are read from the block's own source, which stands <c>stands</c> characters into the document
/// that holds it.
/// </para>
/// <para>
/// <strong>What a reading owns need not be one stretch.</strong> A grid table's cell is its slice of each line it covers,
/// with the other cells and the table's pipes between: a reading of a cell owns its slices and nothing between them, so
/// what it passes over between two of them is somebody else's and is no trivia of its own.
/// </para>
/// </summary>
internal sealed class Cut
{
    private readonly string _source;
    private readonly int _stands;
    private readonly int _from;
    private readonly int _to;

    /// <summary>The stretches of the source this reading owns, where it does not own all of it from its start to its end.</summary>
    private readonly IReadOnlyList<(int From, int To)>? _owned;

    /// <param name="source">The block's own source, as the document holds it.</param>
    /// <param name="stands">Where <paramref name="source"/>'s first character stands in the document holding it — nought for a document.</param>
    public Cut(string source, int stands = 0)
        : this(source, stands, 0, source.Length, null)
    {
    }

    /// <summary>A stretch of a document, from <paramref name="from"/> to <paramref name="to"/>, every place counted in the whole of it.</summary>
    public Cut(string source, int from, int to)
        : this(source, 0, from, to, null)
    {
    }

    /// <summary>
    /// What is written in <paramref name="owned"/> stretches of <paramref name="source"/> and in nothing between them — a grid
    /// table's cell.
    /// </summary>
    /// <param name="stands">Where <paramref name="source"/>'s first character stands in the document holding it.</param>
    public Cut(string source, int stands, IReadOnlyList<(int From, int To)> owned)
        : this(source, stands, owned.Count > 0 ? owned[0].From : 0, owned.Count > 0 ? owned[^1].To : 0, owned)
    {
    }

    private Cut(string source, int stands, int from, int to, IReadOnlyList<(int From, int To)>? owned)
    {
        _source = source;
        _stands = stands;
        _from = from;
        _to = to;
        _owned = owned;
        this.At = from;
    }

    /// <summary>The characters cut from — the stretch handed over, or the whole of what holds the stretches owned.</summary>
    public string Source => _source;

    /// <summary>Where <see cref="Source"/>'s first character stands in the document.</summary>
    public int Stands => _stands;

    /// <summary>
    /// Whether this reading owns only stretches of what it reads over — in which case nothing in it can be held as one stretch
    /// of source for a reader later, and each block in it is read where it stands.
    /// </summary>
    public bool Sliced => _owned is not null;

    /// <summary>How much of the source has been accounted for.</summary>
    public int At { get; private set; }

    /// <summary>Where the stretch being read ends.</summary>
    public int Length => _to;

    /// <summary>Where this source begins in the document holding it.</summary>
    public int From => _stands + _from;

    /// <summary>Where the next character to be accounted for stands in that document.</summary>
    public int Here => _stands + this.At;

    /// <summary>The stretches owned, or null where everything from the start to the end is.</summary>
    public IReadOnlyList<(int From, int To)>? Owned => _owned;

    /// <summary>
    /// Where a block reaching to <paramref name="end"/> closes: past the end of the line it stopped on — which is also what
    /// puts every block on a line of its own, as it was written. A line of a stretch owned ends where the stretch does.
    /// </summary>
    public int Closing(int end, int from)
    {
        var to = Math.Clamp(end, from, _to);

        while (to < _to && this.Owns(to) && _source[to] != '\n') to++;

        return to < _to && this.Owns(to) && _source[to] == '\n' ? to + 1 : to;
    }

    public string Between(int from, int to) => _source[from..Math.Clamp(to, from, _to)];

    /// <summary>Whatever has been passed over since the last piece, kept where it was written — what of it is this reading's.</summary>
    public void Gap(List<ContentNode> parts, int stop)
    {
        if (stop <= this.At) return;

        if (_owned is null)
        {
            parts.Add(this.Take(stop, Roles.Trivia));
            return;
        }

        foreach (var (from, to) in this.Stretches(this.At, stop))
        {
            var text = _source[from..to];
            parts.Add(ContentNode.Leaf(text.AsSpan().IsWhiteSpace() ? Kinds.Space : Kinds.Token, text, Roles.Trivia, offset: _stands + from));
        }

        this.At = Math.Min(stop, _to);
    }

    /// <summary>Everything up to <paramref name="stop"/> accounted for by pieces cut out somewhere else — the words of a block, read over its lines.</summary>
    public void Accounted(int stop)
    {
        if (stop > this.At) this.At = Math.Min(stop, _to);
    }

    /// <summary>The stretches of what this reading owns between <paramref name="from"/> and <paramref name="to"/>, in order.</summary>
    public IEnumerable<(int From, int To)> Stretches(int from, int to)
    {
        if (_owned is null)
        {
            if (to > from) yield return (from, to);
            yield break;
        }

        foreach (var (start, end) in _owned)
        {
            var (cut, stop) = (Math.Max(start, from), Math.Min(end, to));
            if (stop > cut) yield return (cut, stop);
        }
    }

    /// <summary>Whether the character at <paramref name="at"/> is this reading's.</summary>
    private bool Owns(int at)
    {
        if (_owned is null) return true;

        foreach (var (from, to) in _owned)
            if (at >= from && at < to) return true;

        return false;
    }

    /// <summary>
    /// The source up to <paramref name="stop"/>, as a piece saying where it was cut from. Space is space and
    /// everything else is a mark, unless the caller knows better.
    /// </summary>
    public ContentNode Take(int stop, string role, string? kind = null)
    {
        var at = this.Here;
        var text = this.Text(stop);

        return ContentNode.Leaf(kind ?? (text.AsSpan().IsWhiteSpace() ? Kinds.Space : Kinds.Token), text, role, offset: at);
    }

    /// <summary>
    /// What this reading owns up to <paramref name="stop"/>, as one piece of <paramref name="kind"/> where it is one stretch — or,
    /// where it runs over another's, as a piece of each stretch held together.
    /// </summary>
    public ContentNode Held(int stop, string kind, string role)
    {
        if (_owned is null) return this.Take(stop, role, kind);

        var pieces = new List<ContentNode>();

        foreach (var (from, to) in this.Stretches(this.At, stop))
            pieces.Add(ContentNode.Leaf(kind, _source[from..to], Roles.Element, offset: _stands + from));

        this.At = Math.Min(stop, _to);

        return pieces.Count == 1 ? pieces[0].As(role) : ContentNode.Branch(kind, pieces, role);
    }

    /// <summary>The source up to <paramref name="stop"/>, handed over for somebody else to read.</summary>
    public string Text(int stop)
    {
        var text = this.Between(this.At, stop);
        this.At += text.Length;

        return text;
    }
}

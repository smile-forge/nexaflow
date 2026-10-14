using Markdig.Syntax;

using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The source a reading is cut from, where it stands, and how much of it has been accounted for.
///
/// <para>
/// Every piece of every markdown tree comes through here, so one place can answer the question the whole
/// design rests on: was this character copied, or made up? Every reader hands over places — the scan's blocks, Markdig's
/// spans for the words — and the characters are cut out of the source at them, never taken from the reading.
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
/// </summary>
internal sealed class Cut
{
    private readonly string _source;
    private readonly int _stands;
    private readonly int _from;
    private readonly int _to;

    /// <param name="source">The block's own source, as the document holds it.</param>
    /// <param name="stands">Where <paramref name="source"/>'s first character stands in the document holding it — nought for a document.</param>
    public Cut(string source, int stands = 0)
        : this(source, stands, 0, source.Length)
    {
    }

    /// <summary>A stretch of a document, from <paramref name="from"/> to <paramref name="to"/>, every place counted in the whole of it.</summary>
    public Cut(string source, int from, int to)
        : this(source, 0, from, to)
    {
    }

    private Cut(string source, int stands, int from, int to)
    {
        _source = source;
        _stands = stands;
        _from = from;
        _to = to;
        this.At = from;
    }

    /// <summary>How much of the source has been accounted for.</summary>
    public int At { get; private set; }

    /// <summary>Where the stretch being read ends.</summary>
    public int Length => _to;

    /// <summary>Where this source begins in the document holding it.</summary>
    public int From => _stands + _from;

    /// <summary>Where the next character to be accounted for stands in that document.</summary>
    public int Here => _stands + this.At;

    /// <summary>Where something Markdig read starts, never behind what has already been taken.</summary>
    public int Starts(MarkdownObject what) => Math.Clamp(what.Span.Start, this.At, _to);

    /// <summary>One past the last character something Markdig read stands for.</summary>
    public int Ends(MarkdownObject what) => Math.Clamp(what.Span.End + 1, this.At, _to);

    /// <summary>
    /// Where a block reaching to <paramref name="end"/> closes: past the end of the line it stopped on — which is also what
    /// puts every block on a line of its own, as it was written.
    /// </summary>
    public int Closing(int end, int from)
    {
        var to = Math.Clamp(end, from, _to);

        while (to < _to && _source[to] != '\n') to++;

        return to < _to ? to + 1 : to;
    }

    public string Between(int from, int to) => _source[from..Math.Clamp(to, from, _to)];

    /// <summary>Everything not yet accounted for, shown as the characters it was written with.</summary>
    public ContentNode Unaccounted()
    {
        var at = this.Here;

        return ContentNode.Shown(this.Text(this.Length), offset: at);
    }

    /// <summary>Whatever has been passed over since the last piece, kept where it was written.</summary>
    public void Gap(List<ContentNode> parts, int stop)
    {
        if (stop > this.At) parts.Add(this.Take(stop, Roles.Trivia));
    }

    /// <summary>The source up to the next <paramref name="mark"/>, where one stands before the end.</summary>
    public ContentNode? Upto(char mark, int end)
    {
        var found = _source.IndexOf(mark, this.At);

        return found >= this.At && found < end ? this.Take(found + 1, Roles.Close, Kinds.Token) : null;
    }

    /// <summary>Where a stretch stands in the source, with whatever the writer left in front of it.</summary>
    public (ContentNode? Before, ContentNode Found)? Finds(string what, int end)
    {
        var at = _source.IndexOf(what, this.At, StringComparison.Ordinal);
        if (at < this.At || at + what.Length > end) return null;

        var before = at > this.At ? this.Take(at, Roles.Trivia, Kinds.Token) : null;

        return (before, this.Take(at + what.Length, Roles.Element, MarkdownKinds.Word));
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

    /// <summary>The source up to <paramref name="stop"/>, handed over for somebody else to read.</summary>
    public string Text(int stop)
    {
        var text = this.Between(this.At, stop);
        this.At += text.Length;

        return text;
    }
}

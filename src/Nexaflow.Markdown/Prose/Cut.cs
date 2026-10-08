using Markdig.Syntax;

using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The source a reading is cut from, where it stands, and how much of it has been accounted for.
///
/// <para>
/// Every piece of every markdown tree comes through here, so one place can answer the question the whole
/// design rests on: was this character copied, or made up? Markdig's own tree is no use for that — it holds
/// text it has already read, an entity decoded, an escape taken off, a line's indentation dropped — so
/// nothing in it is copied across. Only the spans are, and the characters are cut out of the source at them.
/// </para>
/// <para>
/// What falls between two spans is not lost either. A bullet, a quote's <c>&gt;</c>, the pipes round a cell:
/// none of it is stripped and none of it is invented, because it is kept as the trivia it is, exactly where
/// the writer put it. That is the whole of why a reading prints back as what it was read from.
/// </para>
/// <para>
/// And every piece says where it was cut from, counted in the document rather than in the stretch handed over:
/// a block's words are read from the block's own source, which stands <paramref name="stands"/> characters into
/// the document that holds it.
/// </para>
/// </summary>
/// <param name="stands">Where this source's first character stands in the document holding it — nought for a document.</param>
internal sealed class Cut(string source, int stands = 0)
{
    /// <summary>How much of the source has been accounted for.</summary>
    public int At { get; private set; }

    /// <summary>How much of it there is.</summary>
    public int Length => source.Length;

    /// <summary>Where this source begins in the document holding it.</summary>
    public int From => stands;

    /// <summary>Where the next character to be accounted for stands in that document.</summary>
    public int Here => stands + this.At;

    /// <summary>Where something starts, never behind what has already been taken.</summary>
    public int Starts(MarkdownObject what) => Math.Clamp(what.Span.Start, this.At, source.Length);

    /// <summary>One past the last character something stands for.</summary>
    public int Ends(MarkdownObject what) => Math.Clamp(what.Span.End + 1, this.At, source.Length);

    /// <summary>
    /// Where a block closes: past the end of the line it stopped on.
    ///
    /// <para>
    /// A span alone will not do. Markdig measures a block by what it read, which stops a heading's span
    /// before the newline where a paragraph's takes it in, and gives a fence nobody closed a span of four
    /// characters because there was no closing fence to measure to. Half-written input is what an editor
    /// holds all day, so a block reaches as far as its span or its own lines, whichever is further, and then
    /// to the end of the line that leaves it on — which is also what puts every block on a line of its own,
    /// as it was written.
    /// </para>
    /// </summary>
    public int Closes(Block block, int from)
    {
        var to = Math.Max(block.Span.End + 1, from);

        if (block is LeafBlock { Lines.Count: > 0 } leaf)
            to = Math.Max(to, leaf.Lines.Lines[leaf.Lines.Count - 1].Slice.End + 1);

        to = Math.Clamp(to, from, source.Length);

        while (to < source.Length && source[to] != '\n') to++;

        return to < source.Length ? to + 1 : to;
    }

    public string Between(int from, int to) => source[from..Math.Clamp(to, from, source.Length)];

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
        var found = source.IndexOf(mark, this.At);

        return found >= this.At && found < end ? this.Take(found + 1, Roles.Close, Kinds.Token) : null;
    }

    /// <summary>Where a stretch stands in the source, with whatever the writer left in front of it.</summary>
    public (ContentNode? Before, ContentNode Found)? Finds(string what, int end)
    {
        var at = source.IndexOf(what, this.At, StringComparison.Ordinal);
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

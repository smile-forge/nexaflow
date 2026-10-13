using System.Text;
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
/// a block's words are read from the block's own source, which stands <c>stands</c> characters into the document
/// that holds it.
/// </para>
/// <para>
/// <strong>A block is read as it reads where it was written.</strong> A list written inside a list item starts part-way
/// along its first line, and every line after that carries the indentation — or the <c>&gt;</c> — of the containers it
/// sits in. Handed to the reader as it stands, its second item is indented past its first and reads as a list inside it,
/// and the third inside that. So the reader is handed <see cref="Contained"/>: the block's lines with what its containers
/// put in front of each one taken off, which is what they are inside those containers. The spans it hands back are counted
/// in those lines, and every one is turned back into the source here (<see cref="Original"/>), so what was taken off is
/// still in the reading, as the trivia between the pieces either side of it.
/// </para>
/// </summary>
internal sealed class Cut
{
    /// <summary>How many columns a tab advances to the next multiple of.</summary>
    private const int TabStop = 4;

    private readonly string _source;
    private readonly int _stands;

    /// <summary>For each character of <see cref="Contained"/>, where it stands in the source; null when the two are one.</summary>
    private readonly int[]? _written;

    /// <param name="source">The block's own source, as the document holds it.</param>
    /// <param name="stands">Where <paramref name="source"/>'s first character stands in the document holding it — nought for a document.</param>
    /// <param name="lead">
    /// What stands in front of the block on the line it starts on — its containers' marks and indentation — or nothing
    /// for a block that starts its line.
    /// </param>
    public Cut(string source, int stands = 0, string? lead = null)
    {
        _source = source;
        _stands = stands;

        if (lead is { Length: > 0 } && Within(source, lead) is { } within)
            (this.Contained, _written) = within;
        else
            this.Contained = source;
    }

    /// <summary>The source as it reads inside the containers it was written in — what the reader is handed.</summary>
    public string Contained { get; }

    /// <summary>How much of the source has been accounted for.</summary>
    public int At { get; private set; }

    /// <summary>How much of it there is.</summary>
    public int Length => _source.Length;

    /// <summary>Where this source begins in the document holding it.</summary>
    public int From => _stands;

    /// <summary>Where the next character to be accounted for stands in that document.</summary>
    public int Here => _stands + this.At;

    /// <summary>Where a position the reader counted in <see cref="Contained"/> stands in the source.</summary>
    public int Original(int position)
    {
        if (_written is null || position < 0) return position;

        return position < _written.Length ? _written[position] : _source.Length + (position - _written.Length);
    }

    /// <summary>Where something starts, never behind what has already been taken.</summary>
    public int Starts(MarkdownObject what) => Math.Clamp(this.Original(what.Span.Start), this.At, _source.Length);

    /// <summary>One past the last character something stands for.</summary>
    public int Ends(MarkdownObject what) => Math.Clamp(this.Original(what.Span.End) + 1, this.At, _source.Length);

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
        var to = Math.Max(this.Original(block.Span.End) + 1, from);

        if (block is LeafBlock { Lines.Count: > 0 } leaf)
            to = Math.Max(to, this.Original(leaf.Lines.Lines[leaf.Lines.Count - 1].Slice.End) + 1);

        to = Math.Clamp(to, from, _source.Length);

        while (to < _source.Length && _source[to] != '\n') to++;

        return to < _source.Length ? to + 1 : to;
    }

    public string Between(int from, int to) => _source[from..Math.Clamp(to, from, _source.Length)];

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

    // ── A block's lines inside its containers ─────────────────────────────────

    /// <summary>
    /// <paramref name="source"/>'s lines with what its containers put in front of every line after the first taken off, and
    /// where each character left stands in the source — or null where no line carries anything to take off.
    /// </summary>
    private static (string Lines, int[] Written)? Within(string source, string lead)
    {
        var marks = Marks(lead);
        var lines = new StringBuilder(source.Length);
        var written = new List<int>(source.Length);
        var trimmed = false;

        for (var at = 0; at < source.Length;)
        {
            var start = at == 0 ? 0 : Past(source, at, marks);
            trimmed |= start > at;

            var end = source.IndexOf('\n', start);
            end = end < 0 ? source.Length : end + 1;

            for (var character = start; character < end; character++)
            {
                lines.Append(source[character]);
                written.Add(character);
            }

            at = end;
        }

        return trimmed ? (lines.ToString(), written.ToArray()) : null;
    }

    /// <summary>
    /// What a block's containers put at the head of each of its lines, read off the line it starts on: a quote's
    /// <c>&gt;</c> where one stands, which every line the quote holds carries again, and the width of everything else —
    /// indentation, and a list item's marker, which its later lines carry as that many columns of space.
    /// </summary>
    private static List<int> Marks(string lead)
    {
        // A quote's marker is a column of -1; anything else is how many columns of space stand for it.
        var marks = new List<int>();
        var column = 0;
        var width = 0;

        foreach (var character in lead)
        {
            if (character == '>')
            {
                if (width > 0) marks.Add(width);
                marks.Add(-1);

                width = 0;
                column++;
                continue;
            }

            var next = character == '\t' ? (column / TabStop + 1) * TabStop : column + 1;
            width += next - column;
            column = next;
        }

        if (width > 0) marks.Add(width);

        return marks;
    }

    /// <summary>
    /// Where the line starting at <paramref name="at"/> begins inside the block's containers: past each quote marker it
    /// carries and as much of each run of indentation as the first line's marks take up. A line that stops carrying them —
    /// a lazy continuation — keeps whatever is left.
    /// </summary>
    private static int Past(string source, int at, List<int> marks)
    {
        var column = 0;

        foreach (var mark in marks)
        {
            if (mark < 0)
            {
                // Up to three spaces may stand in front of a quote's marker.
                var probe = at;
                var spaces = 0;

                while (probe < source.Length && source[probe] == ' ' && spaces < 3)
                {
                    probe++;
                    spaces++;
                }

                if (probe >= source.Length || source[probe] != '>') return at;

                column += spaces + 1;
                at = probe + 1;
                continue;
            }

            var goal = column + mark;

            while (at < source.Length && column < goal)
            {
                var next = source[at] switch
                {
                    ' ' => column + 1,
                    '\t' => (column / TabStop + 1) * TabStop,
                    _ => -1,
                };

                // Not indentation, or a tab reaching past what the containers take: the rest is the block's own.
                if (next < 0 || next > goal) return at;

                column = next;
                at++;
            }

            if (column < goal) return at;
        }

        return at;
    }
}

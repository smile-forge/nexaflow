namespace Nexaflow.Markdown.Ast;

/// <summary>
/// The characters a body written in another language is read as — its own: the lines written in that language, without the marks a
/// container put in front of each, and without the line ending closing the last — and where each of them stands among the characters
/// holding it.
///
/// <para>
/// <strong>A view, not a copy.</strong> The characters stay where they are, in the characters of the reading holding the body
/// (<see cref="Holder"/>: the document, or another body's own space); this only says which of them are the body's. A fence written in
/// plain prose is one run of them; one written in a quote is a run per line, the quote's marks between. So a body megabytes long is read
/// without a character of it being copied, and what is read of it stands for them (<see cref="ContentNode.Stands"/>).
/// </para>
/// <para>
/// <strong>A place, either way, down one path.</strong> The runs are indexed by how many of the body's own characters each holds and how
/// many of the holder's it spreads over, its marks in front included (<see cref="ContentIndex{T}"/>), so a place among the body's own
/// characters is a place among the holder's — and back — however many lines the body runs to (<see cref="OwnAt"/>,
/// <see cref="HolderAt"/>). A place in the document is the spaces of every body on the way down, composed innermost first.
/// </para>
/// <para>
/// Places among the holder's characters are counted from where the body starts, so the space says the same wherever the body is moved
/// to; and the characters it reads are those of the version it was made over, so a body kept through a change somewhere else still reads
/// exactly what it read.
/// </para>
/// </summary>
public sealed class ContentSpace : IContentCharacters
{
    /// <summary>The runs of the body's own characters, each with the marks in front of it — or null, where the body is one run.</summary>
    private readonly ContentIndex<ContentRun>? _runs;

    private ContentSpace(IContentCharacters holder, int start, int length, ContentIndex<ContentRun>? runs)
    {
        this.Holder = holder;
        this.Start = start;
        this.Length = length;
        _runs = runs;
    }

    /// <summary>The characters holding the body, as they stood when this was made: the document's, or the body holding this one's.</summary>
    public IContentCharacters Holder { get; }

    /// <summary>Where the body starts among <see cref="Holder"/>'s characters.</summary>
    public int Start { get; }

    /// <summary>How many of the body's own characters there are.</summary>
    public int Length { get; }

    /// <summary>
    /// The space of <paramref name="body"/> — a body held as <see cref="Kinds.Nested"/>, starting at <paramref name="start"/> among
    /// <paramref name="holder"/>'s characters: every run of its own characters, the marks a container put between them and the line
    /// ending closing the last left out. Walks the body's pieces once.
    /// </summary>
    public static ContentSpace Of(IContentCharacters holder, int start, ContentNode body)
    {
        if (body.IsLeaf) return new(holder, start, body.Width - Math.Min(Closing(holder, start + body.Width), body.Width), null);

        var runs = new List<ContentRun>();
        var marks = new List<ContentNode>();
        var (own, spread) = (0, 0);

        foreach (var leaf in body.Leaves())
        {
            if (leaf.IsDerived || leaf.Width == 0) continue;

            if (leaf.Role == Roles.Trivia)
            {
                marks.Add(leaf);
            }
            else if (marks.Count == 0 && runs.Count > 0)
            {
                // Two pieces of the language's own side by side, nothing between them: one run.
                runs[^1] = runs[^1] with { Width = runs[^1].Width + leaf.Width };
                own += leaf.Width;
            }
            else
            {
                runs.Add(new ContentRun([.. marks], leaf.Width));
                own += leaf.Width;
                marks.Clear();
            }

            spread += leaf.Width;
        }

        // The line ending closing the last line is the last run's, where there is one: marks alone hold none of the body's characters.
        var ends = spread - marks.Sum(mark => mark.Width);
        var length = own - Math.Min(Closing(holder, start + ends), runs.Count == 0 ? 0 : runs[^1].Width);

        return runs is [] or [{ Marks.Count: 0 }] ? new(holder, start, length, null) : new(holder, start, length, ContentIndex<ContentRun>.Of(runs));
    }

    /// <summary>A body whose own characters are every one of <paramref name="characters"/>, from the first: one run, with no marks.</summary>
    public static ContentSpace Whole(IContentCharacters characters) => new(characters, 0, characters.Length, null);

    /// <summary>How long the line ending is that ends the characters before <paramref name="end"/>: two, one, or none.</summary>
    private static int Closing(IContentCharacters characters, int end) =>
        end >= 2 && characters[end - 1] == '\n' && characters[end - 2] == '\r' ? 2
        : end >= 1 && characters[end - 1] == '\n' ? 1
        : 0;

    /// <summary>
    /// The same body once <paramref name="by"/> of its own characters have been written into the run holding <paramref name="own"/> —
    /// none of the marks between runs touched — standing at <paramref name="start"/> among <paramref name="holder"/>'s characters as
    /// they are now.
    /// </summary>
    public ContentSpace Grown(IContentCharacters holder, int start, int own, int by)
    {
        if (_runs is null) return new(holder, start, this.Length + by, null);

        var index = _runs.IndexAt(own);
        var (run, _) = _runs[index];

        return new(holder, start, this.Length + by, _runs.Replaced(index, index + 1, [run with { Width = run.Width + by }]));
    }

    /// <summary>
    /// Where the body's own character <paramref name="own"/> stands, counted from where the body starts among the holder's characters —
    /// past the marks in front of its run, where it starts one; one past the last for <see cref="Length"/>.
    /// </summary>
    public int HolderAt(int own)
    {
        if (_runs is null) return own;

        var (run, start, spread) = _runs.Placed(_runs.IndexAt(own));
        return spread + run.Marked + (own - start);
    }

    /// <summary>
    /// Where a stretch of the body's own characters starting at <paramref name="own"/> starts, counted from where the body starts among the
    /// holder's characters — at the marks in front of its run, where it starts one, as the stretch read again with those marks put back
    /// starts there (<see cref="ContentNested.Spliced(ContentNode, ContentSpace, int, int)"/>); and otherwise where the character stands.
    /// </summary>
    public int HolderFrom(int own)
    {
        if (_runs is null) return own;

        var (run, start, spread) = _runs.Placed(_runs.IndexAt(own));
        return own == start ? spread : spread + run.Marked + (own - start);
    }

    /// <summary>
    /// How many of the body's own characters stand in front of <paramref name="at"/>, a place counted from where the body starts among
    /// the holder's characters — what that place is to the language, which was handed its own characters and none of the marks.
    /// </summary>
    public int OwnAt(int at)
    {
        if (_runs is null) return Math.Clamp(at, 0, this.Length);

        var (run, start, spread) = _runs.Placed(_runs.IndexAtSpread(Math.Max(at, 0)));
        var inside = Math.Clamp(at - spread - run.Marked, 0, run.Width);

        return Math.Min(start + inside, this.Length);
    }

    /// <summary>
    /// Which of the body's own lines <paramref name="offset"/> is on: as many as the holder's lines between where the body starts and
    /// where that character stands, since every own line is one of the holder's with the marks in front of it left out.
    /// </summary>
    public int LineOf(int offset) => this.Holder.LineOf(this.Start + this.HolderAt(Math.Clamp(offset, 0, this.Length))) - this.Holder.LineOf(this.Start);

    /// <summary>Where the body's own line <paramref name="line"/> starts: past the marks in front of the holder's line it is.</summary>
    public int OffsetOf(int line) => this.OwnAt(this.Holder.OffsetOf(this.Holder.LineOf(this.Start) + line) - this.Start);

    /// <summary>
    /// Every mark a container put in front of a run starting from <paramref name="from"/> up to <paramref name="to"/> among the body's
    /// own characters, with the own character it stands in front of — in the order they are written. None stands in front of the end:
    /// what follows the last own character is the closing delimiter's line.
    /// </summary>
    public IEnumerable<(int At, ContentNode Mark)> Marks(int from, int to)
    {
        if (_runs is null) yield break;

        foreach (var (run, start) in _runs.From(Math.Max(_runs.IndexAt(from), 0)))
        {
            if (start >= Math.Min(to, this.Length)) yield break;
            if (start < from) continue;

            foreach (var mark in run.Marks) yield return (start, mark);
        }
    }

    /// <inheritdoc/>
    public char this[int offset] => this.Holder[this.Start + this.HolderAt(offset)];

    /// <inheritdoc/>
    public string Slice(int start, int length)
    {
        Checked(start, length);

        return length == 0 ? string.Empty : string.Create(length, (Space: this, Start: start), static (into, said) => said.Space.CopyTo(said.Start, into));
    }

    /// <inheritdoc/>
    public void CopyTo(int start, Span<char> into)
    {
        Checked(start, into.Length);

        foreach (var (from, length) in this.Pieces(start, start + into.Length))
        {
            this.Holder.CopyTo(from, into[..length]);
            into = into[length..];
        }
    }

    /// <inheritdoc/>
    public IEnumerable<ReadOnlyMemory<char>> Chunks(int start = 0, int? end = null)
    {
        var to = end ?? this.Length;
        Checked(start, to - start);

        foreach (var (from, length) in this.Pieces(start, to))
            foreach (var chunk in this.Holder.Chunks(from, from + length))
                yield return chunk;
    }

    /// <summary>The own characters from <paramref name="start"/> to <paramref name="end"/>, as the stretches of the holder's they are, in order.</summary>
    private IEnumerable<(int From, int Length)> Pieces(int start, int end)
    {
        if (end <= start) yield break;

        if (_runs is null)
        {
            yield return (this.Start + start, end - start);
            yield break;
        }

        var index = _runs.IndexAt(start);
        var (_, _, spread) = _runs.Placed(index);

        foreach (var (run, at) in _runs.From(index))
        {
            if (at >= end) yield break;

            var from = Math.Max(start, at);
            var length = Math.Min(end, at + run.Width) - from;
            if (length > 0) yield return (this.Start + spread + run.Marked + (from - at), length);

            spread += run.Spread;
        }
    }

    private void Checked(int start, int length)
    {
        if (start < 0 || length < 0 || start + length > this.Length)
            throw new ArgumentOutOfRangeException(nameof(start), $"{start}+{length} is not within the {this.Length} characters of the body");
    }
}

/// <summary>
/// One run of a body's own characters, unbroken, and the marks a container put in front of it (<see cref="ContentSpace"/>): what it
/// holds of the body is <paramref name="Width"/>; what it spreads over in the holder is the marks and that.
/// </summary>
/// <param name="Marks">The marks in front of the run, each as the piece of the holder's tree it is, in order.</param>
/// <param name="Width">How many of the body's own characters the run holds.</param>
public sealed record ContentRun(IReadOnlyList<ContentNode> Marks, int Width) : IContentWidth
{
    /// <summary>How many of the holder's characters the marks in front of it take.</summary>
    public int Marked { get; } = Marks.Sum(mark => mark.Width);

    /// <inheritdoc/>
    public int Spread => this.Marked + this.Width;
}

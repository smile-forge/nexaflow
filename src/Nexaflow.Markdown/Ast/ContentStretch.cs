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
        this.Skipped = original.Skipped;
    }

    /// <summary>
    /// The readings of the bodies it holds written in other languages, each at the place its holder starts in the unit, in order —
    /// those read so far: a body is read when something asks for it (<see cref="ContentBody"/>).
    /// </summary>
    public IReadOnlyList<(int At, ContentBody Body)> Bodies { get; init; } = [];

    /// <summary>
    /// Whether it is a stretch not read — not yet, or let go of since — standing for its characters only by how many there are: what a
    /// reading that can start at any line start (<see cref="ContentUnits.Skips"/>) leaves between the stretches it read where the reader was.
    /// </summary>
    public bool Skipped { get; init; }

    /// <summary>A stretch of <paramref name="width"/> characters not read, reaching the end of the reading where <paramref name="last"/> says.</summary>
    public static ContentUnit Skipping(int width, bool last) =>
        new(width, null, null, last, ContentNode.Leaf(Kinds.Unreached, string.Empty, Roles.Trivia)) { Skipped = true };

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

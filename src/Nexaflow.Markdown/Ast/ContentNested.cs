namespace Nexaflow.Markdown.Ast;

/// <summary>
/// Content written in another language inside this one: a fenced block of a document, a formula in a sentence, a tune on a
/// flowchart node.
///
/// <para>
/// The parser reading the outer content holds it as a <see cref="BlockNode"/> in the language it names — its delimiters as
/// trivia, and a <see cref="Kinds.Nested"/> body holding the characters as written (<see cref="Roles.Body"/>). What they say is
/// the other language's to read, and it reads them only when a builder asks the engine to lay them out: what comes back is a
/// layout, put wherever the builder wants it. So no tree ever holds another language's nodes, and no builder ever sees one.
/// </para>
/// <para>
/// <strong>What a body holds is not always one stretch of the document.</strong> A fence written in a quote has the quote's
/// <c>&gt;</c> in front of every line, and one written in a list item the item's indentation: those marks are the containers',
/// held in the body as trivia between the lines that are the language's own. The language is handed its own characters
/// (<see cref="Own(ContentNode)"/>) and nothing else, and what it reads is given the marks back where they stand
/// (<see cref="Spliced"/>), so every piece of it still names the characters it was written as in the document.
/// </para>
/// </summary>
public static class ContentNested
{
    /// <summary>
    /// The word naming the language <paramref name="node"/> holds content written in, or null where it holds none — a fence naming no
    /// language holds its characters as written, in none.
    /// </summary>
    public static string? Language(ContentNode node) =>
        node is BlockNode { Language.Length: > 0 } block && node.Children.Any(child => child.Kind == Kinds.Nested) ? block.Language : null;

    /// <inheritdoc cref="Language(ContentNode)"/>
    public static string? Language(ContentPart part) => Language(part.Node);

    /// <summary>
    /// Every part holding content written in another language, from <paramref name="part"/> up the tree — the innermost first.
    /// Each is the whole of what that language is written in, its delimiters and all.
    /// </summary>
    public static IEnumerable<ContentPart> Holders(ContentPart? part)
    {
        for (var holder = part; holder is not null; holder = holder.Parent)
            if (Language(holder) is not null && holder.Part(Roles.Body) is not null) yield return holder;
    }

    /// <summary>
    /// The stretch of the document a body's own characters stand in: from where the body starts to the last of them, the line ending
    /// that closes its last line left out — that belongs to the line the closing delimiter stands on, and written into, it would carry
    /// what was typed onto the delimiter's line and the delimiter would no longer close anything. The marks a container put in front
    /// of a line are inside the stretch where the body runs over several lines (<see cref="OwnAt"/>).
    /// </summary>
    public static (int Start, int Length) Own(ContentPart body) => (body.Start, DocumentAt(body, Own(body.Node).Length) - body.Start);

    /// <summary>
    /// The characters of a body that are the other language's own — what its parser is handed: every line written in it, without the
    /// marks a container put in front of each, and without the line ending closing the last (<see cref="Own(ContentPart)"/>).
    /// </summary>
    public static string Own(ContentNode body)
    {
        var written = Written(body);

        return written.EndsWith("\r\n", StringComparison.Ordinal) ? written[..^2]
             : written.EndsWith('\n') ? written[..^1]
             : written;
    }

    /// <summary>What a body holds in its language, its closing line ending and all: the whole of it where it is one run of characters.</summary>
    private static string Written(ContentNode body)
    {
        if (body.IsLeaf) return body.Text;

        var said = new System.Text.StringBuilder(body.Width);

        foreach (var leaf in body.Leaves())
            if (leaf.Role != Roles.Trivia) said.Append(leaf.Text);

        return said.ToString();
    }

    /// <summary>Where the language's own character <paramref name="own"/> places along stands in the document — one past the last for all of them.</summary>
    public static int DocumentAt(ContentPart body, int own)
    {
        if (body.Children.Count == 0) return body.Start + own;

        var left = own;
        var end = body.Start;

        foreach (var leaf in body.SelfAndDescendants())
        {
            if (leaf.Children.Count > 0 || leaf.Derived || leaf.Role == Roles.Trivia) continue;
            if (left < leaf.Length) return leaf.Start + left;

            left -= leaf.Length;
            end = leaf.End;
        }

        return end;
    }

    /// <summary>
    /// How many of the language's own characters stand in front of <paramref name="at"/> in the document — what a place in the document
    /// is to the language, which was handed its own characters and none of the marks between them.
    /// </summary>
    public static int OwnAt(ContentPart body, int at)
    {
        if (body.Children.Count == 0) return Math.Clamp(at - body.Start, 0, body.Length);

        var own = 0;

        foreach (var leaf in body.SelfAndDescendants())
        {
            if (leaf.Children.Count > 0 || leaf.Derived || leaf.Role == Roles.Trivia) continue;
            if (at <= leaf.Start) return own;
            if (at < leaf.End) return own + (at - leaf.Start);

            own += leaf.Length;
        }

        return own;
    }

    /// <summary>
    /// What a language read of a body's own characters (<paramref name="read"/>), given back the marks the body holds between its
    /// lines: each put in where it stands among the characters read — a piece standing over the place cut in two round it — and
    /// every piece saying where it now stands. So the reading prints as the stretch of the document the body's own characters stand
    /// in (<see cref="Own(ContentPart)"/>), and positioned from where the body starts, every part names the characters it was written as.
    /// A body that is one stretch of the document is handed back as it was read.
    /// </summary>
    public static ContentNode Spliced(ContentNode read, ContentNode body)
    {
        if (body.IsLeaf) return read;

        var cuts = new List<(int At, ContentNode Mark)>();
        var own = 0;

        foreach (var leaf in body.Leaves())
        {
            if (leaf.Role == Roles.Trivia) cuts.Add((own, leaf));
            else own += leaf.Text.Length;
        }

        // Only the marks inside the characters handed over: what stands after the last of them is the closing delimiter's line.
        var length = Own(body).Length;
        cuts.RemoveAll(cut => cut.At >= length);
        if (cuts.Count == 0) return read;

        var splice = new Splice(cuts, read.Offset ?? 0);
        var at = 0;
        var made = splice.Pieces(read, ref at);
        var rest = splice.Rest();

        if (made is [{ IsLeaf: false } whole]) return rest.Count == 0 ? whole : whole.With([.. whole.Children, .. rest], whole.Offset);

        return ContentNode.Branch(read.Kind, [.. made, .. rest], read.Role, read.Offset);
    }

    /// <summary>Marks put back into a reading of a body's own characters, in the order they stand.</summary>
    private sealed class Splice(List<(int At, ContentNode Mark)> cuts, int origin)
    {
        private readonly bool[] _placed = new bool[cuts.Count];

        /// <summary>What <paramref name="node"/> comes to with the marks standing inside it put in — <paramref name="at"/> being where it starts among the characters read, moved on past it.</summary>
        public List<ContentNode> Pieces(ContentNode node, ref int at)
        {
            if (node.IsDerived) return [node];

            var start = node.Offset is { } offset ? offset - origin : at;

            if (node.IsLeaf)
            {
                at = start + node.Width;
                return this.Leaf(node, start);
            }

            var children = new List<ContentNode>(node.Children.Count);
            var inside = start;

            foreach (var child in node.Children) children.AddRange(this.Pieces(child, ref inside));

            at = Math.Max(start + node.Width, inside);
            return [node.With(children, node.Offset is null ? null : this.Before(start))];
        }

    /// <summary>
        /// A piece standing for characters, cut wherever a mark stands inside it with the mark between the halves — and one standing
        /// where it starts put in front of it.
        /// </summary>
        private List<ContentNode> Leaf(ContentNode leaf, int start)
        {
            var end = start + leaf.Width;
            List<ContentNode>? made = null;
            var cut = start;

            for (var index = 0; index < cuts.Count && leaf.Width > 0; index++)
            {
                var (place, mark) = cuts[index];
                if (_placed[index] || place < start || place >= end) continue;

                made ??= [];
                if (place > cut) made.Add(leaf.With(leaf.Text[(cut - start)..(place - start)], this.After(cut)).As(Roles.Element));

                made.Add(mark.With(mark.Text, origin + place + this.Shift(place, inclusive: false)));
                _placed[index] = true;
                cut = place;
            }

            if (made is null) return [leaf.Offset is null ? leaf : leaf.With(leaf.Text, this.After(start))];

            made.Add(leaf.With(leaf.Text[(cut - start)..], this.After(cut)).As(Roles.Element));

            // Still one piece to whatever holds it, of the kind it was and what it was to it — a body cut round a mark is still the
            // body — with the halves and the mark inside, as a run written over a container's lines is.
            return [ContentNode.Branch(leaf.Kind, made, leaf.Role, this.Before(start))];
        }

        /// <summary>Whatever marks no piece was standing over, to go at the end — where the characters read stop short of them.</summary>
        public List<ContentNode> Rest()
        {
            var rest = new List<ContentNode>();

            for (var index = 0; index < cuts.Count; index++)
                if (!_placed[index])
                {
                    var (place, mark) = cuts[index];
                    rest.Add(mark.With(mark.Text, origin + place + this.Shift(place, inclusive: false)));
                    _placed[index] = true;
                }

            return rest;
        }

        /// <summary>Where a piece starting at <paramref name="at"/> now stands, past every mark put in at or before it.</summary>
        private int After(int at) => origin + at + this.Shift(at, inclusive: true);

        /// <summary>Where a piece starting at <paramref name="at"/> now stands, the marks it begins with included.</summary>
        private int Before(int at) => origin + at + this.Shift(at, inclusive: false);

        /// <summary>How many characters of marks stand in front of <paramref name="at"/> — those put in exactly there too, where asked.</summary>
        private int Shift(int at, bool inclusive)
        {
            var by = 0;

            for (var index = 0; index < cuts.Count; index++)
            {
                var place = cuts[index].At;
                if (place < at || (inclusive && place == at && _placed[index])) by += cuts[index].Mark.Text.Length;
            }

            return by;
        }
    }

    /// <summary>Whether the stretch from <paramref name="start"/> to <paramref name="end"/> lies inside a body's own characters.</summary>
    public static bool Holds(ContentPart body, int start, int end)
    {
        var (from, length) = Own(body);
        return from <= start && end <= from + length;
    }

    /// <summary>
    /// <paramref name="root"/> and every part under it — none of what is written in another language inside it, which is held as
    /// its characters and answers for itself once it is laid out.
    /// </summary>
    public static IEnumerable<ContentPart> OwnParts(ContentPart root) => root.SelfAndDescendants();
}

/// <summary>What a parser hands back: the tree, a block in the language it read (<see cref="BlockNode"/>).</summary>
public sealed record ContentParse(ContentNode Tree)
{
    public static ContentParse Of(ContentNode tree) => new(tree);

    /// <summary>
    /// Where a reading asked to stop softly stopped (<see cref="ContentStretch"/>) — where the next stretch starts — or null
    /// where it read to the end of the source.
    /// </summary>
    public int? Stopped { get; init; }

    /// <summary>
    /// What the parser needs to carry on reading from <see cref="Stopped"/> as though it had never stopped, compared by value: two
    /// readings that stop in equal states read the same from there on. Null where nothing is carried.
    /// </summary>
    public object? Resume { get; init; }
}

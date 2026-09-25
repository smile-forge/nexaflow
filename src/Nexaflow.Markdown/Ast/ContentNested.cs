namespace Nexaflow.Markdown.Ast;

/// <summary>
/// Content written in another language inside this one: a fenced block of a document, a formula in a sentence, a tune on a
/// flowchart node.
///
/// <para>
/// The parser reading the outer content says only that it is there and which language it is in (<see cref="Kinds.Language"/>),
/// and holds the characters as written in the node's <see cref="Roles.Body"/>. What they say is the other language's to read:
/// the engine has that language's parser read them and puts the tree it read in the body (<see cref="Reading"/>) — so the
/// body of a node holding another language is either the characters, unread, or that language's tree.
/// </para>
/// </summary>
public static class ContentNested
{
    /// <summary><paramref name="holder"/>, saying the language its body is written in.</summary>
    public static ContentNode Naming(ContentNode holder, string language) =>
        holder.With([.. holder.Children, ContentNode.Holding(Kinds.Language, Roles.Derived, language)]);

    /// <summary>The word naming the language what <paramref name="node"/> holds is written in, or null where it holds none.</summary>
    public static string? Language(ContentNode node)
    {
        for (var at = node.Children.Count - 1; at >= 0; at--)
            if (node.Children[at] is { Kind: Kinds.Language, Held: string language }) return language;

        return null;
    }

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
    /// What the other language wrote in <paramref name="holder"/> amounts to — its tree, positioned where it was written — or
    /// null where nothing has read it.
    /// </summary>
    public static ContentPart? Read(ContentPart holder) =>
        holder.Part(Roles.Body) is { Kind: Kinds.Nested, Children.Count: > 0 } body ? body.Children[0] : null;

    /// <summary>
    /// <paramref name="holder"/> with <paramref name="read"/> — its body's own characters, as the other language read them —
    /// in its body. The line ending that closed the characters stays the body's last part, as it was.
    /// </summary>
    public static ContentNode Reading(ContentNode holder, ContentNode read)
    {
        var children = new ContentNode[holder.Children.Count];

        for (var at = 0; at < children.Length; at++)
        {
            var child = holder.Children[at];
            if (child.Role != Roles.Body) { children[at] = child; continue; }

            // What the language was not handed is the line ending closing the characters, which stays where it was.
            var written = child.Print();
            var closing = written[read.Width..];

            children[at] = ContentNode.Branch(Kinds.Nested,
                                              closing.Length > 0 ? [read, ContentNode.Leaf(Kinds.Space, closing, Roles.Trivia)] : [read],
                                              Roles.Body);
        }

        return holder.With(children);
    }

    /// <summary>
    /// The stretch of a body that is the other language's own: all of it but the line ending that closes its last line, which
    /// belongs to the line the closing delimiter stands on — written into, that ending would carry what was typed onto the
    /// delimiter's line and the delimiter would no longer close anything.
    /// </summary>
    public static (int Start, int Length) Own(ContentPart body) =>
        body.Children.Count > 0 && body.Children[^1] is { Role: Roles.Trivia } closing
            ? (body.Start, closing.Start - body.Start)
            : (body.Start, body.Length);

    /// <summary>The characters of a body that are the other language's own — what its parser is handed (<see cref="Own(ContentPart)"/>).</summary>
    public static string Own(ContentNode body)
    {
        var written = body.Print();

        return written.EndsWith("\r\n", StringComparison.Ordinal) ? written[..^2]
             : written.EndsWith('\n') ? written[..^1]
             : written;
    }

    /// <summary>Whether the stretch from <paramref name="start"/> to <paramref name="end"/> lies inside a body's own characters.</summary>
    public static bool Holds(ContentPart body, int start, int end)
    {
        var (from, length) = Own(body);
        return from <= start && end <= from + length;
    }

    /// <summary>
    /// <paramref name="root"/> and every part under it written in its own language — none of what another language read inside
    /// it, which that language answers for itself.
    /// </summary>
    public static IEnumerable<ContentPart> OwnParts(ContentPart root)
    {
        var waiting = new Stack<ContentPart>();
        waiting.Push(root);

        while (waiting.Count > 0)
        {
            var part = waiting.Pop();
            yield return part;

            if (part is { Kind: Kinds.Nested, Role: Roles.Body }) continue;

            for (var at = part.Children.Count - 1; at >= 0; at--) waiting.Push(part.Children[at]);
        }
    }

    /// <summary>
    /// Every piece of <paramref name="tree"/> written in another language, in the order written — found by following the counts
    /// each node keeps of what it holds (<see cref="ContentNode.Nests"/>), so only the way down to each piece is visited.
    /// </summary>
    public static IReadOnlyList<NestedPiece> Pieces(ContentNode tree)
    {
        if (tree.Nests == 0) return [];

        var found = new List<NestedPiece>(tree.Nests);
        Find(tree, 0, found);
        return found;
    }

    private static void Find(ContentNode node, int at, List<NestedPiece> found)
    {
        if (Language(node) is { } language)
        {
            for (var index = 0; index < node.Children.Count; index++)
            {
                var child = node.Children[index];
                if (child is { Role: Roles.Body, IsDerived: false })
                {
                    found.Add(new NestedPiece(at, Own(child), language, node.Kind));
                    return;
                }

                at += child.Width;
            }

            return;
        }

        for (var index = 0; index < node.Children.Count; index++)
        {
            var child = node.Children[index];
            if (child.Nests > 0) Find(child, at, found);
            at += child.Width;
        }
    }

}

/// <summary>A piece of content written in another language, as the parser that placed it says where it is.</summary>
/// <param name="At">Where its body starts in what was parsed.</param>
/// <param name="Written">The characters its language reads — the body's own (<see cref="ContentNested.Own(ContentNode)"/>).</param>
/// <param name="Language">The word naming the language.</param>
/// <param name="Holder">The kind of node holding it — a fence, a formula, a label.</param>
public readonly record struct NestedPiece(int At, string Written, string Language, string Holder);

/// <summary>What a parser hands back: the tree, and every piece in it written in another language.</summary>
public sealed record ContentParse(ContentNode Tree, IReadOnlyList<NestedPiece> Nested)
{
    /// <summary>A tree and the pieces in it written in another language, as the tree's own counts say where they are.</summary>
    public static ContentParse Of(ContentNode tree) => new(tree, ContentNested.Pieces(tree));
}

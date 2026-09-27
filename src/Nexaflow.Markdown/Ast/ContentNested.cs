namespace Nexaflow.Markdown.Ast;

/// <summary>
/// Content written in another language inside this one: a fenced block of a document, a formula in a sentence, a tune on a
/// flowchart node.
///
/// <para>
/// The parser reading the outer content holds it as a <see cref="BlockNode"/> in the language it names — its delimiters as
/// trivia, and a <see cref="Kinds.Nested"/> leaf holding the characters as written (<see cref="Roles.Body"/>). What they say is
/// the other language's to read, and it reads them only when a builder asks the engine to lay them out: what comes back is a
/// layout, put wherever the builder wants it. So no tree ever holds another language's nodes, and no builder ever sees one.
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
    /// The stretch of a body that is the other language's own: all of it but the line ending that closes its last line, which
    /// belongs to the line the closing delimiter stands on — written into, that ending would carry what was typed onto the
    /// delimiter's line and the delimiter would no longer close anything.
    /// </summary>
    public static (int Start, int Length) Own(ContentPart body)
    {
        var length = Own(body.Node).Length;
        return (body.Start, length);
    }

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
    /// <paramref name="root"/> and every part under it — none of what is written in another language inside it, which is held as
    /// its characters and answers for itself once it is laid out.
    /// </summary>
    public static IEnumerable<ContentPart> OwnParts(ContentPart root) => root.SelfAndDescendants();
}

/// <summary>What a parser hands back: the tree, a block in the language it read (<see cref="BlockNode"/>).</summary>
public sealed record ContentParse(ContentNode Tree)
{
    public static ContentParse Of(ContentNode tree) => new(tree);
}

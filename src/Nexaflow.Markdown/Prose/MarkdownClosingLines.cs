using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The line break closing the last line of a block held as written — indented code, raw markup, front matter, a link's
/// definition — cut into a piece of its own as the block is read (<see cref="MarkdownBlocks"/>). Not a block in another
/// language, whose characters are that language's, held whole and unread (<see cref="ContentNested"/>).
///
/// <para>
/// That line break is where the next line starts rather than a line of the block's own: a fence's belongs to the line its
/// closing fence stands on. Cut here, what the block holds is exactly the characters drawn, and whatever sets them sets
/// them as they are, without looking at them to see where they stop.
/// </para>
/// </summary>
internal static class MarkdownClosingLines
{
    /// <summary>This piece with the line break closing what it holds as written made a piece of its own, where it is one that does.</summary>
    public static ContentNode Closed(ContentNode node)
    {
        if (node.Kind is not (MarkdownKinds.Code or MarkdownKinds.Html or MarkdownKinds.Reference or MarkdownKinds.FrontMatter)) return node;

        ContentNode[]? cut = null;

        for (var at = 0; at < node.Children.Count; at++)
        {
            var child = node.Children[at];
            var now = Cut(child);

            if (cut is null && !ReferenceEquals(now, child))
            {
                cut = new ContentNode[node.Children.Count];
                for (var before = 0; before < at; before++) cut[before] = node.Children[before];
            }

            if (cut is not null) cut[at] = now;
        }

        return cut is null ? node : node.With(cut);
    }

    /// <summary>
    /// A body with the line break closing what it holds made a piece of its own: the body's, where it is one run of characters, and
    /// its last line's, where it is held line by line with a container's marks between them.
    /// </summary>
    private static ContentNode Cut(ContentNode body)
    {
        if (body.Role != Roles.Body) return body;
        if (body.IsLeaf) return Ending(body, Roles.Body) is { } whole ? ContentNode.Branch(body.Kind, whole, Roles.Body, body.Offset) : body;

        var last = -1;
        for (var at = 0; at < body.Children.Count; at++)
            if (body.Children[at].Role != Roles.Trivia) last = at;

        if (last < 0 || !body.Children[last].IsLeaf || Ending(body.Children[last], body.Children[last].Role) is not { } cut) return body;

        return body.With([.. body.Children.Take(last), .. cut, .. body.Children.Skip(last + 1)]);
    }

    /// <summary>
    /// A run of characters as what comes before the line break closing it and the line break, each a piece — or null where it ends
    /// with none. Both halves were read from where the run was read from, so each says so, and neither says anything where the run
    /// said nothing.
    /// </summary>
    private static ContentNode[]? Ending(ContentNode run, string role)
    {
        var text = run.Text;
        var ending = text.EndsWith("\r\n", StringComparison.Ordinal) ? 2 : text.EndsWith('\n') ? 1 : 0;
        if (ending == 0) return null;

        var at = run.Offset;
        var closing = ContentNode.Leaf(Kinds.Space, text[^ending..], Roles.Trivia, offset: at + (text.Length - ending));

        return text.Length > ending
            ? [ContentNode.Leaf(run.Kind, text[..^ending], role == Roles.Body ? Roles.Element : role, offset: at), closing]
            : [closing];
    }
}

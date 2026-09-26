using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The line break closing the last line of a block held as written — a fence's body, a maths block's, indented code, raw
/// markup, front matter, a link's definition — cut into a piece of its own as the block is read (<see cref="MarkdownBlocks"/>).
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
        if (node.Kind is not (MarkdownKinds.Fence or MarkdownKinds.Math or MarkdownKinds.Code or MarkdownKinds.Html
                              or MarkdownKinds.Reference or MarkdownKinds.FrontMatter)) return node;

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

    /// <summary>A body held as one run of characters, with the line break closing it made a piece of its own.</summary>
    private static ContentNode Cut(ContentNode body)
    {
        if (body.Role != Roles.Body || !body.IsLeaf) return body;

        var text = body.Text;
        var ending = text.EndsWith("\r\n", StringComparison.Ordinal) ? 2 : text.EndsWith('\n') ? 1 : 0;
        if (ending == 0) return body;

        var closing = ContentNode.Leaf(Kinds.Space, text[^ending..], Roles.Trivia);

        return ContentNode.Branch(body.Kind,
                                  text.Length > ending ? [ContentNode.Leaf(body.Kind, text[..^ending]), closing] : [closing],
                                  Roles.Body);
    }
}

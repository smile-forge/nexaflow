using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Prose.Stages;

/// <summary>
/// Cuts the line break closing the last line of a block held as written — a fence's body, a maths block's, indented code,
/// raw markup, front matter, a link's definition — into a piece of its own.
///
/// <para>
/// That line break is where the next line starts rather than a line of the block's own: a fence's belongs to the line its
/// closing fence stands on. Cut here, what the block holds is exactly the characters drawn, and whatever sets them sets
/// them as they are, without looking at them to see where they stop.
/// </para>
/// </summary>
public sealed class WithClosingLines : IAstStage
{
    public string Name => "markdown:closing-lines";

    public ContentNode Run(ContentNode tree) => AstRewrite.Each(tree, Closed);

    private static ContentNode Closed(ContentNode node)
    {
        if (node.Kind is not (MarkdownKinds.Fence or MarkdownKinds.Math or MarkdownKinds.Code or MarkdownKinds.Html
                              or MarkdownKinds.Reference or MarkdownKinds.FrontMatter)) return node;

        var moved = false;
        var children = new List<ContentNode>(node.Children.Count);

        foreach (var child in node.Children)
        {
            var cut = Cut(child);
            moved |= !ReferenceEquals(cut, child);
            children.Add(cut);
        }

        return moved ? node.With(children) : node;
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

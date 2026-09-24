using Markdig.Renderers.Html;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// In-page links: <c>[Searching](#searching)</c> jumps to the heading "Searching". Every heading gets a GitHub-style
/// id as the document is read (lower-case, spaces to hyphens, punctuation dropped, a repeat numbered <c>-1</c>,
/// <c>-2</c>…), the heading's piece answers to it (<see cref="Sought"/>), and the document resolves a <c>#id</c> link to
/// it itself — never asking its host, since a bare anchor means nothing outside the document it sits in.
/// </summary>
public static class MarkdownAnchors
{
    /// <summary>True for a link into the same document (<c>#id</c>), with the id it names, unescaped.</summary>
    public static bool IsInPage(string? url, out string anchor)
    {
        anchor = "";
        if (url is not { Length: > 1 } || url[0] != '#') return false;
        anchor = Uri.UnescapeDataString(url[1..]);
        return true;
    }

    /// <summary>The id the pipeline gave <paramref name="heading"/>, or null — for what reads a page's headings before it is shown.</summary>
    public static string? IdOf(Markdig.Syntax.HeadingBlock heading) => heading.TryGetAttributes()?.Id;

    /// <summary>
    /// The piece a <c>#id</c> points at, on a laid-out document — the heading the reader wrote that name on,
    /// or nothing where the document has no such heading.
    /// </summary>
    public static Piece Sought(Laid laid, string anchor)
    {
        foreach (var piece in laid.Root.SelfAndDescendants())
            if (piece.Part is ContentPart part && Named(part) is { } id
                && string.Equals(id, anchor, StringComparison.OrdinalIgnoreCase))
                return piece;

        return default;
    }

    /// <summary>The name a heading answers to, where the reader worked one out for it.</summary>
    private static string? Named(ContentPart part)
    {
        for (var at = part; at is not null; at = at.Parent)
        {
            if (at.Kind != MarkdownKinds.Heading) continue;

            foreach (var child in at.Children)
                foreach (var held in child.Children)
                    if (held.Node.Kind == MarkdownKinds.Anchor && held.Node.Held is string id)
                        return id;

            return null;
        }

        return null;
    }
}

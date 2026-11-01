
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// In-page links: <c>[Searching](#searching)</c> jumps to the heading "Searching". Every heading gets a GitHub-style
/// id as the document is worked over (lower-case, spaces to hyphens, punctuation dropped, a repeat numbered <c>-1</c>,
/// <c>-2</c>… — <see cref="WithTargets"/>), the heading's piece answers to it (<see cref="Sought"/>), and the document
/// resolves a <c>#id</c> link to it itself — never asking its host, since a bare anchor means nothing outside the document
/// it sits in.
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
}

using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// Where a link or a picture goes, asked the one way whatever reads it.
///
/// <para>
/// Most say so in their own characters — <c>[text](url)</c>, <c>&lt;url&gt;</c>, a bare address — and that stretch is the
/// destination. A link naming a definition, <c>[text][name]</c>, has no address anywhere in it: the definition is written
/// on a line of its own, and the reading that could see both hangs what it says on the link instead.
/// </para>
/// </summary>
public static class MarkdownLinks
{
    /// <summary>Where <paramref name="link"/> goes, or null where it says nowhere.</summary>
    public static string? Goes(ContentNode link)
    {
        foreach (var child in link.Children)
        {
            if (child.Role == MarkdownRoles.Destination) return child.Text;

            if (child.IsDerived)
                foreach (var held in child.Children)
                    if (held.Role == MarkdownRoles.Destination && held.Held is string defined) return defined;
        }

        return null;
    }
}

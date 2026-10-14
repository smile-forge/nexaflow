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
    /// <summary>
    /// Where <paramref name="link"/> goes, or null where it says nowhere: what was worked out for it where anything was — a
    /// definition's address, or the address written with its escapes and entities read — and otherwise the address as written.
    /// </summary>
    public static string? Goes(ContentNode link)
    {
        string? written = null;

        foreach (var child in link.Children)
        {
            if (child.IsDerived)
            {
                foreach (var held in child.Children)
                    if (held.Role == Roles.Destination && held.Held is string defined) return defined;
            }
            else if (written is null && child.Role == Roles.Destination)
            {
                written = child.Text;
            }
        }

        return written;
    }

    /// <summary>
    /// Where <paramref name="link"/> goes, said as a browser is told it, or null where it says nowhere: what <see cref="Goes"/>
    /// says — and of an address written as a link to itself, in angle brackets or bare, what kind of address it is. An e-mail
    /// address is one to write to, and one written bare from <c>www.</c> is fetched over HTTP.
    /// </summary>
    public static string? Address(ContentNode link)
    {
        if (Goes(link) is not { } goes) return null;

        // Only an address written as a link to itself: anything with words of its own says where it goes in full.
        if (link.Part(Roles.Body) is not null || goes.Contains(':', StringComparison.Ordinal)) return goes;

        if (goes.Contains('@', StringComparison.Ordinal)) return "mailto:" + goes;

        return link.Part(Roles.Open) is null && goes.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "http://" + goes : goes;
    }
}

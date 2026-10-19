using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// What a piece of words written as other characters than it reads as stands for: an entity the character it names, an escape
/// the character after its backslash, punctuation typed plainly the typographer's (<see cref="MarkdownKinds.Typographic"/>), an
/// emoji's name the emoji (<see cref="MarkdownKinds.Shortcode"/>).
///
/// <para>
/// Everything that says what words read as — the page drawing them, a document written out, the plain text a search, a reader
/// aloud or a copy takes — asks here, so they all read a piece the same way, and a new piece of this sort is taught to all of
/// them at once.
/// </para>
/// </summary>
public static class MarkdownStandIns
{
    /// <summary>What <paramref name="piece"/> reads as, or null where it reads as what was written — or is no piece of words.</summary>
    public static string? Decoded(ContentNode piece)
    {
        if (!piece.IsLeaf) return null;

        return piece.Kind switch
        {
            MarkdownKinds.Entity => MarkdownEntities.Decoded(piece.Text) ?? piece.Text,
            MarkdownKinds.Escape when piece.Text.Length > 1 => piece.Text[1..],
            MarkdownKinds.Typographic => Typeset(piece.Text, piece.Role) ?? piece.Text,
            MarkdownKinds.Shortcode => MarkdownShortcodes.Decoded(piece.Text) ?? piece.Text,
            _ => null,
        };
    }

    /// <summary>The character the typographer sets for what was typed as <paramref name="written"/>, facing as <paramref name="role"/> says.</summary>
    private static string? Typeset(string written, string role) => written switch
    {
        "--" => "–",
        "---" => "—",
        "..." => "…",
        "'" => role == MarkdownRoles.LeftQuote ? "‘" : "’",
        "\"" => role == MarkdownRoles.LeftQuote ? "“" : "”",
        "<<" => "«",
        ">>" => "»",
        _ => null,
    };
}

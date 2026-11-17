using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Architecture;

/// <summary>
/// What a piece of a title in quotes written as other characters than it reads as stands for — said here and nowhere else, and
/// handed to <see cref="WithWordPieces"/> so whatever sets a title sets that in its place.
///
/// <para>
/// A backslash keeps the character after it, as Mermaid's grammar reads one: <c>\"</c> reads as a quote and <c>\\</c> as a
/// backslash. A <c>\n</c> breaks the line, as it does in every diagram's words, so it is no piece of this. Another letter naming a
/// control character names one — <c>\r</c> and <c>\t</c> a line ending or a tab, which a title drawn on one line shows as a
/// space, as Mermaid's own does; <c>\b</c>, <c>\f</c>, <c>\v</c> and <c>\0</c> characters that draw nothing. A line ending the
/// title runs across is a space for the same reason.
/// </para>
/// </summary>
public static class ArchitectureStandIns
{
    /// <summary>What a line ending drawn on one line is.</summary>
    private const string Spaced = " ";

    /// <summary>What <paramref name="piece"/> reads as, or null where it reads as what was written — or is no piece of a title.</summary>
    public static string? Decoded(ContentNode piece)
    {
        if (!piece.IsLeaf) return null;

        return piece.Kind switch
        {
            ArchitectureKinds.Escape when piece.Text.Length > 1 => Kept(piece.Text[1]),
            ArchitectureKinds.Break => Spaced,
            _ => null,
        };
    }

    /// <summary>What a backslash and <paramref name="kept"/> after it read as.</summary>
    private static string Kept(char kept) => kept switch
    {
        'r' or 't' or '\r' or '\n' => Spaced,
        'b' or 'f' or 'v' or '0' => string.Empty,
        _ => kept.ToString(),
    };
}

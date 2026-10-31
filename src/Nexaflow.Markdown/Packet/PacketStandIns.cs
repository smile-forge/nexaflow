using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Packet;

/// <summary>
/// What a piece of a label written as other characters than it reads as stands for — said here and nowhere else, as
/// <see cref="Prose.MarkdownStandIns"/> says it for prose, and handed to <see cref="Mermaid.WithWordPieces"/> so whatever sets a
/// label sets that in its place.
///
/// <para>
/// A backslash keeps the character after it, as Mermaid's grammars read one: <c>\"</c> reads as a quote and <c>\\</c> as a
/// backslash. A letter naming a control character names one — <c>\n</c>, <c>\r</c> and <c>\t</c> a line ending or a tab, which a
/// label drawn on one line shows as a space, as Mermaid's own does; <c>\b</c>, <c>\f</c>, <c>\v</c> and <c>\0</c> characters that
/// draw nothing. A line ending the label runs across is a space for the same reason.
/// </para>
/// </summary>
public static class PacketStandIns
{
    /// <summary>What a line ending drawn on one line is.</summary>
    private const string Spaced = " ";

    /// <summary>What <paramref name="piece"/> reads as, or null where it reads as what was written — or is no piece of a label.</summary>
    public static string? Decoded(ContentNode piece)
    {
        if (!piece.IsLeaf) return null;

        return piece.Kind switch
        {
            PacketKinds.Escape when piece.Text.Length > 1 => Kept(piece.Text[1]),
            PacketKinds.Break => Spaced,
            _ => null,
        };
    }

    /// <summary>What a backslash and <paramref name="kept"/> after it read as.</summary>
    private static string Kept(char kept) => kept switch
    {
        'n' or 'r' or 't' or '\r' or '\n' => Spaced,
        'b' or 'f' or 'v' or '0' => string.Empty,
        _ => kept.ToString(),
    };
}

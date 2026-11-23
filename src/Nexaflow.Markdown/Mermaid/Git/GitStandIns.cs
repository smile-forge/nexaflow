using System.Text;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Git;

/// <summary>
/// What a piece of a branch's name or a commit's message in quotes, written as other characters than it reads as, stands for —
/// said here and nowhere else, and handed to <see cref="WithWordPieces"/> so whatever sets the words sets that in its place.
///
/// <para>
/// A backslash keeps the character after it, as Mermaid's strings are read: <c>\"</c> reads as a quote and <c>\\</c> as a
/// backslash. A <c>\n</c> breaks the line, as it does in every diagram's words, so it is no piece of this. Another letter naming a
/// control character names one — <c>\r</c> and <c>\t</c> a line ending or a tab, which words drawn on one line show as a space;
/// <c>\b</c>, <c>\f</c>, <c>\v</c> and <c>\0</c> characters that draw nothing. A line ending the string runs across is a space for
/// the same reason.
/// </para>
/// </summary>
public static class GitStandIns
{
    /// <summary>What a line ending drawn on one line is.</summary>
    private const string Spaced = " ";

    /// <summary>What <paramref name="piece"/> reads as, or null where it reads as what was written — or is no piece of a string.</summary>
    public static string? Decoded(ContentNode piece)
    {
        if (!piece.IsLeaf) return null;

        return piece.Kind switch
        {
            GitKinds.Escape when piece.Text.Length > 1 => Kept(piece.Text[1]),
            GitKinds.Break => Spaced,
            _ => null,
        };
    }

    /// <summary>What <paramref name="said"/> reads as, every escape and line ending in it as what it stands for.</summary>
    public static string Reads(ContentNode said)
    {
        if (said.IsLeaf) return Decoded(said) ?? said.Text;

        var read = new StringBuilder();
        foreach (var child in said.Children) read.Append(Reads(child));
        return read.ToString();
    }

    /// <summary>What a backslash and <paramref name="kept"/> after it read as.</summary>
    private static string Kept(char kept) => kept switch
    {
        'r' or 't' or '\r' or '\n' => Spaced,
        'b' or 'f' or 'v' or '0' => string.Empty,
        _ => kept.ToString(),
    };
}

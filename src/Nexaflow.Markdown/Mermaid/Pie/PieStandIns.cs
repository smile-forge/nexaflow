using System.Text;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Pie;

/// <summary>
/// What a piece of a slice's label written as other characters than it reads as stands for — said here and nowhere else, and
/// handed to <see cref="WithWordPieces"/> so whatever sets a label sets that in its place.
///
/// <para>
/// A backslash keeps the character after it, as Mermaid's grammar reads one: <c>\"</c> reads as a quote and <c>\\</c> as a
/// backslash. A <c>\n</c> breaks the line, as it does in every diagram's words, so it is no piece of this. Another letter naming a
/// control character names one — <c>\r</c> and <c>\t</c> a line ending or a tab, which a label drawn on one line shows as a
/// space, as Mermaid's own does; <c>\b</c>, <c>\f</c>, <c>\v</c> and <c>\0</c> characters that draw nothing. A line ending the
/// label runs across is a space for the same reason.
/// </para>
/// </summary>
public static class PieStandIns
{
    /// <summary>What a line ending drawn on one line is.</summary>
    private const string Spaced = " ";

    /// <summary>What <paramref name="piece"/> reads as, or null where it reads as what was written — or is no piece of a label.</summary>
    public static string? Decoded(ContentNode piece)
    {
        if (!piece.IsLeaf) return null;

        return piece.Kind switch
        {
            PieKinds.Escape when piece.Text.Length > 1 => Kept(piece.Text[1]),
            PieKinds.Break => Spaced,
            _ => null,
        };
    }

    /// <summary>What the words of a label read as, every escape and line ending in them as what it stands for.</summary>
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

    /// <summary>What a slice is called, as its label reads — or null for one whose label could not be read.</summary>
    public static string? Label(ContentNode slice) => slice.Inner(Kinds.Words) is { } said ? Reads(said) : null;

    /// <summary>
    /// For each slice of <paramref name="tree"/>, in the order they are written, whether a slice above it is called the same:
    /// Mermaid keeps the first slice of a label and draws nothing for any other, so such a slice is no part of the pie.
    /// </summary>
    public static IReadOnlyList<bool> Repeats(ContentNode tree)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        return [.. tree.SelfAndDescendants().Where(node => node.Kind == PieKinds.Slice).Select(slice => Label(slice) is { } label && !named.Add(label))];
    }
}

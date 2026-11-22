using System.Text;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Treemap;

/// <summary>
/// What a piece of an item's label written as other characters than it reads as stands for — said here and nowhere else, and
/// handed to <see cref="Mermaid.WithWordPieces"/> so whatever sets a label sets that in its place. A line ending the label runs
/// across is a space, as a label drawn on one line shows it.
/// </summary>
public static class TreemapStandIns
{
    /// <summary>What a line ending drawn on one line is.</summary>
    private const string Spaced = " ";

    /// <summary>What <paramref name="piece"/> reads as, or null where it reads as what was written — or is no piece of a label.</summary>
    public static string? Decoded(ContentNode piece) => piece.IsLeaf && piece.Kind == TreemapKinds.Break ? Spaced : null;

    /// <summary>What the words of a label read as, every line ending in them as what it stands for.</summary>
    public static string Reads(ContentNode said)
    {
        if (said.IsLeaf) return Decoded(said) ?? said.Text;

        var read = new StringBuilder();
        foreach (var child in said.Children) read.Append(Reads(child));
        return read.ToString();
    }
}

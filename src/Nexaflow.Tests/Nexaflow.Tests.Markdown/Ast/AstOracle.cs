using Nexaflow.Markdown.Ast;

namespace Nexaflow.Tests.Markdown.Ast;

/// <summary>
/// What a parser has to have said about where it read a tree from, checked without trusting the order of the tree.
///
/// <para>
/// A round-trip check alone cannot tell a piece that says where it was read from apart from one that says nothing, because
/// a tree printed in the order it was built comes out the same either way. These two checks can: <see cref="Faults"/> asks
/// every piece standing for characters where it was read from and holds the answer against the source, and
/// <see cref="Reversed"/> puts the parts of every piece in the opposite order, which only still prints as the source if
/// every piece of it knows its own place. Together they are what makes an offset provable rather than plausible.
/// </para>
/// </summary>
internal static class AstOracle
{
    /// <summary>
    /// The same tree with the parts of every piece in the opposite order — the sharpest statement of a stage being free to
    /// hand back the pieces it was given in any order it likes.
    /// </summary>
    public static ContentNode Reversed(ContentNode node) =>
        node.Children.Count == 0 ? node : node.With([.. node.Children.Select(Reversed).Reverse()]);

    /// <summary>
    /// What is wrong with where <paramref name="tree"/> says its pieces were read from in <paramref name="source"/>, which is
    /// nothing for a tree a parser has said all of it about.
    ///
    /// <para>
    /// Every piece standing for characters has to say where it was read from, because a piece standing for characters is the
    /// only thing that stands for any. A piece made of parts may say, and is held to it where it does; one that does not is a
    /// stage grouping what it was handed, which reads no source and so has nothing to say about it.
    /// </para>
    /// <para>
    /// Nothing under a derived piece is asked, for the same reason nothing under one is printed: a stage may hang whatever
    /// explains something there, and none of it stands for characters anybody wrote.
    /// </para>
    /// </summary>
    public static IEnumerable<string> Faults(string source, ContentNode tree)
    {
        if (tree.IsDerived) yield break;

        if (tree.Offset is not { } offset)
        {
            if (tree.IsLeaf && tree.Width > 0) yield return $"{Named(tree)} does not say where it was read from";
        }
        else if (offset < 0 || offset + tree.Width > source.Length)
        {
            yield return $"{Named(tree)} claims {offset}+{tree.Width} of {source.Length}";
        }
        else if (source.Substring(offset, tree.Width) is var said && !string.Equals(said, tree.Print(), StringComparison.Ordinal))
        {
            yield return $"{Named(tree)} at {offset} prints {Shortened(tree.Print())} where the source says {Shortened(said)}";
        }

        foreach (var child in tree.Children)
            foreach (var fault in Faults(source, child))
                yield return fault;
    }

    /// <summary>What to call a piece in a complaint about it.</summary>
    private static string Named(ContentNode node) => $"{node.Kind}[{node.Role}]";

    /// <summary>Text short enough to read in a failure, quoted and with its line breaks shown.</summary>
    private static string Shortened(string text)
    {
        var said = text.Replace("\r", "\\r").Replace("\n", "\\n");

        return said.Length <= 40 ? $"'{said}'" : $"'{said[..40]}'...";
    }
}

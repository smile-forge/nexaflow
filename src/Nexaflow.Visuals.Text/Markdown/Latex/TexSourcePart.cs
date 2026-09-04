using Nexaflow.Maths.Latex;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// A part of a formula's parse tree, offered to the editor as the stretch of source it is named by.
///
/// <para>
/// Which characters a part is <em>named by</em> is not always the characters it spans: a braced argument
/// is named by its contents and a cell by its ink, because handing over the honest span instead selects
/// the braces along with what is in them, and re-braces an argument that is already braced. That is a
/// convention about editing rather than a fact about the formula, so it lives on this side of the
/// boundary — the reading stays true, and the editor is told what it needs.
/// </para>
/// <para>
/// It exists at all because the reading cannot implement <see cref="ISourcePart"/> itself: that
/// interface belongs to the editing seam, and a parse tree that had to know about the editor would be
/// the wrong way round. One of these is made per piece when the formula is laid out, so it is as stable
/// as the part it wraps and can be compared by reference like one.
/// </para>
/// </summary>
internal sealed class TexSourcePart(TexPart of) : ISourcePart
{
    /// <summary>The part itself, for every question that is about the formula rather than the source.</summary>
    public TexPart Of { get; } = of;

    /// <inheritdoc/>
    public int Start => Named.Start;

    /// <inheritdoc/>
    public int Length => Named.Length;

    private (int Start, int Length) Named => Of.Kind switch
    {
        TexKind.Group => Of.Contents,
        TexKind.Cell => Of.Written,
        _ => (Of.Start, Of.Length),
    };

    public override string ToString() => $"{Of.Role}[{Start},{Length}]";
}

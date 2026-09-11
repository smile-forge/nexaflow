using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Where a caret is: a piece of the layout, and which edge of it the caret stands against.
///
/// <para>
/// An offset alone cannot say it, and that is the whole reason this exists. Two things a reader sees as
/// different places are one position in the text — inside the exponent of <c>x^2</c> and past the whole
/// script finish at the same character — and on the page the difference is plain: one is half the height
/// of the other and raised off the line. The piece is what tells them apart, because the piece is what
/// the caret is standing against.
/// </para>
/// <para>
/// This used to be an offset and an index into the bars drawn there, which meant every question about
/// the caret went through a list rebuilt from the offset, and the second bar of <c>x^2</c> — the one past
/// the script — was invented by the layout rather than declared by anyone. Now a builder declares which
/// edges of which pieces a caret may rest on (<see cref="Stops"/>) and a place is one of them.
/// </para>
/// <para>
/// <strong>It is only ever true of the tree it came from.</strong> A rebuild makes a new tree, so a place
/// held across one is stale — and the caret goes back to the innermost place at its offset, which is
/// where a reader who has just typed something is. The source offset is what survives an edit; this is
/// what survives a step.
/// </para>
/// </summary>
/// <param name="Against">The piece the caret is standing against.</param>
/// <param name="Trailing">Whether it is at that piece's far edge rather than its near one.</param>
public readonly record struct CaretPlace(Piece Against, bool Trailing)
{
    /// <summary>Whether this is anywhere at all.</summary>
    public bool Exists => Against.Exists;

    /// <summary>The offset in the source — which end of the piece depends on which edge this is.</summary>
    public int Offset
    {
        get
        {
            var at = Against.Sits();
            return Trailing ? at.Start + at.Length : at.Start;
        }
    }
}

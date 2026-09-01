namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Where a caret is: an offset in the source, and which of the bars drawn at that offset.
/// <para>
/// An offset alone cannot say it. Two things a reader sees as different places are one position in the
/// text — after the <c>6</c> and before the <c>+</c> of <c>6+5</c> are the same character boundary, and
/// so are inside the exponent of <c>x^2</c> and past the whole script — and in a formula the difference
/// is plain on the page: one is a hand's width to the left of the other, or half its height and raised.
/// So the caret is the position <em>and</em> which side of the gap, or how far out of the construct, it
/// is being drawn.
/// </para>
/// <para>
/// <see cref="Level"/> indexes the bars at the offset, in the order an arrow key visits them: against the
/// thing that ends there first, then out through anything that ends where its contents do, then against
/// the thing that starts there. Where two of those would be drawn in the same place they are one, so the
/// reader never presses an arrow twice for a caret that does not appear to move.
/// </para>
/// <para>
/// It survives a step and nothing else. Every edit, click and jump puts the caret back at level 0 —
/// innermost, which is where a reader who has just typed something is — so nothing has to remember to
/// clear it.
/// </para>
/// </summary>
/// <param name="Offset">Offset into the source.</param>
/// <param name="Level">Which bar at that offset, from innermost.</param>
public readonly record struct CaretPlace(int Offset, int Level)
{
    /// <summary>The innermost place at an offset — what any position not arrived at by stepping means.</summary>
    public static CaretPlace At(int offset) => new(offset, 0);
}

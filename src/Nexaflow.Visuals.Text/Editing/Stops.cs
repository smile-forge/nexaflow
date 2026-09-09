namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Where a caret may rest against a piece — which is to say, where the reader may write.
///
/// <para>
/// A property of the piece, said by the thing that built it, because only that knows whether what it has
/// made can be written before and after. The layout used to work it out from wherever a piece happened to
/// name characters, and then compensate in <c>CaretBars</c> for the cases that misses — <c>x^{2}</c>
/// closes its exponent with a brace and so has an offset of its own for "past the script", while
/// <c>x^2</c> has none, and the layout was inventing one by looking for an enclosure.
/// </para>
/// <para>
/// It goes with the part, because a stop is somewhere text can be inserted and a piece naming no source
/// has nothing to insert into. The tree is honoured: a piece with a part inside another with a part has
/// stops of its own <em>and</em> its parent's, which is what puts two bars at the same offset — one just
/// inside the script and one past it.
/// </para>
/// <para>
/// Content with a structure that needs something else says so, or nests its pieces differently, which is
/// the same statement made in the shape of the tree.
/// </para>
/// </summary>
[System.Flags]
public enum Stops
{
    /// <summary>Nowhere against this piece — it is drawn, and cannot be written beside.</summary>
    None = 0,

    /// <summary>A caret may rest where it begins.</summary>
    Before = 1,

    /// <summary>A caret may rest where it ends.</summary>
    After = 2,

    /// <summary>Both ends, which is what a piece of written content usually offers.</summary>
    Both = Before | After,
}

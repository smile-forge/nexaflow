using XamlMath.Boxes;

namespace XamlMath.Atoms;

/// <summary>
/// A matrix cell that covers more than one column, and is drawn to the width of the columns it
/// covers rather than setting a width of its own. Everything else in a matrix is measured and then
/// the columns sized around it; this is the other way round, so <see cref="MatrixAtom"/> leaves such
/// a cell out of its measurements and builds it once the columns are known.
/// </summary>
internal interface ISpanningMatrixCell
{
    /// <summary>How many columns the cell covers, counting the one it sits in.</summary>
    int ColumnSpan { get; }

    /// <summary>Draws the cell into the width the columns it spans came out to.</summary>
    Box CreateSpanningBox(TexEnvironment environment, double width);
}

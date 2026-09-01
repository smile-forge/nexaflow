using System.Collections.Generic;
using System.Linq;
using XamlMath.Atoms;
using XamlMath.Exceptions;

namespace XamlMath.Parsers.Matrices;

/// <summary>
/// The <c>array</c> environment. Unlike the other matrix-shaped environments it takes an argument -
/// the column preamble - which sits at the front of its body, since <c>\begin</c> hands the whole of
/// what follows the environment name over as one span.
/// </summary>
internal sealed class ArrayCommandParser
{
    internal static readonly ArrayCommandParser Instance = new();

    private ArrayCommandParser()
    {
    }

    /// <summary>
    /// The arrangement, once the cells are built — see
    /// <see cref="MatrixCommandParser.Assemble"/>, which this is the <c>array</c> half of.
    /// </summary>
    internal static Atom Assemble(
        SourceSpan? source,
        IEnumerable<IEnumerable<Atom?>> cells,
        ArrayColumnSpec spec,
        IReadOnlyCollection<int>? horizontalRules,
        Nexaflow.Maths.Latex.TexPart? origin = null) =>
        new MatrixAtom(
            source,
            cells,
            MatrixCellAlignment.Center,
            // Rows a line apart, struts rather than padding — the same as any other table, and the half of
            // this that had been left at the defaults. An array without them spaced its rows by the 0.35 of
            // DefaultPadding instead of a stretched line, so a five-row grid came out squashed to a third of
            // its height; and because \left( sizes itself to what it encloses, the brackets came out short
            // with it. Three complaints, one cause.
            verticalPadding: 0,
            // An array keeps its outer gaps, unlike a matrix: that is the space you see inside the
            // brackets of \left[\begin{array}{cc|c} … \right].
            horizontalPadding: MatrixAtom.DefaultColumnGap,
            suppressOuterPadding: true,
            columnSpec: spec,
            horizontalRules: horizontalRules,
            rowStrutHeight: MatrixAtom.DefaultRowStrutHeight,
            rowStrutDepth: MatrixAtom.DefaultRowStrutDepth)
        {
            Origin = origin,
        };

    private static void MakeRectangular(List<List<Atom>> rowAtoms)
    {
        var maxRowLength = rowAtoms.Max(r => r.Count);
        foreach (var row in rowAtoms.Where(r => r.Count < maxRowLength))
        {
            while (row.Count < maxRowLength)
                row.Add(new NullAtom());
        }
    }
}

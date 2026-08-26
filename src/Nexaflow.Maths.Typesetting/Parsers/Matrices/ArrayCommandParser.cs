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
internal sealed class ArrayCommandParser : IEnvironmentParser
{
    internal static readonly ArrayCommandParser Instance = new();

    private ArrayCommandParser()
    {
    }

    public EnvironmentProcessingResult ProcessEnvironment(EnvironmentContext context)
    {
        var body = context.EnvironmentBodySource;
        var position = 0;
        var preamble = TexFormulaParser.ReadElementGroupOptional(body, ref position, '{', '}')
            ?? throw new TexParseException(@"\begin{array} needs a column preamble, e.g. \begin{array}{cc}.");

        var spec = ArrayColumnSpec.Parse(preamble.ToString());
        var cellsSource = body.Segment(position);

        var rows = new List<List<Atom>> { new List<Atom>() };
        var hlines = new HashSet<int>();
        var environment = new MatrixInternalEnvironment(context.Environment, rows, hlines);

        var lastCellAtom = context.Parser.Parse(cellsSource, context.Formula.TextStyle, environment).RootAtom;
        if (lastCellAtom != null)
            rows.Last().Add(lastCellAtom);

        // A trailing row separator leaves an empty row behind; an array that ends with \hline is the
        // usual way to get one, and it should not add a blank line to the grid.
        if (rows.Count > 1 && rows.Last().Count == 0)
            rows.RemoveAt(rows.Count - 1);

        MakeRectangular(rows);

        var atom = new MatrixAtom(
            context.EnvironmentSource,
            rows,
            MatrixCellAlignment.Center,
            // An array keeps its outer gaps, unlike a matrix: that is the space you see inside the
            // brackets of \left[\begin{array}{cc|c} … \right].
            horizontalPadding: MatrixAtom.DefaultColumnGap,
            columnSpec: spec,
            horizontalRules: hlines);
        return new EnvironmentProcessingResult(atom);
    }

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

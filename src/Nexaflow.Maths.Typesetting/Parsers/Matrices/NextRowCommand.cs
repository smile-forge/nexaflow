using System.Collections.Generic;
using System.Linq;
using XamlMath.Atoms;

namespace XamlMath.Parsers.Matrices;

internal sealed class NextRowCommand : ICommandParser
{
    private readonly List<List<Atom>> _rows;

    public NextRowCommand(List<List<Atom>> rows)
    {
        _rows = rows;
    }

    /// <summary>
    /// Closes the cell being read and adds it to the last row.
    /// </summary>
    /// <param name="at">
    /// Where the separator that closed it is. An empty cell is a hole rather than nothing at all: it
    /// takes a <see cref="PlaceholderAtom"/> standing exactly where its contents would have begun, so it
    /// draws a box the reader can aim at — and, just as importantly, so it is a node with a place. A
    /// <c>NullAtom</c> has neither a box nor a source position, which left a matrix's empty cells absent
    /// from the tree: nothing downstream could say which column it was even looking at.
    /// </param>
    internal static void NextCell(List<List<Atom>> rows, TexFormula formula, SourceSpan at, bool placeholders)
    {
        var currentAtom = formula.RootAtom ?? Hole(at, placeholders);
        formula.RootAtom = null;

        var lastRow = rows.Last();
        lastRow.Add(currentAtom);
    }

    /// <summary>
    /// A cell with nothing written in it, standing where the writing would have gone - or, where the
    /// parse is not for an editor, the nothing it has always been.
    /// </summary>
    internal static Atom Hole(SourceSpan at, bool placeholders) =>
        placeholders ? new PlaceholderAtom(at.Segment(0, 0)) : new NullAtom();

    public CommandProcessingResult ProcessCommand(CommandContext context)
    {
        NextCell(
            _rows,
            context.Formula,
            context.CommandSource.Segment(context.CommandNameStartPosition, 0),
            context.Environment.Placeholders);
        _rows.Add(new List<Atom>());

        return new CommandProcessingResult(null, context.ArgumentsStartPosition);
    }
}

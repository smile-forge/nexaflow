using System.Collections.Generic;
using System.Linq;
using XamlMath.Atoms;
using XamlMath.Exceptions;

namespace XamlMath.Parsers.Matrices;

/// <summary>A parser for matrix-like constructs.</summary>
internal sealed class MatrixCommandParser : ICommandParser, IEnvironmentParser
{
    // An aligned block is not a table: its columns are an equation and its parts, so they keep the
    // close spacing they had rather than taking a column gap.
    internal static readonly MatrixCommandParser Align = new(
        null, null, MatrixCellAlignment.Aligned,
        verticalPadding: MatrixAtom.DefaultPadding, horizontalPadding: MatrixAtom.DefaultPadding);
    internal static readonly MatrixCommandParser Cases = new("lbrace", null, MatrixCellAlignment.Left);
    internal static readonly MatrixCommandParser Matrix = new(null, null, MatrixCellAlignment.Center);
    internal static readonly MatrixCommandParser PMatrix = new("(", ")", MatrixCellAlignment.Center); // \pmatrix ( )
    internal static readonly MatrixCommandParser BMatrix = new("lbrack", "rbrack", MatrixCellAlignment.Center); // \bmatrix [ ]
    internal static readonly MatrixCommandParser BbMatrix = new("lbrace", "rbrace", MatrixCellAlignment.Center); // \Bmatrix { }
    internal static readonly MatrixCommandParser VMatrix = new("vert", "vert", MatrixCellAlignment.Center); // \vmatrix | |
    internal static readonly MatrixCommandParser VvMatrix = new("Vert", "Vert", MatrixCellAlignment.Center); // \Vmatrix ‖ ‖
    internal static readonly MatrixCommandParser Gathered = new(null, null, MatrixCellAlignment.Center);

    // \smallmatrix is an inline matrix: the same layout, set in script size.
    internal static readonly MatrixCommandParser SmallMatrix =
        new(null, null, MatrixCellAlignment.Center, TexStyle.Script);

    // \substack stacks the lines of a big operator's limit: script size like \smallmatrix, but set
    // solid, since the lines belong to one limit rather than to separate rows of a table.
    internal static readonly MatrixCommandParser SubStack =
        new(null, null, MatrixCellAlignment.Center, TexStyle.Script, verticalPadding: 0.1, horizontalPadding: 0);

    private readonly string? _leftDelimiterSymbolName;
    private readonly string? _rightDelimiterSymbolName;
    private readonly MatrixCellAlignment _cellAlignment;
    private readonly TexStyle? _style;
    private readonly double _verticalPadding;
    private readonly double _horizontalPadding;
    private readonly bool _rowStrut;

    private MatrixCommandParser(
        string? leftDelimiterSymbolName,
        string? rightDelimiterSymbolName,
        MatrixCellAlignment cellAlignment,
        TexStyle? style = null,
        double verticalPadding = 0,
        double horizontalPadding = MatrixAtom.DefaultColumnGap)
    {
        _leftDelimiterSymbolName = leftDelimiterSymbolName;
        _rightDelimiterSymbolName = rightDelimiterSymbolName;
        _cellAlignment = cellAlignment;
        _style = style;
        _verticalPadding = verticalPadding;
        _horizontalPadding = horizontalPadding;

        // A table struts its rows a line apart; an aligned block and a stacked limit set theirs solid
        // and space them with padding of their own instead.
        _rowStrut = verticalPadding == 0;
    }

    public CommandProcessingResult ProcessCommand(CommandContext context)
    {
        var position = context.ArgumentsStartPosition;
        var source = context.CommandSource;

        if (position == source.Length)
            throw new TexParseException("illegal end!");

        var afterCells = TexFormulaParser.ReadElement(source, position);
        position = afterCells.position;
        var cellsSource = afterCells.source;
        var matrixSource = context.CommandSource.Segment(
            context.CommandNameStartPosition,
            position - context.CommandNameStartPosition);

        var envContext = new EnvironmentContext(
            context.Parser,
            context.Formula,
            context.Environment,
            matrixSource,
            cellsSource);
        var result = ProcessEnvironment(envContext);
        return new CommandProcessingResult(result.Atom, position, result.AppendMode);
    }

    public EnvironmentProcessingResult ProcessEnvironment(EnvironmentContext context)
    {
        var cellsSource = context.EnvironmentBodySource;
        var matrixSource = context.EnvironmentSource;

        var cells = ReadMatrixCells(context.Parser, context.Formula, cellsSource, context.Environment);
        // A matrix has no outer gap - its brackets sit against its contents - but an aligned block is
        // not bracketed and its columns are its own business, so it keeps what it had.
        var matrix = new MatrixAtom(
            matrixSource,
            cells,
            _cellAlignment,
            _verticalPadding,
            _horizontalPadding,
            suppressOuterPadding: _cellAlignment != MatrixCellAlignment.Aligned,
            rowStrutHeight: _rowStrut ? MatrixAtom.DefaultRowStrutHeight : 0,
            rowStrutDepth: _rowStrut ? MatrixAtom.DefaultRowStrutDepth : 0);

        SymbolAtom? GetDelimiter(string? name) =>
            name == null
                ? null
                : TexFormulaParser.GetDelimiterSymbol(name, null) ??
                  throw new TexParseException($"The delimiter {name} could not be found");

        SymbolAtom? leftDelimiter = GetDelimiter(_leftDelimiterSymbolName);
        SymbolAtom? rightDelimiter = GetDelimiter(_rightDelimiterSymbolName);

        var atom = leftDelimiter == null && rightDelimiter == null
            ? (Atom)matrix
            : new FencedAtom(
                matrixSource,
                matrix,
                leftDelimiter,
                rightDelimiter);

        if (_style is { } style)
            atom = new StyleAtom(matrixSource, atom, style);

        return new EnvironmentProcessingResult(atom);
    }

    private static List<List<Atom>> ReadMatrixCells(
        TexFormulaParser parser,
        TexFormula formula,
        SourceSpan source,
        ICommandEnvironment parentEnvironment)
    {
        var rows = new List<List<Atom>> { new List<Atom>() }; // enter first row by default

        // Commands from the environment will add all the finished cells to the matrix body, but the last one should
        // be extracted here.
        var environment = new MatrixInternalEnvironment(parentEnvironment, rows);
        var lastCellAtom = parser.Parse(source, formula.TextStyle, environment).RootAtom;
        {
            var lastRow = rows.LastOrDefault();
            if (lastRow == null)
                rows.Add(lastRow = new List<Atom>());

            // A cell with nothing in it is still a cell, so a trailing `&` leaves a hole to write into
            // rather than nothing at all — the last cell was the one position in a matrix where it did.
            //
            // But only where a row is actually being closed. A trailing `\\` opens a row that is not a
            // row, and the rule just below drops it for being empty; putting a hole in it would keep it,
            // and "a & b \\" would set as a matrix with a blank line hanging under it.
            if (lastCellAtom != null) lastRow.Add(lastCellAtom);
            else if (lastRow.Count > 0)
                lastRow.Add(NextRowCommand.Hole(
                    source.Segment(source.Length, 0), parentEnvironment.Placeholders));
        }

        // "a & b \ c & d \\" is a normal way to write a matrix out, and the \ at the end closes the
        // last row rather than opening another. An empty row left behind would be a blank line the
        // grid grows to fit, and the delimiters around it grow again to cover that.
        if (rows.Count > 1 && rows[rows.Count - 1].Count == 0)
            rows.RemoveAt(rows.Count - 1);

        MakeRectangular(rows, source, parentEnvironment.Placeholders);

        return rows;
    }

    /// <summary>
    /// Squares off a ragged matrix. The cells that were never written are holes like any other empty one,
    /// standing at the end of the row they complete - so every position in the grid is a node with a
    /// place, and "the third column" means something in every row.
    /// </summary>
    private static void MakeRectangular(List<List<Atom>> rowAtoms, SourceSpan source, bool placeholders)
    {
        var maxRowLength = rowAtoms.Max(r => r.Count);
        foreach (var row in rowAtoms.Where(r => r.Count < maxRowLength))
        {
            var end = row.LastOrDefault()?.Source is { } last
                ? last.Segment(last.Length, 0)
                : source.Segment(source.Length, 0);

            while (row.Count < maxRowLength)
                row.Add(NextRowCommand.Hole(end, placeholders));
        }
    }
}

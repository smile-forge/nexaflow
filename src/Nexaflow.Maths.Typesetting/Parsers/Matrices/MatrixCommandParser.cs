using System.Collections.Generic;
using System.Linq;
using XamlMath.Atoms;
using XamlMath.Exceptions;

namespace XamlMath.Parsers.Matrices;

/// <summary>A parser for matrix-like constructs.</summary>
internal sealed class MatrixCommandParser
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

    /// <summary>
    /// The arrangement, once the cells are built: how they are padded and aligned, what is drawn round
    /// them, and at what size.
    /// <para>
    /// Apart from reading them, because they can arrive already built. <see cref="TexFormulaBuilder"/>
    /// has a reading of the same source that keeps what this one drops, and it needs this half of the
    /// job done exactly as it is done here — which it is, by being the same code rather than a copy of
    /// it that would drift the first time a padding changed.
    /// </para>
    /// </summary>
    /// <param name="origin">
    /// The <c>\begin</c> all of this was written as, when there is a parse tree behind it. Every atom
    /// made here gets it: a bracketed matrix comes back as a fence holding a grid, and a small one as a
    /// style holding that, and they are one construct drawn in parts — as a fraction's box and its bar
    /// are. Passed in rather than hung on afterwards because a style atom names no parts, so there is no
    /// walking into one from outside to find what it holds.
    /// </param>
    internal Atom Assemble(
        SourceSpan? source,
        IEnumerable<IEnumerable<Atom?>> cells,
        Nexaflow.Maths.Latex.TexPart? origin = null)
    {
        // A matrix has no outer gap - its brackets sit against its contents - but an aligned block is
        // not bracketed and its columns are its own business, so it keeps what it had.
        var matrix = new MatrixAtom(
            source,
            cells,
            _cellAlignment,
            _verticalPadding,
            _horizontalPadding,
            suppressOuterPadding: _cellAlignment != MatrixCellAlignment.Aligned,
            rowStrutHeight: _rowStrut ? MatrixAtom.DefaultRowStrutHeight : 0,
            rowStrutDepth: _rowStrut ? MatrixAtom.DefaultRowStrutDepth : 0)
        {
            Origin = origin,
        };

        SymbolAtom? GetDelimiter(string? name) =>
            name == null
                ? null
                : TexFormulaParser.GetDelimiterSymbol(name, null) ??
                  throw new TexParseException($"The delimiter {name} could not be found");

        SymbolAtom? leftDelimiter = GetDelimiter(_leftDelimiterSymbolName);
        SymbolAtom? rightDelimiter = GetDelimiter(_rightDelimiterSymbolName);

        var atom = leftDelimiter == null && rightDelimiter == null
            ? (Atom)matrix
            : new FencedAtom(source, matrix, leftDelimiter, rightDelimiter) { Origin = origin };

        if (_style is { } style)
            atom = new StyleAtom(source, atom, style) { Origin = origin };

        return atom;
    }

    /// <summary>
    /// Squares off a ragged matrix. The cells that were never written are holes like any other empty one,
    /// standing at the end of the row they complete - so every position in the grid is a node with a
    /// place, and "the third column" means something in every row.
    /// </summary>
    private static void MakeRectangular(List<List<Atom>> rowAtoms)
    {
        var maxRowLength = rowAtoms.Max(r => r.Count);
        foreach (var row in rowAtoms.Where(r => r.Count < maxRowLength))
            while (row.Count < maxRowLength)
                row.Add(new NullAtom());
    }
}

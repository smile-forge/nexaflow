using System.Collections.Generic;
using System.Linq;
using XamlMath.Exceptions;
using Nexaflow.Markdown.Ast;

namespace XamlMath.Parsers.Matrices;

/// <summary>A parser for matrix-like constructs.</summary>
internal sealed class MatrixCommandParser
{
    // TeX's line spacing for a table, in em: a baseline skip stretched as \arraystretch does, and the strut each row
    // stands on so short rows keep the spacing of tall ones.
    private const double BaselineSkip = 1.2;
    private const double ArrayStretch = 1.15;
    internal const double DefaultPadding = 0.35;
    internal const double DefaultColumnGap = 1.0;
    internal const double DefaultRowStrutHeight = 0.7 * BaselineSkip * ArrayStretch;
    internal const double DefaultRowStrutDepth = 0.3 * BaselineSkip * ArrayStretch;

    // An aligned block is not a table: its columns are an equation and its parts, so they keep the
    // close spacing they had rather than taking a column gap.
    internal static readonly MatrixCommandParser Align = new(
        null, null, MatrixCellAlignment.Aligned,
        verticalPadding: DefaultPadding, horizontalPadding: DefaultPadding);
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
        double horizontalPadding = DefaultColumnGap)
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

    internal string? LeftDelimiter => _leftDelimiterSymbolName;
    internal string? RightDelimiter => _rightDelimiterSymbolName;
    internal MatrixCellAlignment CellAlignment => _cellAlignment;
    internal TexStyle? Style => _style;
    internal double VerticalPadding => _verticalPadding;
    internal double HorizontalPadding => _horizontalPadding;
    internal bool RowStrut => _rowStrut;
}

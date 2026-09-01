using System;
using System.Collections.Generic;
using System.Linq;
using XamlMath.Boxes;
using XamlMath.Parsers.Matrices;
using SurroundingGap = System.Tuple<double, double>;

namespace XamlMath.Atoms;

/// <summary>An atom representing a tabular arrangement of atoms.</summary>
internal sealed record MatrixAtom : Atom
{
    /// <summary>
    /// Every cell, row by row, each saying which row and column it is — what makes a block of a matrix
    /// answerable as cells, and a single cell answerable as "the one in the second column".
    /// <para>
    /// The coordinates are the point. A flat run of "cell" says a matrix is made of cells and nothing
    /// more, so anything wanting to move a column, insert a row or even name what the reader had picked
    /// had to work it out again from where the cells were drawn — geometry standing in for structure the
    /// parse already knew.
    /// </para>
    /// </summary>
    public override IReadOnlyList<FormulaSlot> Slots =>
        MatrixCells
            .SelectMany((row, r) => row.Select((cell, c) => (Cell: cell, Row: r, Column: c)))
            .Where(x => x.Cell is not null)
            .Select(x => new FormulaSlot("cell", x.Cell!, x.Row, x.Column))
            .ToList();

    /// <summary>Used for grouping of align statements into several columns.</summary>
    /// <remarks>
    /// See section "Aligning several equations" of
    /// <a href="https://www.overleaf.com/learn/latex/Aligning_equations_with_amsmath">this article</a> for details.
    /// </remarks>
    private const double AlignGroupLeftPadding = 4;
    public const double DefaultPadding = 0.35;

    /// <summary>
    /// The space between two columns of a matrix or an array: TeX's 2 x rraycolsep, 10pt. Half of
    /// it sits on each side of a cell, so a matrix that suppresses its outer half-gaps still has the
    /// full amount between its columns.
    /// </summary>
    public const double DefaultColumnGap = 1.0;

    /// <summary>A line of type: TeX's aselineskip at the size a formula is set at.</summary>
    private const double BaselineSkip = 1.2;

    /// <summary>
    /// TeX's <c>rraystretch</c>: how much room every row of a table is given, as a multiple of a
    /// line. One is TeX's own default and reads tight; papers routinely open it up, and this is set
    /// where a rendering sits beside a published one without the rows looking crowded.
    /// </summary>
    private const double ArrayStretch = 1.15;

    /// <summary>
    /// The least a row of a table measures above its baseline. TeX struts every row of an array with
    /// 0.7 of a line above and 0.3 below, so rows sit a line apart whatever they hold and then abut,
    /// rather than being padded away from each other.
    /// </summary>
    public const double DefaultRowStrutHeight = 0.7 * BaselineSkip * ArrayStretch;

    /// <inheritdoc cref="DefaultRowStrutHeight"/>
    public const double DefaultRowStrutDepth = 0.3 * BaselineSkip * ArrayStretch;

    /// <summary>
    /// The least room left between one row's ink and the next's — TeX's <c>\lineskip</c>, 1pt.
    ///
    /// <para>
    /// The struts space rows a line apart whatever they hold, but they are a floor and not a gap: a row
    /// taller than its strut keeps its own height, and two such rows abut exactly. Boxes abutting is not
    /// the same as ink abutting — a glyph may sit a shade outside the metrics that describe it — so
    /// <c>\frac T m</c> over <c>\frac 1 3</c> put the m and the 1 in the same pixels.
    /// </para>
    /// <para>
    /// TeX inserts this between two boxes exactly when the natural spacing would be tighter, which is what
    /// the maximum below says: where the strut binds it already leaves more than this, and where it does
    /// not this is what keeps the rows apart.
    /// </para>
    /// </summary>
    private const double LineSkip = 0.1;


    public MatrixAtom(
        SourceSpan? source,
        IEnumerable<IEnumerable<Atom?>> cells,
        MatrixCellAlignment matrixCellAlignment,
        double verticalPadding = DefaultPadding,
        double horizontalPadding = DefaultPadding,
        ArrayColumnSpec? columnSpec = null,
        IReadOnlyCollection<int>? horizontalRules = null,
        bool suppressOuterPadding = false,
        double rowStrutHeight = 0,
        double rowStrutDepth = 0) : base(source)
    {
        MatrixCells = ToImmutableCollection(cells.Select(ToImmutableCollection));
        MatrixCellAlignment = matrixCellAlignment;
        VerticalPadding = verticalPadding;
        HorizontalPadding = horizontalPadding;
        ColumnSpec = columnSpec;
        HorizontalRules = horizontalRules;
        SuppressOuterPadding = suppressOuterPadding;
        RowStrutHeight = rowStrutHeight;
        RowStrutDepth = rowStrutDepth;
    }

    /// <summary>Per-column alignment and vertical rules, for an <c>array</c>; null for everything else.</summary>
    public ArrayColumnSpec? ColumnSpec { get; }

    /// <summary>Row boundaries carrying an <c>\hline</c>, numbered from 0 (above the first row).</summary>
    public IReadOnlyCollection<int>? HorizontalRules { get; }

    public IReadOnlyCollection<IReadOnlyCollection<Atom?>> MatrixCells { get; }

    public double VerticalPadding { get; }

    public double HorizontalPadding { get; }

    public MatrixCellAlignment MatrixCellAlignment { get; }

    /// <summary>
    /// Whether the outer half-gaps go. A matrix has none - amsmath sets one with
    /// <c>\hskip -rraycolsep</c> at each end - while an array keeps them, which is the gap you see
    /// inside the brackets of <c>\left[egin{array}...</c>.
    /// </summary>
    public bool SuppressOuterPadding { get; }

    /// <summary>The least each row measures above its baseline. See <see cref="DefaultRowStrutHeight"/>.</summary>
    public double RowStrutHeight { get; }

    /// <summary>And below it.</summary>
    public double RowStrutDepth { get; }


    protected override Box CreateBoxCore(TexEnvironment environment)
    {
        Box CreateCell(Atom? atom) => atom is null ? StrutBox.Empty : atom.CreateBox(environment);

        var atomRows = MatrixCells.Select(row => row.ToArray()).ToArray();
        var columnCount = atomRows.Length == 0 ? 0 : atomRows.Max(row => row.Length);

        // A cell that spans several columns is drawn to their combined width, so it cannot be one of
        // the cells deciding that width. It is left out of the measuring pass and built afterwards,
        // once there is a width to hand it.
        var cells = atomRows
            .Select(row => row.Select(atom => atom is ISpanningMatrixCell ? null : CreateCell(atom)).ToArray())
            .ToArray();

        var columnWidths = new double[columnCount];
        foreach (var row in cells)
            for (var j = 0; j < row.Length; j++)
                if (row[j] is { } box)
                    columnWidths[j] = Math.Max(columnWidths[j], box.TotalWidth);

        var columnEdges = new List<double>();
        var rowHeights = new List<double>();
        var rowsContainer = new VerticalBox();

        for (var r = 0; r < cells.Length; r++)
        {
            var laidOut = LayOutRow(environment, atomRows[r], cells[r], columnWidths, columnCount);

            // Align cells on a common baseline within the row (LaTeX behaviour): the row is made
            // tall enough for the largest ascent and deepest descent it contains, but every cell
            // sits on the same baseline rather than being vertically centred (which would raise
            // short glyphs like "a" above taller ones like "b").
            // The strut is leading between rows, so it applies where there is a neighbour to be kept
            // away from. Above the first row and below the last there is nothing, and adding it there
            // would only inflate the box - and with it the delimiters drawn around the whole thing.
            var naturalAscent = laidOut.Count > 0 ? laidOut.Max(cell => cell.Box.Height) : 0.0;
            var naturalDescent = laidOut.Count > 0 ? laidOut.Max(cell => cell.Box.Depth) : 0.0;
            var rowAscent = r == 0 ? naturalAscent : Math.Max(naturalAscent + LineSkip, this.RowStrutHeight);
            var rowDescent = r == cells.Length - 1
                ? naturalDescent
                : Math.Max(naturalDescent, this.RowStrutDepth);
            var halfVPadding = VerticalPadding / 2;

            // Column edges - where a vertical rule goes - only make sense from a row that has one
            // cell per column, so a row carrying a spanning cell is not asked for them.
            var edgesFromThisRow = columnEdges.Count == 0 && laidOut.Count == columnCount;
            var columnEdgeX = 0.0;

            var rowContainer = new HorizontalBox();
            foreach (var (cell, lGap, rGap) in laidOut)
            {
                var topGap = rowAscent - cell.Height + halfVPadding;
                var bottomGap = rowDescent - cell.Depth + halfVPadding;
                var cellContainer = new VerticalBox();
                cellContainer.Add(new StrutBox(0.0, topGap, 0.0, 0.0));
                cellContainer.Add(cell);
                cellContainer.Add(new StrutBox(0.0, bottomGap, 0.0, 0.0));
                cellContainer.Height = cellContainer.TotalHeight;
                cellContainer.Depth = 0;

                rowContainer.Add(new StrutBox(lGap, 0.0, 0.0, 0.0));
                rowContainer.Add(cellContainer);
                rowContainer.Add(new StrutBox(rGap, 0.0, 0.0, 0.0));

                if (edgesFromThisRow)
                    columnEdges.Add(columnEdgeX);
                columnEdgeX += lGap + cell.TotalWidth + rGap;
            }

            rowHeights.Add(rowContainer.TotalHeight);
            rowsContainer.Add(rowContainer);
        }

        var axis = environment.MathFont.GetAxisHeight(environment.Style);
        var containerHeight = rowsContainer.TotalHeight;
        rowsContainer.Depth = containerHeight / 2 - axis;
        rowsContainer.Height = containerHeight / 2 + axis;

        var rules = CreateRulesBox(environment, rowsContainer, columnEdges, rowsContainer.Width, rowHeights);
        if (rules == null)
            return rowsContainer;

        var layered = new LayeredBox();
        layered.Add(rowsContainer);
        layered.Add(rules);
        layered.Height = rowsContainer.Height;
        layered.Depth = rowsContainer.Depth;
        layered.Width = rowsContainer.Width;
        return layered;
    }

    /// <summary>Places one row's boxes into columns, giving a spanning cell the width of all it covers.</summary>
    private List<PlacedCell> LayOutRow(
        TexEnvironment environment,
        IReadOnlyList<Atom?> atoms,
        IReadOnlyList<Box?> boxes,
        IReadOnlyList<double> columnWidths,
        int columnCount)
    {
        var laidOut = new List<PlacedCell>();
        var column = 0;
        while (column < columnCount)
        {
            if (column < atoms.Count && atoms[column] is ISpanningMatrixCell spanning)
            {
                var span = Math.Max(1, Math.Min(spanning.ColumnSpan, columnCount - column));
                var width = 0.0;
                for (var covered = column; covered < column + span; covered++)
                {
                    var (left, right) = GetLeftRightGap(0.0, covered);
                    width += left + columnWidths[covered] + right;
                }

                // The cell absorbs the columns' gaps into its own width, so the outer ones have to be
                // taken off here too - otherwise a spanning row comes out wider than the rows that
                // decided the columns, and the matrix grows to fit it.
                var (spanLeft, spanRight) = OuterAdjustment(column, column + span - 1, columnCount);
                laidOut.Add(new PlacedCell(
                    spanning.CreateSpanningBox(environment, width + spanLeft + spanRight), 0.0, 0.0));
                column += span;
            }
            else
            {
                var box = column < boxes.Count ? boxes[column] ?? StrutBox.Empty : StrutBox.Empty;
                var (left, right) = GetLeftRightGap(columnWidths[column] - box.TotalWidth, column);
                var (outerLeft, outerRight) = OuterAdjustment(column, column, columnCount);
                laidOut.Add(new PlacedCell(box, left + outerLeft, right + outerRight));
                column++;
            }
        }

        return laidOut;
    }

    /// <summary>
    /// What to take off the gaps at the two ends of a cell covering columns
    /// <paramref name="first"/> to <paramref name="last"/>, when the outer half-gaps are suppressed.
    /// </summary>
    private SurroundingGap OuterAdjustment(int first, int last, int columnCount)
    {
        if (!this.SuppressOuterPadding)
            return new SurroundingGap(0, 0);

        var half = this.HorizontalPadding / 2;
        return new SurroundingGap(first == 0 ? -half : 0, last == columnCount - 1 ? -half : 0);
    }

    private Box? CreateRulesBox(
        TexEnvironment environment,
        Box grid,
        IReadOnlyList<double> columnEdges,
        double totalWidth,
        IReadOnlyList<double> rowHeights)
    {
        var wantsVertical = ColumnSpec?.VerticalRules.Count > 0;
        var wantsHorizontal = HorizontalRules?.Count > 0;
        if (!wantsVertical && !wantsHorizontal)
            return null;

        var thickness = environment.MathFont.GetDefaultLineThickness(environment.Style);

        var verticalAt = new List<double>();
        if (ColumnSpec != null)
        {
            foreach (var boundary in ColumnSpec.VerticalRules)
            {
                // A boundary past the last column is the right edge; the rule is drawn inside it.
                var x = boundary < columnEdges.Count ? columnEdges[boundary] : totalWidth - thickness;
                verticalAt.Add(x);
            }
        }

        var horizontalAt = new List<double>();
        if (HorizontalRules != null)
        {
            foreach (var boundary in HorizontalRules)
            {
                var y = 0.0;
                for (var i = 0; i < boundary && i < rowHeights.Count; i++)
                    y += rowHeights[i];
                horizontalAt.Add(boundary >= rowHeights.Count ? y - thickness : y);
            }
        }

        return new GridRulesBox(environment, verticalAt, horizontalAt, thickness)
        {
            Width = totalWidth,
            Height = grid.Height,
            Depth = grid.Depth,
        };
    }

    private SurroundingGap GetLeftRightGap(double hFreeSpace, int columnIndex)
    {
        var lrPadding = HorizontalPadding / 2;

        if (ColumnSpec != null)
        {
            return ColumnSpec.AlignmentOf(columnIndex) switch
            {
                TexAlignment.Left => new SurroundingGap(lrPadding, lrPadding + hFreeSpace),
                TexAlignment.Right => new SurroundingGap(lrPadding + hFreeSpace, lrPadding),
                _ => new SurroundingGap(lrPadding + hFreeSpace / 2, lrPadding + hFreeSpace / 2),
            };
        }

        return MatrixCellAlignment switch
        {
            MatrixCellAlignment.Aligned => (columnIndex % 2) switch
            {
                0 when columnIndex != 0 => new SurroundingGap(AlignGroupLeftPadding + lrPadding + hFreeSpace, lrPadding),
                0 => new SurroundingGap(lrPadding + hFreeSpace, lrPadding),
                1 => new SurroundingGap(lrPadding, lrPadding + hFreeSpace),
                _ => throw new ArgumentOutOfRangeException()
            },
            MatrixCellAlignment.Left => new SurroundingGap(lrPadding, lrPadding + hFreeSpace),
            MatrixCellAlignment.Center => new SurroundingGap(lrPadding + hFreeSpace / 2, lrPadding + hFreeSpace / 2),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    /// <summary>One cell as it sits in a row: its box, and the space either side of it.</summary>
    private readonly record struct PlacedCell(Box Box, double LeftGap, double RightGap);

    private static IReadOnlyCollection<T> ToImmutableCollection<T>(IEnumerable<T> s) => s.ToList().AsReadOnly();
}

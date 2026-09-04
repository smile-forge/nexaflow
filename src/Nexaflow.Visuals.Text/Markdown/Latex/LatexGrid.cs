using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// A matrix as the parse tree holds it — every cell, the row and column it is in, and the source it was
/// written from — together with the structural edits that only make sense on a grid.
///
/// <para>
/// Read from the tree, not from the text. The parser knows a matrix is a table; it now says so, with each
/// cell carrying its place (<c>FormulaSlot.Row</c>/<c>Column</c>) and an empty cell standing as a
/// placeholder rather than as nothing at all. Everything here follows from that, which is why adding
/// "insert a row" or "delete this column" later is a method on this class rather than another reading of
/// the source.
/// </para>
/// <para>
/// The edits produce source, because source is what an edit to a formula <em>is</em> — the tree is a
/// reading of it, so a changed tree is changed text, re-read. What is never done here is inferring the
/// structure back out of that text: the separators are found by asking the tree where the cells are, not
/// by scanning for <c>&amp;</c>.
/// </para>
/// </summary>
public sealed class LatexGrid
{
    private readonly List<List<Cell>> _cells;

    private LatexGrid(string latex, int start, int length, int bodyStart, int bodyEnd, List<List<Cell>> cells)
    {
        Latex = latex;
        Start = start;
        Length = length;
        BodyStart = bodyStart;
        BodyEnd = bodyEnd;
        _cells = cells;
    }

    /// <summary>One cell: where its contents sit in the formula, and what they say.</summary>
    private readonly record struct Cell(int Start, int Length, string Text);

    /// <summary>The formula this grid was read from.</summary>
    public string Latex { get; }

    /// <summary>Where the whole matrix sits in the formula, delimiters and all.</summary>
    public int Start { get; }

    /// <summary>How much of the formula the matrix occupies.</summary>
    public int Length { get; }

    /// <summary>
    /// The matrix as a stretch of source, backslash included — what moving the whole of it moves.
    /// <para>
    /// The span as read, with nothing added back. It used to reach one character further left, because
    /// the typesetter's span for a command begins at its <em>name</em> and moving from that would have
    /// carried <c>begin{pmatrix}…</c> away and left a lone backslash behind. The parse tree names the
    /// whole of what was written, so reaching outside it now would take a character belonging to
    /// whatever the matrix was written after.
    /// </para>
    /// </summary>
    public (int Start, int Length) Span => (Start, Length);

    /// <summary>
    /// The stretch of source the cells occupy, first cell to last - what a rewrite replaces.
    /// <para>
    /// Carried from the reading rather than worked out from the cells in hand, because a move may have
    /// taken some of them away: asking the cells that are left where they start gives the extent of
    /// what survived, and splicing over that leaves the removed ones sitting in the source.
    /// </para>
    /// </summary>
    public int BodyStart { get; }

    /// <inheritdoc cref="BodyStart"/>
    public int BodyEnd { get; }

    /// <summary>How many rows.</summary>
    public int RowCount => _cells.Count;

    /// <summary>How many columns. Every row has the same number — the parser squares a ragged matrix off.</summary>
    public int ColumnCount => _cells.Count == 0 ? 0 : _cells[0].Count;

    /// <summary>What is written in one cell; empty for a cell holding a placeholder.</summary>
    public string CellText(int row, int column) => _cells[row][column].Text;

    /// <summary>
    /// Builds a grid from what the tree says: the matrix's own span, and each cell's place and span.
    /// Null when the cells do not form a rectangle, which the parser should now make impossible and which
    /// is still not worth guessing at.
    /// </summary>
    internal static LatexGrid? From(
        string latex, int start, int length,
        IReadOnlyList<(int Row, int Column, int Start, int Length)> cells)
    {
        if (string.IsNullOrEmpty(latex) || cells.Count == 0) return null;

        var rows = cells.Max(c => c.Row) + 1;
        var columns = cells.Max(c => c.Column) + 1;
        if (cells.Count != rows * columns) return null;

        var grid = new List<List<Cell>>();
        for (var row = 0; row < rows; row++) grid.Add([.. Enumerable.Repeat(default(Cell), columns)]);

        foreach (var (row, column, cellStart, cellLength) in cells)
        {
            if (cellStart < 0 || cellStart + cellLength > latex.Length) return null;
            grid[row][column] = new Cell(cellStart, cellLength, latex.Substring(cellStart, cellLength).Trim());
        }

        var flat = cells.Select(c => (c.Start, End: c.Start + c.Length)).ToList();
        return new LatexGrid(latex, start, length, flat.Min(c => c.Start), flat.Max(c => c.End), grid);
    }

    /// <summary>Which cell holds <paramref name="offset"/>, or null when it is not in one.</summary>
    public (int Row, int Column)? CellAt(int offset)
    {
        for (var row = 0; row < RowCount; row++)
            for (var column = 0; column < ColumnCount; column++)
            {
                var cell = _cells[row][column];
                if (offset >= cell.Start && offset <= cell.Start + cell.Length) return (row, column);
            }

        return null;
    }

    /// <summary>
    /// The smallest block of cells covering every one of <paramref name="ranges"/>, or null when any of
    /// them falls outside this grid — a selection that is partly elsewhere is not a block of cells.
    /// </summary>
    public GridBlock? BlockOf(IReadOnlyList<(int Start, int Length)> ranges)
    {
        if (ranges is null || ranges.Count == 0) return null;

        int top = int.MaxValue, left = int.MaxValue, bottom = -1, right = -1;
        foreach (var range in ranges)
        {
            // Both ends, because one range covers a run of cells — a selected row comes back as a single
            // stretch from its first cell to its last, separators included.
            if (CellAt(range.Start) is not { } from) return null;
            if (CellAt(range.Start + range.Length) is not { } to) return null;

            top = Math.Min(top, Math.Min(from.Row, to.Row));
            bottom = Math.Max(bottom, Math.Max(from.Row, to.Row));
            left = Math.Min(left, Math.Min(from.Column, to.Column));
            right = Math.Max(right, Math.Max(from.Column, to.Column));
        }

        return bottom < 0 ? null : new GridBlock(top, left, bottom, right);
    }

    /// <summary>Whether <paramref name="block"/> is every row of some columns — a column selection.</summary>
    public bool IsWholeColumns(GridBlock block) =>
        block.Top == 0 && block.Bottom == RowCount - 1 && block.Columns < ColumnCount;

    /// <summary>Whether <paramref name="block"/> is every column of some rows — a row selection.</summary>
    public bool IsWholeRows(GridBlock block) =>
        block.Left == 0 && block.Right == ColumnCount - 1 && block.Rows < RowCount;

    // ── The moves ───────────────────────────────────────────────────────────

    /// <summary>
    /// The columns of <paramref name="block"/> taken out and put back before column
    /// <paramref name="before"/>, everything else closing up and shifting over.
    /// </summary>
    public GridMove WithColumnsMoved(GridBlock block, int before)
    {
        var order = Reordered(ColumnCount, block.Left, block.Right, before);
        var landed = order.IndexOf(block.Left);
        return new GridMove(
            Rebuilt(_cells.Select(row => order.Select(c => row[c]).ToList()).ToList()),
            new GridBlock(0, landed, RowCount - 1, landed + block.Columns - 1));
    }

    /// <summary>The rows of <paramref name="block"/> moved to sit before row <paramref name="before"/>.</summary>
    public GridMove WithRowsMoved(GridBlock block, int before)
    {
        var order = Reordered(RowCount, block.Top, block.Bottom, before);
        var landed = order.IndexOf(block.Top);
        return new GridMove(
            Rebuilt(order.Select(r => _cells[r]).ToList()),
            new GridBlock(landed, 0, landed + block.Rows - 1, ColumnCount - 1));
    }

    /// <summary>
    /// The block's contents written starting at <paramref name="toRow"/>/<paramref name="toColumn"/>, the
    /// cells it came from left empty — a block dragged about inside the matrix, as it would move on a
    /// sheet. The matrix keeps its shape, so a block dropped near an edge slides back to fit rather than
    /// growing the grid or losing what runs off it.
    /// </summary>
    public GridMove WithBlockMoved(GridBlock block, int toRow, int toColumn)
    {
        toRow = Math.Clamp(toRow, 0, Math.Max(0, RowCount - block.Rows));
        toColumn = Math.Clamp(toColumn, 0, Math.Max(0, ColumnCount - block.Columns));

        var moved = Copy();

        // Emptied first, then written, so a block overlapping where it lands keeps its contents rather
        // than blanking cells it has just filled.
        Clear(moved, block);
        for (var row = block.Top; row <= block.Bottom; row++)
            for (var column = block.Left; column <= block.Right; column++)
                moved[toRow + row - block.Top][toColumn + column - block.Left] =
                    moved[toRow + row - block.Top][toColumn + column - block.Left]
                        with { Text = _cells[row][column].Text };

        return new GridMove(
            Rebuilt(moved),
            new GridBlock(toRow, toColumn, toRow + block.Rows - 1, toColumn + block.Columns - 1));
    }

    /// <summary>
    /// The block's cells emptied, and any of its columns or rows that emptying leaves entirely blank
    /// taken out — what a block dragged out of the matrix leaves behind.
    /// <para>
    /// One rule rather than two. A cell that has been vacated is a hole to write in, and a whole column
    /// of holes is not: it is a column the matrix no longer has, and keeping it would mean dragging a
    /// column out and watching the matrix stay exactly as wide with a blank stripe down it. Whether the
    /// selection happened to be a whole column never has to be asked — the answer falls out of what is
    /// left.
    /// </para>
    /// <para>
    /// Only the block's own columns and rows are considered. A column that was already empty before the
    /// move is the writer's, and taking it away because something else moved would be an edit nobody
    /// asked for.
    /// </para>
    /// </summary>
    public LatexGrid WithBlockTaken(GridBlock block)
    {
        var cells = Copy();
        Clear(cells, block);

        var columns = Enumerable.Range(0, ColumnCount)
            .Where(c => !(c >= block.Left && c <= block.Right && cells.All(row => row[c].Text.Length == 0)))
            .ToList();
        var rows = Enumerable.Range(0, RowCount)
            .Where(r => !(r >= block.Top && r <= block.Bottom && cells[r].All(cell => cell.Text.Length == 0)))
            .ToList();

        // Never nothing. A matrix emptied entirely is still a matrix until the reader deletes it, and a
        // grid of no cells is not something the rest of this can answer questions about.
        if (columns.Count == 0) columns = [block.Left];
        if (rows.Count == 0) rows = [block.Top];

        return Rebuilt(rows.Select(r => columns.Select(c => cells[r][c]).ToList()).ToList());
    }

    /// <summary>
    /// The block on its own as a matrix of the same kind — what a selection dragged out of the matrix
    /// becomes. A single cell comes back as its contents instead: a matrix around one term is punctuation
    /// nobody asked for.
    /// </summary>
    public string Extracted(GridBlock block)
    {
        var rows = Enumerable.Range(block.Top, block.Rows)
            .Select(row => Written(Enumerable.Range(block.Left, block.Columns).Select(c => _cells[row][c])))
            .ToList();

        if (rows.Count == 1 && block.Columns == 1) return rows[0];
        return $@"\begin{{{Environment}}} {string.Join(@" \\ ", rows)} \end{{{Environment}}}";
    }

    /// <summary>
    /// The environment this matrix was written as — <c>pmatrix</c>, <c>bmatrix</c> — read off the source
    /// at the matrix's own start, which the tree gave us. Plain <c>matrix</c> when it was written some
    /// other way, so a block dragged out of one is still a matrix.
    /// </summary>
    public string Environment
    {
        get
        {
            // Either side of the backslash: a command's parse span begins at its *name*, and the layout's
            // at the backslash that introduced it, so which one this grid was built from decides.
            foreach (var at in new[] { Start, Start - 1 })
            {
                const string opens = @"\begin{";
                if (at < 0 || at + opens.Length >= Latex.Length) continue;
                if (string.CompareOrdinal(Latex, at, opens, 0, opens.Length) != 0) continue;

                var close = Latex.IndexOf('}', at + opens.Length);
                if (close > 0) return Latex[(at + opens.Length)..close];
            }

            return "matrix";
        }
    }

    /// <summary>
    /// This grid's formula with the matrix's cells rewritten, and where the rewritten cells now sit.
    /// <para>
    /// Only the stretch the cells occupy is replaced — from the first one's start to the last one's end —
    /// so whatever wrote the matrix (<c>\begin{pmatrix}</c>, or a command form) is left exactly as it was.
    /// </para>
    /// </summary>
    /// <returns>
    /// The rewritten formula, and this grid over it — cells carrying where they have just been put.
    /// Handing the grid back is what lets a caller ask where a moved block ended up without parsing the
    /// text it has this moment written: the answer is structural, and re-reading the source to get it
    /// would be going out through the typesetter and back for something already known.
    /// </returns>
    public (string Latex, LatexGrid Grid) Render()
    {
        var (body, placed) = Lay();
        var latex = Latex[..BodyStart] + body + Latex[BodyEnd..];

        return (latex, new LatexGrid(
            latex, Start, Length + body.Length - (BodyEnd - BodyStart),
            BodyStart, BodyStart + body.Length, placed));
    }

    /// <summary>
    /// The cells written out as a matrix body, without splicing them anywhere. What a caller needs when
    /// it is rewriting <em>two</em> matrices in one formula — a block leaving one and joining another —
    /// and has to put both back itself, later one first, so the earlier one's offsets still hold.
    /// </summary>
    public string Body() => Lay().Body;

    /// <summary>Writes the cells out, recording where each one lands in what is written.</summary>
    private (string Body, List<List<Cell>> Placed) Lay()
    {
        var body = new System.Text.StringBuilder();
        var placed = new List<List<Cell>>();

        for (var row = 0; row < RowCount; row++)
        {
            if (row > 0) body.Append(@" \\ ");
            placed.Add([]);

            for (var column = 0; column < ColumnCount; column++)
            {
                if (column > 0) body.Append(" & ");

                var text = Written(_cells[row][column].Text);
                placed[row].Add(new Cell(BodyStart + body.Length, text.Length, _cells[row][column].Text));
                body.Append(text);
            }
        }

        return (body.ToString(), placed);
    }

    /// <summary>What is written in a block of cells, row by row.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Contents(GridBlock block) =>
        [.. Enumerable.Range(block.Top, block.Rows)
            .Select(row => (IReadOnlyList<string>)
                [.. Enumerable.Range(block.Left, block.Columns).Select(c => _cells[row][c].Text)])];

    /// <summary>
    /// <paramref name="columns"/> put in as new columns at <paramref name="at"/>, the matrix widening and
    /// everything from there rightwards shifting over — a block dropped between two columns, or off
    /// either end of them, rather than onto a cell.
    /// <para>
    /// Squared off to the matrix it is joining: a block with fewer rows leaves holes under it, and one
    /// with more is clipped. A matrix has one number of rows, and a merge cannot make it ragged.
    /// </para>
    /// </summary>
    public GridMove WithColumnsInserted(int at, IReadOnlyList<IReadOnlyList<string>> columns)
    {
        at = Math.Clamp(at, 0, ColumnCount);
        var width = columns.Count == 0 ? 0 : columns.Max(r => r.Count);
        if (width == 0) return new GridMove(this, new GridBlock(0, at, RowCount - 1, at));

        var cells = new List<List<Cell>>();
        for (var row = 0; row < RowCount; row++)
        {
            var incoming = Enumerable.Range(0, width)
                .Select(c => new Cell(0, 0, row < columns.Count && c < columns[row].Count ? columns[row][c] : string.Empty));

            cells.Add([.. _cells[row][..at], .. incoming, .. _cells[row][at..]]);
        }

        return new GridMove(Rebuilt(cells), new GridBlock(0, at, RowCount - 1, at + width - 1));
    }

    /// <summary><paramref name="rows"/> put in as new rows at <paramref name="at"/>, the matrix growing downwards.</summary>
    public GridMove WithRowsInserted(int at, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        at = Math.Clamp(at, 0, RowCount);
        if (rows.Count == 0) return new GridMove(this, new GridBlock(at, 0, at, ColumnCount - 1));

        var incoming = rows.Select(row => Enumerable.Range(0, ColumnCount)
            .Select(c => new Cell(0, 0, c < row.Count ? row[c] : string.Empty))
            .ToList());

        return new GridMove(
            Rebuilt([.. _cells[..at], .. incoming, .. _cells[at..]]),
            new GridBlock(at, 0, at + rows.Count - 1, ColumnCount - 1));
    }

    /// <summary>
    /// Where <paramref name="block"/> sits in the source — from the start of its first cell to the end of
    /// its last. Meaningful on a grid read from the tree, not on one a move has rearranged.
    /// </summary>
    public (int Start, int Length)? SpanOf(GridBlock block)
    {
        if (block.Top < 0 || block.Bottom >= RowCount || block.Left < 0 || block.Right >= ColumnCount)
            return null;

        var first = _cells[block.Top][block.Left];
        var last = _cells[block.Bottom][block.Right];
        return (first.Start, Math.Max(0, last.Start + last.Length - first.Start));
    }

    // ── Mechanics ───────────────────────────────────────────────────────────

    private static string Written(IEnumerable<Cell> row) => string.Join(" & ", row.Select(c => Written(c.Text)));

    /// <summary>
    /// A cell as it goes back into the source. An empty one is written as <c>{}</c> rather than as
    /// nothing: it is a hole the reader is meant to fill, and the braces are what make the parser build a
    /// placeholder there for them to aim at. Written as nothing, a cell emptied by a move would be
    /// invisible and unclickable — the matrix would appear to have lost a column.
    /// </summary>
    private static string Written(string text) => text.Length == 0 ? "{}" : text;

    private List<List<Cell>> Copy() => _cells.Select(row => row.ToList()).ToList();

    private static void Clear(List<List<Cell>> cells, GridBlock block)
    {
        for (var row = block.Top; row <= block.Bottom; row++)
            for (var column = block.Left; column <= block.Right; column++)
                cells[row][column] = cells[row][column] with { Text = string.Empty };
    }

    /// <summary>
    /// The same grid holding <paramref name="cells"/>. The spans stop meaning anything the moment the
    /// cells are rearranged, so what comes out of a move is re-read from the source it produced rather
    /// than carried forward — but the matrix's own extent is still where it was.
    /// </summary>
    private LatexGrid Rebuilt(List<List<Cell>> cells) =>
        new(Latex, Start, Length, BodyStart, BodyEnd, cells);

    /// <summary>
    /// <paramref name="count"/> indices with <paramref name="from"/>..<paramref name="to"/> lifted out and
    /// put back before <paramref name="before"/> — the one piece of arithmetic a reorder needs, and the
    /// one that is easy to get wrong when the destination lies after the stretch being moved.
    /// </summary>
    private static List<int> Reordered(int count, int from, int to, int before)
    {
        var moving = Enumerable.Range(from, to - from + 1).ToList();
        var rest = Enumerable.Range(0, count).Where(i => i < from || i > to).ToList();

        // `before` names a place in the original numbering; in what is left, everything the move took out
        // from in front of it has gone.
        var at = rest.Count(i => i < before);
        rest.InsertRange(Math.Clamp(at, 0, rest.Count), moving);
        return rest;
    }

    /// <summary>
    /// The order this table's columns would be in with <paramref name="block"/> moved before
    /// <paramref name="before"/>, and where the block lands — decided here, done to the tree.
    /// </summary>
    public (IReadOnlyList<int> Order, GridBlock Landed) ColumnOrder(GridBlock block, int before)
    {
        var order = Reordered(ColumnCount, block.Left, block.Right, before);
        var landed = order.IndexOf(block.Left);

        return (order, new GridBlock(0, landed, RowCount - 1, landed + block.Columns - 1));
    }

    /// <summary>The same for rows.</summary>
    public (IReadOnlyList<int> Order, GridBlock Landed) RowOrder(GridBlock block, int before)
    {
        var order = Reordered(RowCount, block.Top, block.Bottom, before);
        var landed = order.IndexOf(block.Top);

        return (order, new GridBlock(landed, 0, landed + block.Rows - 1, ColumnCount - 1));
    }
}

/// <summary>A grid after a move, and where the moved cells ended up in it.</summary>
public readonly record struct GridMove(LatexGrid Grid, GridBlock Landed);

/// <summary>
/// Where a pointer is in a matrix: on a cell, or at a place between columns or rows where something
/// dropped would join the matrix rather than land in it.
/// </summary>
/// <param name="Grid">The matrix under the pointer.</param>
/// <param name="Row">With <paramref name="Column"/>, the cell it is on.</param>
/// <param name="Column">With <paramref name="Row"/>, the cell it is on.</param>
/// <param name="InsertColumn">The column index a block dropped here would be put in at.</param>
/// <param name="InsertRow">The row index a block dropped here would be put in at.</param>
public readonly record struct GridDrop(
    LatexGrid Grid, int? Row, int? Column, int? InsertColumn, int? InsertRow)
{
    /// <summary>The cell the pointer is on, or null when it is at a boundary between them.</summary>
    public (int Row, int Column)? Cell => Row is { } r && Column is { } c ? (r, c) : null;
}

/// <summary>A rectangle of cells: the rows and columns a selection covers.</summary>
public readonly record struct GridBlock(int Top, int Left, int Bottom, int Right)
{
    /// <summary>How many rows it spans.</summary>
    public int Rows => Bottom - Top + 1;

    /// <summary>How many columns it spans.</summary>
    public int Columns => Right - Left + 1;
}

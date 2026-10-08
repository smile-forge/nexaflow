using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Latex;

/// <summary>A rectangle of a table, counted in rows and columns rather than in characters.</summary>
public readonly record struct TexBlock(int Top, int Left, int Bottom, int Right)
{
    public int Rows => this.Bottom - this.Top + 1;

    public int Columns => this.Right - this.Left + 1;

    /// <summary>Whether a cell is one of this block's.</summary>
    public bool Holds(int row, int column) =>
        row >= this.Top && row <= this.Bottom && column >= this.Left && column <= this.Right;
}

/// <summary>
/// Moving cells of a table: whole columns and whole rows reorder, a block of them moves and leaves its own cells
/// empty, and a block carried out of the table becomes a table of its own.
///
/// <para>
/// <strong>Nothing is reprinted.</strong> Every answer is a set of stretches to write, one per cell, each given the
/// characters of whichever cell now stands in its place. So a matrix somebody lined up by hand comes back lined up:
/// the spaces, the line breaks and the <c>&amp;</c> between the cells are never written, because none of that is what
/// moved. Regenerating the body instead is what reformatted every matrix it touched.
/// </para>
/// <para>
/// <strong>Squared-off cells are declined rather than guessed at.</strong> <see cref="TexGrid"/> squares a ragged
/// table so that "the third column" means the same in every row, and those extra cells stand where a cell written
/// there <em>would</em> begin — with no <c>&amp;</c> in front of them, because nobody wrote one. Writing into one
/// would make <c>a &amp; b c</c> of a two-cell row, so a move that would read from or write to one is not made at all.
/// </para>
/// </summary>
public static class TexMove
{
    /// <summary>
    /// What letting go of <paramref name="carried"/> at <paramref name="to"/> comes to in the table those stretches
    /// were carried out of — or null where this is not a move of whole cells, which is a move of characters and
    /// nobody's business here.
    /// </summary>
    /// <param name="root">The formula, positioned: every offset here is the document's own.</param>
    public static ContentChange? Dropped(ContentPart root, IReadOnlyList<EditRange> carried, int to)
    {
        if (carried.Count == 0) return null;
        if (TexGrid.At(root, carried[0].Start) is not { } grid) return null;
        if (Blocked(grid, carried) is not { } block) return null;

        // The whole table carried is nothing to move it into.
        if (block.Rows == grid.RowCount && block.Columns == grid.ColumnCount) return null;

        // Let go outside the table it came from: what was carried becomes a table of its own there.
        if (to < grid.Start || to > grid.End) return Lifted(grid, block, to);

        if (grid.CellAt(to) is not { } over) return null;

        // Let go on a cell of its own is not refused here: a block shifted one across lands its corner inside
        // itself, and each case below says for itself when a move came to nothing.
        if (block.Columns == grid.ColumnCount) return Reordered(grid, block, over.Row, rows: true);
        if (block.Rows == grid.RowCount) return Reordered(grid, block, over.Column, rows: false);

        return Shifted(grid, block, over);
    }

    /// <summary>
    /// The rectangle of <paramref name="grid"/> that <paramref name="carried"/> covers — or null where the stretches
    /// are not whole cells of it, or are cells that make no rectangle.
    ///
    /// <para>
    /// A rectangle because that is what a move of cells can mean: an L of three cells has no shape to land in. The
    /// count settles it without any geometry — a rectangle of so many rows and columns holds exactly that many cells,
    /// so anything else was not one.
    /// </para>
    /// </summary>
    private static TexBlock? Blocked(TexGrid grid, IReadOnlyList<EditRange> carried)
    {
        var cells = new List<TexCell>();

        foreach (var range in carried)
        {
            var inside = grid.Cells
                .Where(cell => cell.Node is not null && range.Start <= cell.Start && cell.End <= range.End)
                .ToList();

            if (inside.Count == 0) return null;
            cells.AddRange(inside);
        }

        var block = new TexBlock(cells.Min(cell => cell.Row), cells.Min(cell => cell.Column),
                                 cells.Max(cell => cell.Row), cells.Max(cell => cell.Column));

        return cells.Count == block.Rows * block.Columns ? block : null;
    }

    /// <summary>
    /// Whole rows, or whole columns, put somewhere else in the order: the ones that were not carried close up, and the
    /// carried ones go in where they were let go — after what they were let go on where that is past them, in front of
    /// it where it is not, which is what carrying a column rightwards and leftwards each mean.
    /// </summary>
    /// <param name="rows">Whether rows are being reordered; columns otherwise.</param>
    private static ContentChange? Reordered(TexGrid grid, TexBlock block, int onto, bool rows)
    {
        var (first, last, count) = rows
            ? (block.Top, block.Bottom, grid.RowCount)
            : (block.Left, block.Right, grid.ColumnCount);

        var order = Enumerable.Range(0, count).Where(at => at < first || at > last).ToList();
        var into = order.IndexOf(onto);
        if (into < 0) return null;

        order.InsertRange(onto > last ? into + 1 : into, Enumerable.Range(first, last - first + 1));

        return Written(grid, (row, column) => rows ? (order[row], column) : (row, order[column]));
    }

    /// <summary>
    /// A block of cells moved so its corner lands on <paramref name="over"/>: every cell it lands on takes the
    /// contents of the cell it came from, and every cell it left is emptied unless the block landed back on it.
    /// </summary>
    private static ContentChange? Shifted(TexGrid grid, TexBlock block, TexCell over)
    {
        var down = over.Row - block.Top;
        var across = over.Column - block.Left;

        // Let go where it already is.
        if (down == 0 && across == 0) return null;

        // Off the edge of the table. Growing it to fit would be writing rows and columns nobody asked for, so the move
        // is simply not made.
        if (block.Bottom + down >= grid.RowCount || block.Right + across >= grid.ColumnCount) return null;

        var landed = new TexBlock(block.Top + down, block.Left + across, block.Bottom + down, block.Right + across);

        return Written(grid, (row, column) =>
            landed.Holds(row, column) ? (row - down, column - across)
            : block.Holds(row, column) ? ((int Row, int Column)?)null
            : (row, column));
    }

    /// <summary>
    /// A block carried out of its table and let go outside it: the cells it came from are emptied, and a table of its
    /// own — the same kind, at the size carried — is written where it was let go.
    /// </summary>
    private static ContentChange? Lifted(TexGrid grid, TexBlock block, int to)
    {
        var writes = new List<ContentWrite>();

        foreach (var cell in Across(grid, block))
        {
            if (cell.Node is null) return null;
            if (cell.Length > 0) writes.Add(new ContentWrite(cell.Start, cell.Length, string.Empty));
        }

        var said = string.Join(@" \\ ", Enumerable.Range(block.Top, block.Rows).Select(row =>
            string.Join(" & ", Enumerable.Range(block.Left, block.Columns).Select(column => Says(grid[row, column])))));

        writes.Add(new ContentWrite(to, 0, $@"\begin{{{grid.Name}}} {said} \end{{{grid.Name}}}"));

        return new ContentChange(writes, to);
    }

    /// <summary>Every cell of a block, row by row.</summary>
    private static IEnumerable<TexCell> Across(TexGrid grid, TexBlock block)
    {
        for (var row = block.Top; row <= block.Bottom; row++)
            for (var column = block.Left; column <= block.Right; column++)
                yield return grid[row, column];
    }

    /// <summary>
    /// The table as the stretches that change: every cell given the characters of whichever cell <paramref name="from"/>
    /// says now stands in its place, or emptied where it says none does.
    ///
    /// <para>
    /// Null where any cell it would read from or write to is one nobody wrote — see the class summary. Null too where
    /// nothing changed, so a move that came to nothing pushes no undo step.
    /// </para>
    /// </summary>
    private static ContentChange? Written(TexGrid grid, Func<int, int, (int Row, int Column)?> from)
    {
        var writes = new List<ContentWrite>();

        for (var row = 0; row < grid.RowCount; row++)
            for (var column = 0; column < grid.ColumnCount; column++)
            {
                var into = grid[row, column];
                if (into.Node is null) return null;

                var said = string.Empty;

                if (from(row, column) is { } stood)
                {
                    if (stood == (row, column)) continue;

                    var cell = grid[stood.Row, stood.Column];
                    if (cell.Node is null) return null;

                    said = Says(cell);
                }

                if (said.Length == 0 && into.Length == 0) continue;

                writes.Add(new ContentWrite(into.Start, into.Length, said));
            }

        return writes.Count == 0 ? null : new ContentChange(writes, writes[0].Start + writes[0].Text.Length);
    }

    /// <summary>
    /// What is written in a cell: its own characters, without the space around them or the <c>&amp;</c> ending it.
    ///
    /// <para>
    /// Read off the node rather than cut out of the source, because the node is what the cell has — and what it holds
    /// is what <see cref="TexCell.Start"/> and <see cref="TexCell.Length"/> name, so the two cannot disagree.
    /// </para>
    /// </summary>
    private static string Says(TexCell cell)
    {
        if (cell.Node is null || cell.Length == 0) return string.Empty;

        var said = new StringBuilder();

        foreach (var child in cell.Node.Children)
        {
            if (child.Role == Roles.Separator) break;
            said.Append(child.Print());
        }

        return said.ToString().Trim();
    }
}

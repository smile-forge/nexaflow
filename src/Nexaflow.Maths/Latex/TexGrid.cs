namespace Nexaflow.Maths.Latex;

/// <summary>One cell of a grid: where it is in the table, and which characters are written in it.</summary>
/// <param name="Start">Where the contents begin, past any space after the separator.</param>
/// <param name="Length">How many characters they run to — zero for a cell with nothing in it.</param>
public readonly record struct TexCell(int Row, int Column, int Start, int Length, TexNode? Node = null)
{
    /// <summary>
    /// The node the cell was read from, or null for a cell nobody wrote - one squared off so that "the
    /// third column" means the same thing in every row. It is what lets a drawn piece be matched to the
    /// cell holding it by identity rather than by comparing offsets.
    /// </summary>

    /// <summary>One past the last character written in the cell.</summary>
    public int End => this.Start + this.Length;

    /// <summary>Whether nothing is written here.</summary>
    public bool IsEmpty => this.Length == 0;
}

/// <summary>
/// A matrix as a table: how many rows and columns, and where every cell is.
///
/// <para>
/// Read off the parse tree, which knows a matrix is a table and says so — the rows and the cells are
/// nodes, and the <c>&amp;</c> and <c>\\</c> between them are nodes too, sitting where they were written.
/// Nothing here counts separators or clusters rectangles to work the shape out again.
/// </para>
/// <para>
/// <strong>Square, even when what was written is not.</strong> A row written with two cells beside rows
/// of four gets two more, each empty and each standing where a cell written there would begin. Every
/// question a table edit asks — which column is this, what is above it, where would a new one go — is
/// meaningless on a ragged grid, and the alternative is for every caller to handle the ragged case and
/// for most of them to forget.
/// </para>
/// </summary>
public sealed class TexGrid
{
    private readonly TexCell[,] _cells;

    private TexGrid(TexNode environment, string name, int start, TexCell[,] cells)
    {
        this.Environment = environment;
        this.Name = name;
        this.Start = start;
        this._cells = cells;
        this.RowCount = cells.GetLength(0);
        this.ColumnCount = cells.GetLength(1);
    }

    /// <summary>The environment node this was read from.</summary>
    public TexNode Environment { get; }

    /// <summary>What it was begun as: <c>matrix</c>, <c>pmatrix</c>, <c>cases</c>, <c>array</c>.</summary>
    public string Name { get; }

    /// <summary>Where the whole of it begins — at the backslash of its <c>\begin</c>.</summary>
    public int Start { get; }

    /// <summary>How much of the formula it occupies, <c>\end</c> included.</summary>
    public int Length => this.Environment.Width;

    /// <summary>One past its last character.</summary>
    public int End => this.Start + this.Length;

    public int RowCount { get; }

    public int ColumnCount { get; }

    /// <summary>
    /// Whether the shape of this table is written down somewhere else as well — the <c>{cc}</c> of an
    /// <c>array</c>. Moving a column of one of these means moving a letter of that spec in step, so
    /// anything that reorders columns has to either do both or decline.
    /// </summary>
    public bool HasColumnSpec => this.Environment.Part(TexRole.Option) is not null;

    public TexCell this[int row, int column] => this._cells[row, column];

    /// <summary>Every cell, row by row.</summary>
    public IEnumerable<TexCell> Cells
    {
        get
        {
            for (var row = 0; row < this.RowCount; row++)
                for (var column = 0; column < this.ColumnCount; column++)
                    yield return this._cells[row, column];
        }
    }

    /// <summary>Which cell holds <paramref name="offset"/>, ends included, or null.</summary>
    public TexCell? CellAt(int offset)
    {
        foreach (var cell in this.Cells)
            if (offset >= cell.Start && offset <= cell.End) return cell;

        return null;
    }

    /// <summary>
    /// The table <paramref name="offset"/> is in, innermost first — so a matrix inside a matrix answers
    /// as the one being pointed into. Null when the offset is not in one.
    /// </summary>
    public static TexGrid? At(TexNode root, int offset)
    {
        TexGrid? innermost = null;

        foreach (var grid in In(root))
        {
            if (offset < grid.Start || offset > grid.End) continue;
            if (innermost is null || grid.Length < innermost.Length) innermost = grid;
        }

        return innermost;
    }

    /// <summary>Every table in this formula, outermost first.</summary>
    public static IEnumerable<TexGrid> In(TexNode root)
    {
        foreach (var place in root.Placed())
        {
            if (place.Node.Kind != TexKind.Environment) continue;
            if (Read(place.Node, place.Start) is { } grid) yield return grid;
        }
    }

    /// <summary>This environment as a table, or null if it holds no cells.</summary>
    public static TexGrid? Read(TexNode environment, int start)
    {
        if (environment.Kind != TexKind.Environment) return null;

        var name = environment.Part(TexRole.Begin) is { } begin ? TexParser.NameOf(begin) : string.Empty;

        var rows = new List<List<TexCell>>();
        var at = start;

        foreach (var child in environment.Children)
        {
            if (child.Role == TexRole.Row) rows.Add(Across(child, at, rows.Count));
            at += child.Width;
        }

        if (rows.Count == 0) return null;

        var columns = rows.Max(row => row.Count);
        if (columns == 0) return null;

        var cells = new TexCell[rows.Count, columns];

        for (var row = 0; row < rows.Count; row++)
        {
            // Where a cell written past the end of a short row would begin: after the last one that was.
            var beyond = rows[row].Count > 0 ? rows[row][^1].End : start;

            for (var column = 0; column < columns; column++)
                cells[row, column] = column < rows[row].Count
                    ? rows[row][column]
                    : new TexCell(row, column, beyond, 0);
        }

        return new TexGrid(environment, name, start, cells);
    }

    /// <summary>The cells of one row, in order, each named by where its contents actually are.</summary>
    private static List<TexCell> Across(TexNode row, int start, int index)
    {
        var cells = new List<TexCell>();
        var at = start;

        foreach (var child in row.Children)
        {
            if (child.Role == TexRole.Cell) cells.Add(Contents(child, at, index, cells.Count));
            at += child.Width;
        }

        return cells;
    }

    /// <summary>
    /// What is written in one cell, without the space around it or the <c>&amp;</c> that ends it.
    /// <para>
    /// A cell with nothing in it comes back as no width at all, standing where its contents would begin
    /// rather than at the separator — which is where a caret goes, and where anything written into it
    /// would be written.
    /// </para>
    /// </summary>
    private static TexCell Contents(TexNode cell, int start, int row, int column)
    {
        var at = start;
        var written = -1;
        var end = start;
        var blank = start;
        var counting = true;

        foreach (var child in cell.Children)
        {
            if (child.Role == TexRole.Separator) break;

            if (child.Kind == TexKind.Space)
            {
                if (counting) blank = at + child.Width;
            }
            else
            {
                counting = false;
                if (written < 0) written = at;
                end = at + child.Width;
            }

            at += child.Width;
        }

        return written < 0
        ? new TexCell(row, column, blank, 0, cell)
        : new TexCell(row, column, written, end - written, cell);
    }
}

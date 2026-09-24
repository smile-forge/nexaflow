using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Prose;

/// <summary>
/// A table, laid as the grid it is.
///
/// <para>
/// Every cell is a piece of its own, and the rows and the columns are declared as runs — the same thing a matrix
/// declares, answered by the same code, so dragging across a table behaves the way dragging across a matrix already
/// does and neither of them has a selection of its own.
/// </para>
/// <para>
/// A column is as wide as its widest cell wants to be, and only shares out the room when all of them together will not
/// fit — and then never below its longest word, so a squeezed table wraps its sentences rather than writing its names
/// over the next column. So a table of one narrow column and one wide one is drawn as one, rather than as two halves.
/// </para>
/// </summary>
public sealed partial class MarkdownBuilder
{
    private void Tabled(LayoutBuilder into, ContentPart part, double x, double room)
    {
        var rows = Body(part).Children.Where(child => child.Kind == MarkdownKinds.Row).ToList();
        var placed = Placed(rows);
        var columns = placed.Count == 0 ? 0 : placed.Max(row => row.Count == 0 ? 0 : row.Max(cell => cell.Column + cell.Across));

        if (columns == 0)
        {
            AsWritten(into, part, x, room);

            return;
        }

        var pad = Style.TextSize * 0.45;
        var (widths, asked) = Widths(placed, rows, columns, room, pad);

        // Laid before anything is placed, because a cell covering two rows has to know how tall both are —
        // and a row is only as tall as the cells that end in it, or one covering it would stretch it twice. A cell whose
        // words fit the column it was given is already laid: asking what it wanted laid it with room to spare, and the
        // same words with room to spare come out the same.
        var laid = new List<List<(LayoutTree Tree, Size Size)>>(placed.Count);

        for (var row = 0; row < placed.Count; row++)
        {
            var line = new List<(LayoutTree Tree, Size Size)>(placed[row].Count);

            for (var at = 0; at < placed[row].Count; at++)
            {
                var cell = placed[row][at];
                var across = Across(widths, cell, pad);

                line.Add(asked[row][at] is { } already && already.Size.Width <= across
                    ? already
                    : Apart(sub => Inside(sub, cell.Part, across, Faced(rows[row]))));
            }

            laid.Add(line);
        }

        var heights = Heights(placed, laid, pad);

        var top = _y;
        var lines = new List<double> { top };
        var cells = new int[placed.Count, columns];

        for (var row = 0; row < placed.Count; row++)
            for (var column = 0; column < columns; column++) cells[row, column] = -1;

        for (var row = 0; row < placed.Count; row++)
        {
            Rowed(into, rows[row], placed[row], laid[row], row, x, widths, heights, pad, cells);
            lines.Add(_y);
        }

        Grid(into, part, x, widths, lines, placed, columns);
        Reads(into, cells, placed.Count, columns);

        Reached(x + widths.Sum());
    }

    /// <summary>
    /// Where every cell sits in the grid, once the ones covering more than their own square are allowed for.
    ///
    /// <para>
    /// A cell is written in the next square nobody has claimed, not in the next column — so a cell covering
    /// two rows pushes the one under it along, exactly as it does on the page.
    /// </para>
    /// </summary>
    private static List<List<Cell>> Placed(List<ContentPart> rows)
    {
        var taken = new HashSet<(int Row, int Column)>();
        var placed = new List<List<Cell>>();

        for (var row = 0; row < rows.Count; row++)
        {
            var line = new List<Cell>();
            var column = 0;

            foreach (var part in Cells(rows[row]))
            {
                while (taken.Contains((row, column))) column++;

                var spans = Spans(part);
                var across = Math.Max(spans?.Across ?? 1, 1);
                var down = Math.Max(spans?.Down ?? 1, 1);

                for (var over = 0; over < down; over++)
                    for (var along = 0; along < across; along++) taken.Add((row + over, column + along));

                line.Add(new Cell(part, column, across, down));
                column += across;
            }

            placed.Add(line);
        }

        return placed;
    }

    /// <summary>What a cell was written to cover, where a stage worked out that it covers more than one square.</summary>
    private static MarkdownSpans? Spans(ContentPart cell) =>
        cell.Children.Select(child => child.Node.Held as MarkdownSpans).FirstOrDefault(spans => spans is not null);

    /// <summary>Whether what is in a cell is blocks rather than a run of words.</summary>
    private static bool Blocked(ContentPart cell) =>
        cell.Children.Any(child => child.Node.Held is bool held && held);

    /// <summary>What is written in a cell, read as whichever of the two things it is.</summary>
    private void Inside(LayoutBuilder into, ContentPart cell, double room, Face? face = null)
    {
        if (Blocked(cell)) Blocks(into, Body(cell), 0, room);
        else Text(into, Body(cell), 0, room, face ?? Face.Plain);
    }

    /// <summary>How a row's words are set — the head being what the columns are called rather than more of the table.</summary>
    private Face Faced(ContentPart row) =>
        row.Role == MarkdownRoles.Head
            ? Face.Plain with { Bold = true, Scale = 13.0 / 13.5, Ink = Style.Heading }
            : Face.Plain;

    /// <summary>How wide a cell is, which is every column it covers plus the lines between them.</summary>
    private static double Across(double[] widths, Cell cell, double pad)
    {
        var wide = 0.0;

        for (var at = cell.Column; at < cell.Column + cell.Across && at < widths.Length; at++) wide += widths[at];

        return Math.Max(wide - (pad * 2), 1);
    }

    /// <summary>
    /// How tall each row is. A cell is only allowed to make the row it <em>ends</em> in taller, and then only
    /// by whatever it still needs after the rows it already covers — or a cell spanning three rows would make
    /// each of them as tall as the whole of it.
    /// </summary>
    private double[] Heights(List<List<Cell>> placed, List<List<(LayoutTree Tree, Size Size)>> laid, double pad)
    {
        var heights = new double[placed.Count];
        var least = Style.TextSize + (pad * 2);

        for (var row = 0; row < placed.Count; row++) heights[row] = least;

        for (var span = 1; span <= Math.Max(1, placed.Count); span++)
            for (var row = 0; row < placed.Count; row++)
                for (var at = 0; at < placed[row].Count; at++)
                {
                    var cell = placed[row][at];
                    if (cell.Down != span) continue;

                    var last = Math.Min(row + cell.Down - 1, placed.Count - 1);
                    var has = 0.0;

                    for (var over = row; over <= last; over++) has += heights[over];

                    var wants = laid[row][at].Size.Height + (pad * 2);

                    if (wants > has) heights[last] += wants - has;
                }

        return heights;
    }

    /// <summary>A cell, and the square of the grid it was written into.</summary>
    private readonly record struct Cell(ContentPart Part, int Column, int Across, int Down);

    /// <summary>One row: its cells put down at the widths and heights the whole grid settled on.</summary>
    private void Rowed(LayoutBuilder into, ContentPart row, List<Cell> placed,
                       List<(LayoutTree Tree, Size Size)> laid, int at, double x,
                       double[] widths, double[] heights, double pad, int[,] cells)
    {
        var head = row.Role == MarkdownRoles.Head;
        var height = heights[at];
        var top = _y;

        if (head || at % 2 == 1)
        {
            into.Open(MarkdownPieces.Block, row, new Point(x, top));
            into.Draw(new WashMark(new Rect(0, 0, Math.Max(widths.Sum(), 1), height),
                                   head ? Style.TableHeaderBg : Style.TableAltRowBg));
            into.Close();
        }

        for (var index = 0; index < placed.Count; index++)
        {
            var cell = placed[index];
            if (cell.Column >= widths.Length) continue;

            var left = x;
            for (var before = 0; before < cell.Column && before < widths.Length; before++) left += widths[before];

            var width = Across(widths, cell, pad) + (pad * 2);
            var covers = height;

            for (var over = 1; over < cell.Down && at + over < heights.Length; over++) covers += heights[at + over];

            var piece = into.Open(MarkdownPieces.Cell, cell.Part, new Point(left, top));
            into.Graft(laid[index].Tree, new Point(Along(cell.Part, width, laid[index].Size.Width, pad), pad));
            into.Covers(new Rect(0, 0, width, covers));
            into.Close();

            for (var along = 0; along < cell.Across && cell.Column + along < cells.GetLength(1); along++)
                cells[at, cell.Column + along] = piece;
        }

        _y = top + height;
    }

    /// <summary>
    /// How far along its cell a stretch sits — which the rule under the head says, and nobody wrote on the cell.
    /// </summary>
    private static double Along(ContentPart cell, double width, double content, double pad) =>
        (cell.Part(Roles.Derived)?.Node.Held as string) switch
        {
            MarkdownAligns.Center => Math.Max((width - content) / 2, pad),
            MarkdownAligns.Right => Math.Max(width - content - pad, pad),
            _ => pad,
        };

    /// <summary>
    /// How wide each column wants to be, and how wide it gets — and each cell as laid while asking, for whoever can use it.
    /// Measured by laying each cell apart at the whole room, set as its row sets it — asking what it would take rather than
    /// guessing — and then shared out only if they will not all fit.
    ///
    /// <para>
    /// A cell covering several columns is left out of the asking. What it wants says nothing about any one of
    /// the columns it lies across, and counting it against the first would make that column as wide as the
    /// whole span. So is a cell holding blocks, whose laying is not only a matter of its words: what it laid at the whole
    /// room is not what it lays in its column, so it is not kept.
    /// </para>
    /// </summary>
    private (double[] Widths, List<(LayoutTree Tree, Size Size)?[]> Asked) Widths(List<List<Cell>> placed, List<ContentPart> rows,
                                                                                    int columns, double room, double pad)
    {
        var wants = new double[columns];
        var asked = new List<(LayoutTree Tree, Size Size)?[]>(placed.Count);

        for (var row = 0; row < placed.Count; row++)
        {
            var kept = new (LayoutTree Tree, Size Size)?[placed[row].Count];

            for (var at = 0; at < placed[row].Count; at++)
            {
                var cell = placed[row][at];
                if (cell.Across != 1 || cell.Column >= columns) continue;

                var face = Faced(rows[row]);
                var laid = Apart(sub => Inside(sub, cell.Part, room, face));

                wants[cell.Column] = Math.Max(wants[cell.Column], laid.Size.Width + (pad * 2));
                if (!Blocked(cell.Part)) kept[at] = laid;
            }

            asked.Add(kept);
        }

        var total = wants.Sum();
        if (total <= 0) return ([.. wants.Select(_ => Math.Max(room / columns, 1))], asked);
        if (total <= room) return (wants, asked);

        // Too wide for the room. A word cannot be broken to fit, so every column keeps what its longest one needs, and what
        // is left over goes to the columns by how much more each wanted — a column of long sentences gives its width up
        // before a column of names does. Where even the words will not fit, the table is wider than the room rather than
        // written over itself.
        var needs = Needs(placed, rows, columns, pad, wants);
        var least = needs.Sum();

        if (least >= room || total <= least) return (needs, asked);

        var spare = (room - least) / (total - least);

        return ([.. wants.Select((want, column) => needs[column] + ((want - needs[column]) * spare))], asked);
    }

    /// <summary>
    /// How narrow each column can be: as wide as the longest word any of its cells holds, found by laying each cell with no
    /// room at all, so every line holds one word. Asked only of a table too wide for its room — one that fits has no use for it.
    /// </summary>
    private double[] Needs(List<List<Cell>> placed, List<ContentPart> rows, int columns, double pad, double[] wants)
    {
        var needs = new double[columns];

        for (var column = 0; column < columns; column++) needs[column] = Math.Min(pad * 2, wants[column]);

        for (var row = 0; row < placed.Count; row++)
            foreach (var cell in placed[row])
            {
                if (cell.Across != 1 || cell.Column >= columns) continue;

                var laid = Apart(sub => Inside(sub, cell.Part, 1, Faced(rows[row])));

                needs[cell.Column] = Math.Min(Math.Max(needs[cell.Column], laid.Size.Width + (pad * 2)), wants[cell.Column]);
            }

        return needs;
    }

    /// <summary>
    /// The lines between the cells, drawn over them, as a table's rules are — and not drawn through a cell
    /// that was written to cover the square on the other side, because there is no edge there to rule.
    /// </summary>
    private void Grid(LayoutBuilder into, ContentPart part, double x, double[] widths, List<double> lines,
                      List<List<Cell>> placed, int columns)
    {
        var thin = Math.Max(1, Style.TextSize / 16);
        var wide = widths.Sum();
        var rows = lines.Count - 1;
        var owners = Owners(placed, rows, columns);

        var edges = new double[widths.Length + 1];
        for (var column = 0; column < widths.Length; column++) edges[column + 1] = edges[column] + widths[column];

        into.Open(MarkdownPieces.Block, part, new Point(x, lines[0]));

        // Across: the line over each row, and under the last, in the stretches no cell covers both sides of.
        for (var row = 0; row <= rows; row++)
        {
            var from = 0.0;

            for (var column = 0; column <= columns; column++)
            {
                if (column < columns && !Joined(owners, row - 1, column, row, column)) continue;

                var to = column < columns ? edges[Math.Min(column, widths.Length)] : wide;

                if (to > from) into.Draw(new RuleMark(new Rect(from, lines[row] - lines[0], to - from, thin), Style.TableBorder));

                from = column < columns ? edges[Math.Min(column + 1, widths.Length)] : wide;
            }
        }

        // Down: the line beside each column, in the rows where no cell covers both sides of it.
        for (var column = 0; column <= widths.Length; column++)
            for (var row = 0; row < rows; row++)
            {
                if (column > 0 && column < columns && Joined(owners, row, column - 1, row, column)) continue;

                into.Draw(new RuleMark(new Rect(edges[Math.Min(column, edges.Length - 1)], lines[row] - lines[0],
                                                thin, lines[row + 1] - lines[row]), Style.TableBorder));
            }

        into.Close();
    }

    /// <summary>Which cell covers each square of the grid, numbered in the order written — -1 for a square nothing covers.</summary>
    private static int[,] Owners(List<List<Cell>> placed, int rows, int columns)
    {
        var owners = new int[rows, columns];
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++) owners[row, column] = -1;

        var number = 0;

        for (var row = 0; row < placed.Count && row < rows; row++)
            foreach (var cell in placed[row])
            {
                for (var over = 0; over < cell.Down && row + over < rows; over++)
                    for (var along = 0; along < cell.Across && cell.Column + along < columns; along++)
                        owners[row + over, cell.Column + along] = number;

                number++;
            }

        return owners;
    }

    /// <summary>Whether one cell covers both squares — so there is no edge between them to rule. Past the grid is nobody's.</summary>
    private static bool Joined(int[,] owners, int row, int column, int nextRow, int nextColumn)
    {
        var rows = owners.GetLength(0);
        if (row < 0 || nextRow >= rows) return false;

        var owner = owners[row, column];
        return owner >= 0 && owner == owners[nextRow, nextColumn];
    }

    /// <summary>
    /// Which cells read across and which read down. The same declaration a matrix makes, so a drag over a table picks
    /// cells out the way a drag over a matrix does — a cell that was never drawn is left out, because there is nothing
    /// there to step to.
    /// </summary>
    private static void Reads(LayoutBuilder into, int[,] cells, int rows, int columns)
    {
        for (var row = 0; row < rows; row++)
            into.Runs([.. Line(cells, row, columns, across: true)], vertical: false);

        for (var column = 0; column < columns; column++)
            into.Runs([.. Line(cells, column, rows, across: false)], vertical: true);
    }

    private static IEnumerable<int> Line(int[,] cells, int at, int count, bool across)
    {
        for (var step = 0; step < count; step++)
        {
            var piece = across ? cells[at, step] : cells[step, at];
            if (piece >= 0) yield return piece;
        }
    }

    private static List<ContentPart> Cells(ContentPart row) =>
        [.. row.Children.Where(child => child.Kind == MarkdownKinds.Cell)];
}

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
/// fit. So a table of one narrow column and one wide one is drawn as one, rather than as two halves.
/// </para>
/// </summary>
public sealed partial class MarkdownBuilder
{
    private void Tabled(LayoutBuilder into, ContentPart part, double x, double room)
    {
        var rows = Body(part).Children.Where(child => child.Kind == MarkdownKinds.Row).ToList();
        var columns = rows.Count == 0 ? 0 : rows.Max(row => Cells(row).Count);

        if (columns == 0)
        {
            AsWritten(into, part, x, room);

            return;
        }

        var pad = Style.TextSize * 0.45;
        var widths = Widths(rows, columns, room, pad);
        var top = _y;

        var cells = new int[rows.Count, columns];
        for (var row = 0; row < rows.Count; row++)
            for (var column = 0; column < columns; column++) cells[row, column] = -1;

        var lines = new List<double> { top };

        for (var row = 0; row < rows.Count; row++)
        {
            Rowed(into, rows[row], row, x, widths, pad, cells);
            lines.Add(_y);
        }

        Grid(into, part, x, widths, lines);
        Reads(into, cells, rows.Count, columns);

        Reached(x + widths.Sum());
    }

    /// <summary>One row: its cells laid apart so the row's height is known before any of them is put down.</summary>
    private void Rowed(LayoutBuilder into, ContentPart row, int at, double x, double[] widths, double pad, int[,] cells)
    {
        var written = Cells(row);
        var head = row.Role == MarkdownRoles.Head;
        var face = head ? Face.Plain with { Bold = true } : Face.Plain;

        var laid = new (LayoutTree Tree, Size Size)[written.Count];

        for (var column = 0; column < written.Count; column++)
        {
            var cell = written[column];
            var inside = Math.Max(widths[Math.Min(column, widths.Length - 1)] - (pad * 2), 1);

            laid[column] = Apart(sub => Text(sub, Body(cell), 0, inside, face));
        }

        var height = (laid.Length == 0 ? Style.TextSize : laid.Max(one => one.Size.Height)) + (pad * 2);
        var top = _y;

        if (head || at % 2 == 1)
        {
            into.Open(MarkdownPieces.Block, row, new Point(x, top));
            into.Draw(new WashMark(new Rect(0, 0, Math.Max(widths.Sum(), 1), height),
                                   head ? Style.TableHeaderBg : Style.TableAltRowBg));
            into.Close();
        }

        var cursor = x;

        for (var column = 0; column < written.Count && column < widths.Length; column++)
        {
            var width = widths[column];
            var cell = written[column];

            var piece = into.Open(MarkdownPieces.Cell, cell, new Point(cursor, top));
            into.Graft(laid[column].Tree, new Point(Along(cell, width, laid[column].Size.Width, pad), pad));
            into.Covers(new Rect(0, 0, width, height));
            into.Close();

            if (column < cells.GetLength(1)) cells[at, column] = piece;

            cursor += width;
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
    /// How wide each column wants to be, and how wide it gets. Measured by laying each cell apart at the whole room —
    /// asking what it would take rather than guessing — and then shared out only if they will not all fit.
    /// </summary>
    private double[] Widths(List<ContentPart> rows, int columns, double room, double pad)
    {
        var wants = new double[columns];

        foreach (var row in rows)
        {
            var cells = Cells(row);

            for (var column = 0; column < cells.Count && column < columns; column++)
            {
                var cell = cells[column];
                var (_, size) = Apart(sub => Text(sub, Body(cell), 0, room, Face.Plain));

                wants[column] = Math.Max(wants[column], size.Width + (pad * 2));
            }
        }

        var total = wants.Sum();
        if (total <= 0) return [.. wants.Select(_ => Math.Max(room / columns, 1))];
        if (total <= room) return wants;

        var share = room / total;
        var least = Style.TextSize * 2.5;

        return [.. wants.Select(want => Math.Max(want * share, least))];
    }

    /// <summary>The lines between the cells, drawn over them, as a table's rules are.</summary>
    private void Grid(LayoutBuilder into, ContentPart part, double x, double[] widths, List<double> lines)
    {
        var thin = Math.Max(1, Style.TextSize / 16);
        var wide = widths.Sum();

        into.Open(MarkdownPieces.Block, part, new Point(x, lines[0]));

        foreach (var line in lines)
            into.Draw(new RuleMark(new Rect(0, line - lines[0], wide, thin), Style.TableBorder));

        var cursor = 0.0;

        for (var column = 0; column <= widths.Length; column++)
        {
            into.Draw(new RuleMark(new Rect(cursor, 0, thin, lines[^1] - lines[0]), Style.TableBorder));

            if (column < widths.Length) cursor += widths[column];
        }

        into.Close();
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

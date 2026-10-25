using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Markdown.Layout.Prose;

/// <summary>A table, laid as the grid it is — every cell a piece, the rows and columns declared as runs.</summary>
public sealed partial class PortableMarkdownBuilder
{
    private void Tabled(PortableBuilder into, ContentPart part, double x, double room)
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

        var laid = new List<List<(PortableTree Tree, double Width, double Height)>>(placed.Count);

        for (var row = 0; row < placed.Count; row++)
        {
            var line = new List<(PortableTree Tree, double Width, double Height)>(placed[row].Count);

            for (var at = 0; at < placed[row].Count; at++)
            {
                var cell = placed[row][at];
                var across = Across(widths, cell, pad);

                line.Add(asked[row][at] is { } already && already.Width <= across
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

    private static MarkdownSpans? Spans(ContentPart cell) =>
        cell.Children.Select(child => child.Node.Held as MarkdownSpans).FirstOrDefault(spans => spans is not null);

    private static bool Blocked(ContentPart cell) => Body(cell) is { Kind: not Kinds.Words };

    private void Inside(PortableBuilder into, ContentPart cell, double room, Face? face = null)
    {
        if (Blocked(cell)) Blocks(into, Body(cell), 0, room);
        else Text(into, Body(cell), 0, room, face ?? Face.Plain);
    }

    private Face Faced(ContentPart row) =>
        row.Role == MarkdownRoles.Head
            ? Face.Plain with { Bold = true, Scale = 13.0 / 13.5, Ink = Style.Heading }
            : Face.Plain;

    private static double Across(double[] widths, Cell cell, double pad)
    {
        var wide = 0.0;

        for (var at = cell.Column; at < cell.Column + cell.Across && at < widths.Length; at++) wide += widths[at];

        return Math.Max(wide - (pad * 2), 1);
    }

    private double[] Heights(List<List<Cell>> placed, List<List<(PortableTree Tree, double Width, double Height)>> laid, double pad)
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

                    var wants = laid[row][at].Height + (pad * 2);

                    if (wants > has) heights[last] += wants - has;
                }

        return heights;
    }

    private readonly record struct Cell(ContentPart Part, int Column, int Across, int Down);

    private void Rowed(PortableBuilder into, ContentPart row, List<Cell> placed,
                       List<(PortableTree Tree, double Width, double Height)> laid, int at, double x,
                       double[] widths, double[] heights, double pad, int[,] cells)
    {
        var head = row.Role == MarkdownRoles.Head;
        var height = heights[at];
        var top = _y;

        if (head || at % 2 == 1)
        {
            into.Open(PortablePieces.Block, row, new LayoutPoint(x, top), PortableStops.None);
            into.Draw(new PortableWashMark(new LayoutRect(0, 0, Math.Max(widths.Sum(), 1), height),
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

            var piece = into.Open(PortablePieces.Cell, cell.Part, new LayoutPoint(left, top));
            into.Graft(laid[index].Tree, new LayoutPoint(Along(cell.Part, width, laid[index].Width, pad), pad));
            into.Covers(new LayoutRect(0, 0, width, covers));
            into.Close();

            for (var along = 0; along < cell.Across && cell.Column + along < cells.GetLength(1); along++)
                cells[at, cell.Column + along] = piece;
        }

        _y = top + height;
    }

    private static double Along(ContentPart cell, double width, double content, double pad) =>
        (cell.Part(Roles.Derived)?.Node.Held as string) switch
        {
            MarkdownAligns.Center => Math.Max((width - content) / 2, pad),
            MarkdownAligns.Right => Math.Max(width - content - pad, pad),
            _ => pad,
        };

    private (double[] Widths, List<(PortableTree Tree, double Width, double Height)?[]> Asked) Widths(
        List<List<Cell>> placed, List<ContentPart> rows, int columns, double room, double pad)
    {
        var wants = new double[columns];
        var asked = new List<(PortableTree Tree, double Width, double Height)?[]>(placed.Count);

        for (var row = 0; row < placed.Count; row++)
        {
            var kept = new (PortableTree Tree, double Width, double Height)?[placed[row].Count];

            for (var at = 0; at < placed[row].Count; at++)
            {
                var cell = placed[row][at];
                if (cell.Across != 1 || cell.Column >= columns) continue;

                var face = Faced(rows[row]);
                var laid = Apart(sub => Inside(sub, cell.Part, room, face));

                wants[cell.Column] = Math.Max(wants[cell.Column], laid.Width + (pad * 2));
                if (!Blocked(cell.Part)) kept[at] = laid;
            }

            asked.Add(kept);
        }

        var total = wants.Sum();
        if (total <= 0) return ([.. wants.Select(_ => Math.Max(room / columns, 1))], asked);
        if (total <= room) return (wants, asked);

        var needs = Needs(placed, rows, columns, pad, wants);
        var least = needs.Sum();

        if (least >= room || total <= least) return (needs, asked);

        var spare = (room - least) / (total - least);

        return ([.. wants.Select((want, column) => needs[column] + ((want - needs[column]) * spare))], asked);
    }

    private double[] Needs(List<List<Cell>> placed, List<ContentPart> rows, int columns, double pad, double[] wants)
    {
        var needs = new double[columns];

        for (var column = 0; column < columns; column++) needs[column] = Math.Min(pad * 2, wants[column]);

        for (var row = 0; row < placed.Count; row++)
            foreach (var cell in placed[row])
            {
                if (cell.Across != 1 || cell.Column >= columns) continue;

                var laid = Apart(sub => Inside(sub, cell.Part, 1, Faced(rows[row])));

                needs[cell.Column] = Math.Min(Math.Max(needs[cell.Column], laid.Width + (pad * 2)), wants[cell.Column]);
            }

        return needs;
    }

    private void Grid(PortableBuilder into, ContentPart part, double x, double[] widths, List<double> lines,
                      List<List<Cell>> placed, int columns)
    {
        var thin = Math.Max(1, Style.TextSize / 16);
        var wide = widths.Sum();
        var rows = lines.Count - 1;
        var owners = Owners(placed, rows, columns);

        var edges = new double[widths.Length + 1];
        for (var column = 0; column < widths.Length; column++) edges[column + 1] = edges[column] + widths[column];

        into.Open(PortablePieces.Block, part, new LayoutPoint(x, lines[0]), PortableStops.None);

        for (var row = 0; row <= rows; row++)
        {
            var from = 0.0;

            for (var column = 0; column <= columns; column++)
            {
                if (column < columns && !Joined(owners, row - 1, column, row, column)) continue;

                var to = column < columns ? edges[Math.Min(column, widths.Length)] : wide;

                if (to > from) into.Draw(new PortableRuleMark(new LayoutRect(from, lines[row] - lines[0], to - from, thin), Style.TableBorder));

                from = column < columns ? edges[Math.Min(column + 1, widths.Length)] : wide;
            }
        }

        for (var column = 0; column <= widths.Length; column++)
            for (var row = 0; row < rows; row++)
            {
                if (column > 0 && column < columns && Joined(owners, row, column - 1, row, column)) continue;

                into.Draw(new PortableRuleMark(new LayoutRect(edges[Math.Min(column, edges.Length - 1)], lines[row] - lines[0],
                                                              thin, lines[row + 1] - lines[row]), Style.TableBorder));
            }

        into.Close();
    }

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

    private static bool Joined(int[,] owners, int row, int column, int nextRow, int nextColumn)
    {
        var rows = owners.GetLength(0);
        if (row < 0 || nextRow >= rows) return false;

        var owner = owners[row, column];
        return owner >= 0 && owner == owners[nextRow, nextColumn];
    }

    private static void Reads(PortableBuilder into, int[,] cells, int rows, int columns)
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

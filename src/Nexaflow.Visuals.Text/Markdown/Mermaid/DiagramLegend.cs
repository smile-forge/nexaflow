using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>One row of a legend: a swatch, and the words in each of the legend's columns — null in a column it leaves empty.</summary>
/// <param name="Part">What pressing the row means: the slice, the series, the actor it says what the colour is.</param>
/// <param name="Swatch">The colour it explains — or null for a row with nothing drawn yet to match, shown as the square one goes in.</param>
internal sealed record DiagramKey(ISourcePart? Part, Brush? Swatch, IReadOnlyList<DiagramWords?> Cells);

/// <summary>
/// A legend: a row per thing a diagram draws in its own colour, each a swatch and the words that say what it is.
///
/// <para>
/// Down a column the rows are a table, so what each says in one column lines up under the rest; along a line they are set
/// one after another, because a table of one row is a row. Each column is a kind of piece of its own — a label, a value, a
/// share — and whether a cell is typed into or only pressed is its words' (<see cref="DiagramWords"/>).
/// </para>
/// </summary>
/// <param name="columns">The kind of piece each column's words are set as.</param>
/// <param name="across">Whether the rows run along a line — a legend over or under a diagram — rather than down a column.</param>
/// <param name="outline">What the square of a swatch with no colour yet is drawn in.</param>
internal sealed class DiagramLegend(IReadOnlyList<DiagramKey> rows, IReadOnlyList<string> columns, bool across, Brush outline)
{
    public const double SwatchSize = 14;

    /// <summary>Clear air between a swatch and its words, and between columns.</summary>
    public const double Gap = 8;

    /// <summary>Clear air between rows down a column.</summary>
    public const double RowGap = 6;

    /// <summary>Clear air between rows along a line.</summary>
    public const double Apart = 24;

    /// <summary>The widest the legend says it is down a column, however wide its words are.</summary>
    public double Room { get; init; } = double.PositiveInfinity;

    /// <summary>How much room the legend takes.</summary>
    public Size Size => rows.Count == 0
        ? default
        : across
            ? new Size(rows.Sum(row => Width(row) + Apart) - Apart, rows.Max(Height))
            : new Size(Math.Min(Room, Left(columns.Count - 1) + Widest(columns.Count - 1)), rows.Sum(row => Height(row) + RowGap) - RowGap);

    /// <summary>Draws the legend with its top left at <paramref name="at"/>.</summary>
    public void Draw(LayoutBuilder build, Point at)
    {
        build.Open(MermaidPiece.Legend, part: null, stops: Stops.None);

        var (x, y) = (at.X, at.Y);
        foreach (var row in rows)
        {
            var height = Height(row);
            build.Open(MermaidPiece.Key, row.Part, new Point(x, y), stops: Stops.None);

            build.Open(MermaidPiece.Swatch, part: null, new Point(0, (height - SwatchSize) / 2), stops: Stops.None);
            build.Draw(Swatch(row.Swatch));
            build.Close();

            var left = SwatchSize + Gap;
            for (var column = 0; column < row.Cells.Count && column < columns.Count; column++)
            {
                if (row.Cells[column] is not { } cell) continue;

                var place = across ? left : Left(column);
                cell.Set(build, new Point(place, 0), columns[column]);
                left = place + cell.Width + Gap;
            }

            build.Close();

            if (across) x += Width(row) + Apart;
            else y += height + RowGap;
        }

        build.Close();
    }

    private static double Height(DiagramKey row) => Math.Max(SwatchSize, row.Cells.OfType<DiagramWords>().Select(cell => cell.Height).DefaultIfEmpty(0).Max());

    /// <summary>How wide a row is set along a line: its swatch, and its words one after another.</summary>
    private static double Width(DiagramKey row)
    {
        var cells = row.Cells.OfType<DiagramWords>().ToList();
        return SwatchSize + Gap + cells.Sum(cell => cell.Width) + (Math.Max(0, cells.Count - 1) * Gap);
    }

    /// <summary>Where a column starts down a column of rows: past the swatch, and past every column before it that any row writes in.</summary>
    private double Left(int column)
    {
        var left = SwatchSize + Gap;
        for (var before = 0; before < column; before++)
            if (rows.Any(row => Cell(row, before) is not null))
                left += Widest(before) + Gap;

        return left;
    }

    private double Widest(int column) => rows.Select(row => Cell(row, column)?.Width ?? 0).DefaultIfEmpty(0).Max();

    private static DiagramWords? Cell(DiagramKey row, int column) => column < row.Cells.Count ? row.Cells[column] : null;

    /// <summary>A row's swatch: the colour it explains, or only the square one goes in.</summary>
    private LayoutMark Swatch(Brush? ink)
    {
        if (ink is not null) return new RuleMark(new Rect(0, 0, SwatchSize, SwatchSize), ink);

        var square = new RectangleGeometry(new Rect(0.5, 0.5, SwatchSize - 1, SwatchSize - 1));
        square.Freeze();
        return new GeometryMark(square, null, outline, 1);
    }
}

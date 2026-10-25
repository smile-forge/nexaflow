using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.Layout.Mermaid;

/// <summary>One row of a legend: a swatch, and the words in each of the legend's columns — null in a column it leaves empty.</summary>
/// <param name="Part">What pressing the row means: the slice, the series, the actor it says what the colour is.</param>
/// <param name="Swatch">The colour it explains — or null for a row with nothing drawn yet to match, shown as the square one goes in.</param>
internal sealed record PortableDiagramKey(ISourcePart? Part, LayoutInk? Swatch, IReadOnlyList<PortableDiagramWords?> Cells);

/// <summary>
/// A legend: a row per thing a diagram draws in its own colour, each a swatch and the words that say what it is.
///
/// <para>
/// Down a column the rows are a table, so what each says in one column lines up under the rest; along a line they are set
/// one after another, because a table of one row is a row. Each column is a kind of piece of its own — a label, a value, a
/// share — and whether a cell is typed into or only pressed is its words' (<see cref="PortableDiagramWords"/>).
/// </para>
/// </summary>
/// <param name="columns">The kind of piece each column's words are set as.</param>
/// <param name="across">Whether the rows run along a line — a legend over or under a diagram — rather than down a column.</param>
/// <param name="outline">What the square of a swatch with no colour yet is drawn in.</param>
internal sealed class PortableDiagramLegend(IReadOnlyList<PortableDiagramKey> rows, IReadOnlyList<string> columns, bool across, LayoutInk outline)
{
    /// <summary>How big a swatch's square is, where nothing asks for another size.</summary>
    public const double SwatchSize = 14;

    /// <summary>How big a swatch's square is drawn — a diagram's front matter may ask: a radar's <c>legendBoxSize</c>.</summary>
    public double Square { get; init; } = SwatchSize;

    /// <summary>Clear air between a swatch and its words, and between columns.</summary>
    public const double Gap = 8;

    /// <summary>Clear air between rows down a column.</summary>
    public const double RowGap = 6;

    /// <summary>Clear air between rows along a line.</summary>
    public const double Apart = 24;

    /// <summary>The widest the legend says it is down a column, however wide its words are.</summary>
    public double Room { get; init; } = double.PositiveInfinity;

    /// <summary>
    /// What the key is drawn on, where a diagram sets it off from the rest rather than letting it sit on the page — the
    /// surface behind it and the edge round it. Null for a key with nothing behind it.
    /// </summary>
    public (LayoutInk Fill, LayoutInk Edge)? Framed { get; init; }

    /// <summary>The air the frame keeps round the rows.</summary>
    public const double Framing = 8;

    /// <summary>How much room the legend takes.</summary>
    public LayoutSize Size
    {
        get
        {
            if (rows.Count == 0) return default;

            var taken = across
                ? new LayoutSize(rows.Sum(row => Width(row) + Apart) - Apart, rows.Max(Height))
                : new LayoutSize(Math.Min(Room, Left(columns.Count - 1) + Widest(columns.Count - 1)),
                                 rows.Sum(row => Height(row) + RowGap) - RowGap);

            return this.Framed is null
                ? taken
                : new LayoutSize(taken.Width + (Framing * 2), taken.Height + (Framing * 2));
        }
    }

    /// <summary>Draws the legend with its top left at <paramref name="at"/>.</summary>
    public void Draw(PortableBuilder build, LayoutPoint at)
    {
        build.Open(MermaidPiece.Legend, part: null, stops: PortableStops.None);

        if (this.Framed is { } framed)
        {
            var frame = build.Medium.Rectangle(new LayoutRect(at, Size), Framing / 2, Framing / 2);
            build.Draw(PortableDiagramMarks.Outlined(build.Medium, frame, framed.Fill, framed.Edge, 1));
        }

        var (x, y) = this.Framed is null ? (at.X, at.Y) : (at.X + Framing, at.Y + Framing);
        foreach (var row in rows)
        {
            var height = Height(row);
            build.Open(MermaidPiece.Key, row.Part, new LayoutPoint(x, y), stops: PortableStops.None);

            build.Open(MermaidPiece.Swatch, part: null, new LayoutPoint(0, (height - Square) / 2), stops: PortableStops.None);
            build.Draw(Swatch(build.Medium, row.Swatch));
            build.Close();

            var left = Square + Gap;
            for (var column = 0; column < row.Cells.Count && column < columns.Count; column++)
            {
                if (row.Cells[column] is not { } cell) continue;

                var place = across ? left : Left(column);
                cell.Set(build, new LayoutPoint(place, 0), columns[column]);
                left = place + cell.Width + Gap;
            }

            build.Close();

            if (across) x += Width(row) + Apart;
            else y += height + RowGap;
        }

        build.Close();
    }

    private double Height(PortableDiagramKey row) => Math.Max(Square, row.Cells.OfType<PortableDiagramWords>().Select(cell => cell.Height).DefaultIfEmpty(0).Max());

    /// <summary>How wide a row is set along a line: its swatch, and its words one after another.</summary>
    private double Width(PortableDiagramKey row)
    {
        var cells = row.Cells.OfType<PortableDiagramWords>().ToList();
        return Square + Gap + cells.Sum(cell => cell.Width) + (Math.Max(0, cells.Count - 1) * Gap);
    }

    /// <summary>Where a column starts down a column of rows: past the swatch, and past every column before it that any row writes in.</summary>
    private double Left(int column)
    {
        var left = Square + Gap;
        for (var before = 0; before < column; before++)
            if (rows.Any(row => Cell(row, before) is not null))
                left += Widest(before) + Gap;

        return left;
    }

    private double Widest(int column) => rows.Select(row => Cell(row, column)?.Width ?? 0).DefaultIfEmpty(0).Max();

    private static PortableDiagramWords? Cell(PortableDiagramKey row, int column) => column < row.Cells.Count ? row.Cells[column] : null;

    /// <summary>A row's swatch: the colour it explains, or only the square one goes in.</summary>
    private PortableMark Swatch(ILayoutMedium medium, LayoutInk? ink)
    {
        if (ink is not null) return new PortableRuleMark(new LayoutRect(0, 0, Square, Square), ink);

        var square = medium.Rectangle(new LayoutRect(0.5, 0.5, Square - 1, Square - 1));
        return PortableDiagramMarks.Outlined(medium, square, null, outline, 1);
    }
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>One row of a compartment: the words set in each of its columns, or nothing where a column writes none.</summary>
internal sealed record DiagramRow(IReadOnlyList<DiagramWords?> Columns)
{
    public static DiagramRow Of(params DiagramWords?[] columns) => new(columns);
}

/// <summary>
/// One compartment of a box: its rows, and how they are set in it.
/// </summary>
/// <param name="Rows">Its rows, in the order they are drawn — none at all for a band written but left empty.</param>
internal sealed record DiagramCompartment(IReadOnlyList<DiagramRow> Rows)
{
    /// <summary>Whether its rows are set across the middle of the box — a name — rather than from the left of it.</summary>
    public bool Centred { get; init; }

    /// <summary>
    /// Whether its columns line up down the band, each row's second column starting where every other row's does — an ER
    /// diagram's attributes — rather than each column following the one before it along its own row.
    /// </summary>
    public bool Aligned { get; init; }
}

/// <summary>
/// A box of compartments, measured: what a class, a requirement and an entity are all drawn as — rows a row deep in bands one
/// under another, a rule the width of the box between each band and the next.
///
/// <para>
/// The arithmetic is the same for all of them and is decided here: how deep a band is, how wide the box has to be for the
/// widest row in it, where each row sits in its band, and where the rules run. What is drawn on a row — a link, a rule under a
/// static member, a colour — is the diagram's own, so <see cref="Placed"/> hands back where each row's words go and the
/// diagram draws them.
/// </para>
/// </summary>
internal sealed class DiagramBox
{
    private readonly IReadOnlyList<DiagramCompartment> _bands;
    private readonly IReadOnlyList<IReadOnlyList<double>> _columns;
    private readonly double _row;
    private readonly double _pad;
    private readonly double _air;
    private readonly double _gap;

    private DiagramBox(IReadOnlyList<DiagramCompartment> bands, IReadOnlyList<IReadOnlyList<double>> columns,
                                IReadOnlyList<double> depths, Size size, double row, double pad, double air, double gap)
    {
        _bands = bands;
        _columns = columns;
        _row = row;
        _pad = pad;
        _air = air;
        _gap = gap;

        Depths = depths;
        Size = size;
    }

    /// <summary>How big the box is: the widest row in it, and every band's depth — never less than <c>least</c>.</summary>
    public Size Size { get; }

    /// <summary>How deep each band is, in the order they were given.</summary>
    public IReadOnlyList<double> Depths { get; }

    /// <summary>
    /// Measures a box of bands.
    /// </summary>
    /// <param name="row">How deep one row is.</param>
    /// <param name="pad">The air either side of what is written in the box.</param>
    /// <param name="air">The air above and below the rows of each band.</param>
    /// <param name="gap">The space between one column of a row and the next.</param>
    /// <param name="least">How wide and how deep the box is whatever little is written in it.</param>
    /// <param name="chrome">The room it keeps for its outline, so its rows are not drawn against it.</param>
    public static DiagramBox Measure(IReadOnlyList<DiagramCompartment> bands, double row, double pad, double air,
                                              double gap, Size least, double chrome = 0)
    {
        var columns = bands.Select(Widths).ToList();
        var depths = bands.Select(band => (band.Rows.Count * row) + (air * 2)).ToList();

        var widest = bands.Count == 0 ? 0 : bands.Select((band, at) => Across(band, columns[at], gap)).Max();
        var size = new Size(Math.Max(least.Width, widest + (pad * 2)),
                            Math.Max(least.Height, depths.Sum()) + chrome);

        return new DiagramBox(bands, columns, depths, size, row, pad, air, gap);
    }

    /// <summary>Where the rule under each band but the last runs, across a box drawn at <paramref name="bounds"/>.</summary>
    public IEnumerable<double> Rules(Rect bounds)
    {
        var top = bounds.Y;

        foreach (var depth in Depths.SkipLast(1))
        {
            top += depth;
            yield return top;
        }
    }

    /// <summary>
    /// Where every row's words go in a box drawn at <paramref name="bounds"/>: which band it is in, which row of that band it
    /// is, and each of its columns with where the words are set.
    /// </summary>
    public IEnumerable<(int Band, int At, IReadOnlyList<(DiagramWords Words, Point Where)> Set)> Placed(Rect bounds)
    {
        var top = bounds.Y;

        for (var band = 0; band < _bands.Count; band++)
        {
            var rows = _bands[band].Rows;
            var down = top + _air;

            for (var at = 0; at < rows.Count; at++)
            {
                yield return (band, at, Set(_bands[band], _columns[band], rows[at], bounds, down));
                down += _row;
            }

            top += Depths[band];
        }
    }

    /// <summary>Where one row's columns are set: from the left of the box, or across the middle of it.</summary>
    private IReadOnlyList<(DiagramWords Words, Point Where)> Set(DiagramCompartment band, IReadOnlyList<double> columns,
                                                                DiagramRow row, Rect bounds, double top)
    {
        var across = band.Centred
            ? bounds.X + ((bounds.Width - Wide(row, columns, band.Aligned, _gap)) / 2)
            : bounds.X + _pad;

        var set = new List<(DiagramWords, Point)>();

        for (var at = 0; at < row.Columns.Count; at++)
        {
            var width = band.Aligned ? At(columns, at) : row.Columns[at]?.Width ?? 0;

            if (row.Columns[at] is { } words)
                set.Add((words, new Point(across, top + ((_row - words.Height) / 2))));

            if (width > 0) across += width + _gap;
        }

        return set;
    }

    /// <summary>How wide each column of a band runs, which is the widest that column is written in any of its rows.</summary>
    private static IReadOnlyList<double> Widths(DiagramCompartment band)
    {
        var columns = new List<double>();

        foreach (var row in band.Rows)
            for (var at = 0; at < row.Columns.Count; at++)
            {
                while (columns.Count <= at) columns.Add(0);
                columns[at] = Math.Max(columns[at], row.Columns[at]?.Width ?? 0);
            }

        return columns;
    }

    /// <summary>How wide a band runs: its columns where they line up, and otherwise its widest row.</summary>
    private static double Across(DiagramCompartment band, IReadOnlyList<double> columns, double gap) =>
        band.Aligned
            ? columns.Where(column => column > 0).Sum() + (Math.Max(0, columns.Count(column => column > 0) - 1) * gap)
            : band.Rows.Count == 0 ? 0 : band.Rows.Max(row => Wide(row, columns, aligned: false, gap));

    /// <summary>How wide one row runs, columns and the space between them and all.</summary>
    private static double Wide(DiagramRow row, IReadOnlyList<double> columns, bool aligned, double gap)
    {
        var wide = 0.0;
        var written = 0;

        for (var at = 0; at < row.Columns.Count; at++)
        {
            var width = aligned ? At(columns, at) : row.Columns[at]?.Width ?? 0;
            if (width <= 0) continue;

            wide += width;
            written++;
        }

        return wide + (Math.Max(0, written - 1) * gap);
    }

    private static double At(IReadOnlyList<double> columns, int at) => at < columns.Count ? columns[at] : 0;
}

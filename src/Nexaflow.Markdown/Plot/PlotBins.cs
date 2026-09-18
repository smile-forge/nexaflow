using System;
using System.Collections.Generic;

namespace Nexaflow.Markdown.Plot;

/// <summary>One bin: where its middle is, and how many rows fell in it.</summary>
public sealed record PlotBin(double X, double Y, int Count);

/// <summary>The bins a set of points falls into, and how big one is.</summary>
public sealed record PlotBinning(IReadOnlyList<PlotBin> Bins, double Wide, double Tall);

/// <summary>
/// Cutting a plane into bins and counting what falls in each — the usual answer to a scatter plot with
/// more points than it has room for, where what a reader wants to see is where the points <em>are</em>
/// rather than which one is which.
///
/// <para>
/// Given places on the panel rather than values, and for a reason: a hexagon is only regular in the
/// space it is drawn in, and binning in the values' own space would give hexagons stretched by whatever
/// the axes happened to span. The counts come out the same either way; the shapes do not.
/// </para>
/// <para>
/// WPF-free, so the binning is tested without a desktop — the same division that keeps a molecule's
/// layout and a word cloud's packing out of their builders.
/// </para>
/// </summary>
public static class PlotBins
{
    /// <summary>The most bins a side may be cut into, so a mistyped number cannot ask for a million.</summary>
    public const int MostBins = 400;

    public const int FewestBins = 1;

    /// <summary>What <c>bins:</c> takes where the block writes none.</summary>
    public const int Bins = 30;

    /// <summary>
    /// The plane cut into rectangles: <paramref name="across"/> of them one way and
    /// <paramref name="up"/> the other, counting what falls in each.
    /// </summary>
    public static PlotBinning Rectangles(IReadOnlyList<(double X, double Y)> points,
                                         double wide, double tall, int across, int up)
    {
        across = Math.Clamp(across, FewestBins, MostBins);
        up = Math.Clamp(up, FewestBins, MostBins);

        var dx = wide / across;
        var dy = tall / up;

        var counts = new Dictionary<(int, int), int>();

        foreach (var (x, y) in points)
        {
            var i = Math.Clamp((int)Math.Floor(x / dx), 0, across - 1);
            var j = Math.Clamp((int)Math.Floor(y / dy), 0, up - 1);

            counts[(i, j)] = counts.GetValueOrDefault((i, j)) + 1;
        }

        var bins = new List<PlotBin>();

        foreach (var (where, count) in counts)
            bins.Add(new PlotBin((where.Item1 + 0.5) * dx, (where.Item2 + 0.5) * dy, count));

        return new PlotBinning(bins, dx, dy);
    }

    /// <summary>
    /// The plane cut into hexagons, which tile it without the corners two rectangles share — so a point is
    /// never as far from its own bin's middle as it can be in a square.
    ///
    /// <para>
    /// Each point goes to the nearest middle, which for a hexagonal lattice is the same as saying it goes
    /// to the hexagon it lies inside. Only two rows can hold it — the one it rounds into and the nearer of
    /// its neighbours — and within a row only one middle can be nearest, so two candidates settle it.
    /// </para>
    /// <para>
    /// d3-hexbin compares those two in the lattice's own units, where a step across and a step aslant are
    /// not the same length. That is near enough for a picture and not near enough for a claim, so the
    /// comparison here is of real distances.
    /// </para>
    /// </summary>
    public static PlotBinning Hexagons(IReadOnlyList<(double X, double Y)> points, double wide, int across)
    {
        across = Math.Clamp(across, FewestBins, MostBins);

        // The radius that fits `across` hexagons over the width, side by side.
        var radius = wide / (across * Math.Sqrt(3));
        if (radius <= 0) radius = 1;

        var dx = radius * Math.Sqrt(3);
        var dy = radius * 1.5;

        var counts = new Dictionary<(int I, int J), int>();

        foreach (var (x, y) in points)
        {
            var py = y / dy;
            var pj = (int)Math.Round(py);
            var pi = Across(x, pj, dx);

            // The nearer neighbouring row, which is the only other one that can hold it.
            var pj2 = pj + (py < pj ? -1 : 1);
            var pi2 = Across(x, pj2, dx);

            if (Apart(x, y, pi2, pj2, dx, dy) < Apart(x, y, pi, pj, dx, dy))
            {
                pi = pi2;
                pj = pj2;
            }

            counts[(pi, pj)] = counts.GetValueOrDefault((pi, pj)) + 1;
        }

        var bins = new List<PlotBin>();

        foreach (var (where, count) in counts)
            bins.Add(new PlotBin(Middle(where.I, where.J, dx), where.J * dy, count));

        // A hexagon standing on a point is two radii tall and a lattice step wide.
        return new PlotBinning(bins, dx, radius * 2);
    }

    /// <summary>Which middle of a row is nearest across — every other row is set half a step along.</summary>
    private static int Across(double x, int row, double dx) =>
        (int)Math.Round((x / dx) - ((row & 1) * 0.5));

    /// <summary>Where a lattice point stands across.</summary>
    private static double Middle(int column, int row, double dx) => (column + ((row & 1) * 0.5)) * dx;

    /// <summary>How far a point is from a lattice point, squared — which is all a comparison needs.</summary>
    private static double Apart(double x, double y, int column, int row, double dx, double dy)
    {
        var across = x - Middle(column, row, dx);
        var up = y - (row * dy);

        return (across * across) + (up * up);
    }
}

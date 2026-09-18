using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Markdown.Plot;

/// <summary>
/// How thickly the points lie, worked out over a grid: the density at each crossing, and where the grid
/// stands in the space the points were given in.
/// </summary>
public sealed record PlotField(double[,] At, double Left, double Bottom, double Wide, double Tall)
{
    /// <summary>How many crossings across and up.</summary>
    public int Across => this.At.GetLength(0);

    public int Up => this.At.GetLength(1);

    /// <summary>The thickest the points lie anywhere on the grid.</summary>
    public double Most
    {
        get
        {
            var most = 0.0;

            foreach (var value in this.At) most = Math.Max(most, value);

            return most;
        }
    }

    /// <summary>Where a crossing stands.</summary>
    public (double X, double Y) Where(int across, int up) =>
        (this.Left + (this.Wide * across / Math.Max(1, this.Across - 1)),
         this.Bottom + (this.Tall * up / Math.Max(1, this.Up - 1)));
}

/// <summary>One closed ring of a contour.</summary>
public sealed record PlotRing(IReadOnlyList<(double X, double Y)> Points);

/// <summary>One level of a contour, and the rings the density makes at it.</summary>
public sealed record PlotLevel(double At, IReadOnlyList<PlotRing> Rings);

/// <summary>
/// A two-dimensional kernel density estimate and the contours of one — what a scatter plot of too many
/// points is drawn as when what a reader wants is the shape of the cloud rather than its members.
///
/// <para>
/// The estimate is MASS's <c>kde2d</c>, which is what ggplot2's <c>stat_density_2d</c> calls: a Gaussian
/// kernel on each point, its width from Silverman's rule as <c>bandwidth.nrd</c> states it. Written out
/// the same way so the numbers can be held against R's rather than against our own opinion of them.
/// </para>
/// <para>
/// WPF-free, so the estimate and the contouring are tested without a desktop.
/// </para>
/// </summary>
public static class PlotDensity
{
    /// <summary>How many crossings a side of the grid has, where nothing says otherwise. ggplot2's own.</summary>
    public const int Crossings = 100;

    /// <summary>
    /// The most crossings times points this will work over before it thins the grid. A block is read on
    /// every keystroke, and an estimate nobody waits for is an estimate nobody sees.
    /// </summary>
    public const long Budget = 40_000_000;

    /// <summary>How many contours are drawn where nothing says otherwise.</summary>
    public const int Levels = 8;

    /// <summary>
    /// The density over a grid covering the points, reaching out past them by three widths of the kernel
    /// so the cloud closes rather than being cut off at the edge.
    /// </summary>
    /// <param name="adjust">What the widths worked out are multiplied by — ggplot2's <c>adjust</c>.</param>
    /// <param name="bandwidth">The widths themselves, where the block would rather say than be told.</param>
    public static PlotField Estimate(IReadOnlyList<(double X, double Y)> points, int crossings = Crossings,
                                     double adjust = 1, (double X, double Y)? bandwidth = null)
    {
        crossings = Math.Clamp(crossings, 8, 512);

        if (points.Count == 0) return new PlotField(new double[crossings, crossings], 0, 0, 1, 1);

        var xs = points.Select(point => point.X).ToArray();
        var ys = points.Select(point => point.Y).ToArray();

        // kde2d takes a quarter of what bandwidth.nrd gives as the kernel's own width.
        var hx = (bandwidth?.X ?? Width(xs)) * adjust / 4;
        var hy = (bandwidth?.Y ?? Width(ys)) * adjust / 4;

        if (!(hx > 0)) hx = 1;
        if (!(hy > 0)) hy = 1;

        var left = xs.Min() - (3 * hx);
        var right = xs.Max() + (3 * hx);
        var bottom = ys.Min() - (3 * hy);
        var top = ys.Max() + (3 * hy);

        // A grid nobody waits for is a grid nobody sees.
        while ((long)crossings * crossings * points.Count > Budget && crossings > 16) crossings /= 2;

        var wide = Math.Max(right - left, 1e-9);
        var tall = Math.Max(top - bottom, 1e-9);

        // Separable, as kde2d is: the kernel over each axis once, then the outer product.
        var ax = new double[crossings, points.Count];
        var ay = new double[crossings, points.Count];

        for (var i = 0; i < crossings; i++)
        {
            var gx = left + (wide * i / (crossings - 1));
            var gy = bottom + (tall * i / (crossings - 1));

            for (var k = 0; k < points.Count; k++)
            {
                ax[i, k] = Bell((gx - xs[k]) / hx);
                ay[i, k] = Bell((gy - ys[k]) / hy);
            }
        }

        var field = new double[crossings, crossings];
        var over = points.Count * hx * hy;

        for (var i = 0; i < crossings; i++)
            for (var j = 0; j < crossings; j++)
            {
                var sum = 0.0;

                for (var k = 0; k < points.Count; k++) sum += ax[i, k] * ay[j, k];

                field[i, j] = sum / over;
            }

        return new PlotField(field, left, bottom, wide, tall);
    }

    /// <summary>
    /// The contours of a field at evenly spaced levels, each a set of closed rings.
    ///
    /// <para>
    /// The grid reaches past the points by three kernel widths, where the density has fallen to almost
    /// nothing, so every ring closes inside it — which is what lets a level be filled as well as drawn.
    /// </para>
    /// </summary>
    public static IReadOnlyList<PlotLevel> Contours(PlotField field, int levels = Levels)
    {
        levels = Math.Clamp(levels, 1, 40);

        var most = field.Most;
        if (!(most > 0)) return [];

        var contours = new List<PlotLevel>();

        for (var which = 1; which <= levels; which++)
        {
            var level = most * which / (levels + 1.0);
            var rings = Rings(field, level);

            if (rings.Count > 0) contours.Add(new PlotLevel(level, rings));
        }

        return contours;
    }

    // ── Marching squares ────────────────────────────────────────────────────

    /// <summary>The rings of one level, stitched from the segments each cell of the grid contributes.</summary>
    private static IReadOnlyList<PlotRing> Rings(PlotField field, double level)
    {
        var segments = new List<((double X, double Y) From, (double X, double Y) To)>();

        for (var i = 0; i < field.Across - 1; i++)
            for (var j = 0; j < field.Up - 1; j++)
                Cell(field, i, j, level, segments);

        return Stitch(segments);
    }

    /// <summary>What one cell of the grid contributes at this level.</summary>
    private static void Cell(PlotField field, int i, int j, double level,
                             List<((double X, double Y) From, (double X, double Y) To)> segments)
    {
        var a = field.At[i, j + 1];
        var b = field.At[i + 1, j + 1];
        var c = field.At[i + 1, j];
        var d = field.At[i, j];

        var which = (a > level ? 8 : 0) | (b > level ? 4 : 0) | (c > level ? 2 : 0) | (d > level ? 1 : 0);
        if (which is 0 or 15) return;

        var topLeft = field.Where(i, j + 1);
        var topRight = field.Where(i + 1, j + 1);
        var bottomRight = field.Where(i + 1, j);
        var bottomLeft = field.Where(i, j);

        var top = Between(topLeft, topRight, a, b, level);
        var right = Between(topRight, bottomRight, b, c, level);
        var bottom = Between(bottomLeft, bottomRight, d, c, level);
        var leftSide = Between(bottomLeft, topLeft, d, a, level);

        switch (which)
        {
            case 1 or 14: segments.Add((leftSide, bottom)); break;
            case 2 or 13: segments.Add((bottom, right)); break;
            case 3 or 12: segments.Add((leftSide, right)); break;
            case 4 or 11: segments.Add((top, right)); break;
            case 6 or 9: segments.Add((top, bottom)); break;
            case 7 or 8: segments.Add((leftSide, top)); break;

            // The two saddles, cut the way the middle of the cell says.
            case 5:
                if ((a + b + c + d) / 4 > level) { segments.Add((leftSide, top)); segments.Add((bottom, right)); }
                else { segments.Add((leftSide, bottom)); segments.Add((top, right)); }

                break;

            case 10:
                if ((a + b + c + d) / 4 > level) { segments.Add((leftSide, bottom)); segments.Add((top, right)); }
                else { segments.Add((leftSide, top)); segments.Add((bottom, right)); }

                break;
        }
    }

    /// <summary>Where the level crosses the edge between two crossings.</summary>
    private static (double X, double Y) Between((double X, double Y) from, (double X, double Y) to,
                                                double at, double onward, double level)
    {
        var apart = onward - at;
        var share = Math.Abs(apart) < 1e-12 ? 0.5 : (level - at) / apart;

        share = Math.Clamp(share, 0, 1);

        return (from.X + ((to.X - from.X) * share), from.Y + ((to.Y - from.Y) * share));
    }

    /// <summary>
    /// The segments joined end to end into rings. Each segment's ends are matched to the nearest others
    /// by rounding them onto a grid finer than the cells, which is enough because every end was worked
    /// out on a cell edge and two cells share one exactly.
    /// </summary>
    private static IReadOnlyList<PlotRing> Stitch(
        List<((double X, double Y) From, (double X, double Y) To)> segments)
    {
        if (segments.Count == 0) return [];

        var ends = new Dictionary<(long, long), List<int>>();
        var used = new bool[segments.Count];

        var fine = Fineness(segments);

        for (var which = 0; which < segments.Count; which++)
        {
            Add(Key(segments[which].From, fine), which);
            Add(Key(segments[which].To, fine), which);
        }

        var rings = new List<PlotRing>();

        for (var which = 0; which < segments.Count; which++)
        {
            if (used[which]) continue;

            var ring = new List<(double X, double Y)> { segments[which].From, segments[which].To };
            used[which] = true;

            var at = segments[which].To;

            // Walk on from the end just reached until the ring closes or runs out of segments.
            while (true)
            {
                var next = Onward(Key(at, fine), used);
                if (next is not { } step) break;

                used[step] = true;

                var far = Near(segments[step].From, at, fine) ? segments[step].To : segments[step].From;

                ring.Add(far);
                at = far;

                if (Near(at, ring[0], fine)) break;
            }

            if (ring.Count >= 3) rings.Add(new PlotRing(ring));
        }

        return rings;

        void Add((long, long) key, int which)
        {
            if (!ends.TryGetValue(key, out var there)) ends[key] = there = [];

            there.Add(which);
        }

        int? Onward((long, long) key, bool[] taken)
        {
            if (!ends.TryGetValue(key, out var there)) return null;

            foreach (var which in there)
                if (!taken[which]) return which;

            return null;
        }
    }

    /// <summary>How finely the ends are matched: far below a cell, and far above rounding error.</summary>
    private static double Fineness(List<((double X, double Y) From, (double X, double Y) To)> segments)
    {
        var reach = 0.0;

        foreach (var (from, to) in segments)
            reach = Math.Max(reach, Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y)));

        return reach > 0 ? reach / 1000 : 1e-9;
    }

    private static (long, long) Key((double X, double Y) at, double fine) =>
        ((long)Math.Round(at.X / fine), (long)Math.Round(at.Y / fine));

    private static bool Near((double X, double Y) one, (double X, double Y) other, double fine) =>
        Key(one, fine) == Key(other, fine);

    // ── The kernel and its width ────────────────────────────────────────────

    /// <summary>The standard normal curve, without the constant a density divides out anyway.</summary>
    private static double Bell(double away) => Math.Exp(-0.5 * away * away) / Math.Sqrt(2 * Math.PI);

    /// <summary>
    /// Silverman's rule as MASS states it in <c>bandwidth.nrd</c>: the narrower of the spread and the
    /// middle half, so one value far out does not widen the whole estimate.
    /// </summary>
    public static double Width(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 1;

                var sorted = values.ToArray();
                Array.Sort(sorted);

        var quarters = (Quantile(sorted, 0.75) - Quantile(sorted, 0.25)) / 1.34;
        var spread = Spread(values);

        var narrower = quarters > 0 ? Math.Min(spread, quarters) : spread;

        return 4 * 1.06 * narrower * Math.Pow(values.Count, -1.0 / 5);
    }

    /// <summary>R's seventh quantile, which is the one <c>quantile()</c> takes by default.</summary>
    private static double Quantile(double[] sorted, double share)
    {
        var at = (sorted.Length - 1) * share;
        var below = (int)Math.Floor(at);
        var above = Math.Min(below + 1, sorted.Length - 1);

        return sorted[below] + ((sorted[above] - sorted[below]) * (at - below));
    }

    private static double Spread(IReadOnlyList<double> values)
    {
        var mean = values.Average();
        var sum = values.Sum(value => (value - mean) * (value - mean));

        return Math.Sqrt(sum / (values.Count - 1));
    }
}

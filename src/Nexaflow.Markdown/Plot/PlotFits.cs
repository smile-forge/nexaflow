using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Markdown.Plot;

/// <summary>
/// A line fitted through the points, and how far the points stray from it.
/// </summary>
/// <param name="Along">The fitted value at each of a run of x, from the lowest to the highest.</param>
/// <param name="Below">The low edge of the confidence band, where one was asked for.</param>
/// <param name="Above">The high edge of it.</param>
public sealed record PlotLine(IReadOnlyList<(double X, double Y)> Along,
                              IReadOnlyList<(double X, double Y)>? Below,
                              IReadOnlyList<(double X, double Y)>? Above);

/// <summary>
/// What the points say about each other: how closely they move together, how much of one is explained
/// by the other, how many there were, and how easily chance alone would have done as well.
/// </summary>
public sealed record PlotCorrelation(double R, double RSquared, int N, double P);

/// <summary>
/// The arithmetic behind a correlation: a straight line or a curve through the points, and the
/// coefficients that say how much the one is worth believing.
///
/// <para>
/// WPF-free, and written as the textbook states it rather than as it is convenient — least squares in
/// closed form, loess as locally weighted regression of the first degree with a tricube weight, and the
/// band from the standard error of the fit at each point. So the numbers can be held against R's.
/// </para>
/// </summary>
public static class PlotFits
{
    /// <summary>How many places along the x a fitted line is worked out at.</summary>
    public const int Along = 80;

    /// <summary>The share of the points a loess fit looks at around each place — R's <c>span</c>.</summary>
    public const double Span = 0.75;

    /// <summary>The confidence a band covers, where nothing says otherwise.</summary>
    public const double Level = 0.95;

    /// <summary>
    /// Least squares: the one straight line through the points that leaves the least squared error.
    /// </summary>
    public static PlotLine? Straight(IReadOnlyList<(double X, double Y)> points, bool band, double level = Level)
    {
        if (points.Count < 2) return null;

        var n = points.Count;
        var meanX = points.Average(point => point.X);
        var meanY = points.Average(point => point.Y);

        var sxx = points.Sum(point => (point.X - meanX) * (point.X - meanX));
        if (!(sxx > 0)) return null;

        var sxy = points.Sum(point => (point.X - meanX) * (point.Y - meanY));

        var slope = sxy / sxx;
        var intercept = meanY - (slope * meanX);

        var along = new List<(double X, double Y)>();
        var below = band ? new List<(double X, double Y)>() : null;
        var above = band ? new List<(double X, double Y)>() : null;

        // How far the points stray from the line, which is what a band is made of.
        var residual = points.Sum(point => Square(point.Y - (intercept + (slope * point.X))));
        var sigma = n > 2 ? Math.Sqrt(residual / (n - 2)) : 0;
        var reaches = Student(n - 2, level);

        var (low, high) = (points.Min(point => point.X), points.Max(point => point.X));

        for (var at = 0; at < Along; at++)
        {
            var x = low + ((high - low) * at / (Along - 1.0));
            var y = intercept + (slope * x);

            along.Add((x, y));

            if (below is null || above is null) continue;

            // The standard error of the fitted value: narrowest at the middle of the points, and
            // widening either side of it, which is why a band is a waist rather than a ribbon.
            var error = sigma * Math.Sqrt((1.0 / n) + (Square(x - meanX) / sxx));

            below.Add((x, y - (reaches * error)));
            above.Add((x, y + (reaches * error)));
        }

        return new PlotLine(along, below, above);
    }

    /// <summary>
    /// Loess: a straight line fitted again at every place, through only the points near it and weighted
    /// by how near — so the curve follows the points without being told what shape to be.
    /// </summary>
    public static PlotLine? Curved(IReadOnlyList<(double X, double Y)> points, bool band,
                                   double span = Span, double level = Level)
    {
        if (points.Count < 4) return Straight(points, band, level);

        var sorted = points.OrderBy(point => point.X).ToList();
        var near = Math.Max(2, (int)Math.Round(Math.Clamp(span, 0.05, 1) * sorted.Count));

        var along = new List<(double X, double Y)>();
        var below = band ? new List<(double X, double Y)>() : null;
        var above = band ? new List<(double X, double Y)>() : null;

        var (low, high) = (sorted[0].X, sorted[^1].X);
        var reaches = Student(sorted.Count - 2, level);

        for (var at = 0; at < Along; at++)
        {
            var x = low + ((high - low) * at / (Along - 1.0));

            // The points nearest this place, and how far away the furthest of them is.
            var window = sorted.OrderBy(point => Math.Abs(point.X - x)).Take(near).ToList();
            var reach = window.Max(point => Math.Abs(point.X - x));

            double sw = 0, swx = 0, swy = 0, swxx = 0, swxy = 0;

            foreach (var point in window)
            {
                var weight = Tricube(reach > 0 ? Math.Abs(point.X - x) / reach : 0);

                sw += weight;
                swx += weight * point.X;
                swy += weight * point.Y;
                swxx += weight * point.X * point.X;
                swxy += weight * point.X * point.Y;
            }

            if (!(sw > 0)) continue;

            var denominator = (sw * swxx) - (swx * swx);

            var y = Math.Abs(denominator) < 1e-12
                ? swy / sw
                : ((((swxx * swy) - (swx * swxy)) / denominator)
                   + ((((sw * swxy) - (swx * swy)) / denominator) * x));

            along.Add((x, y));

            if (below is null || above is null) continue;

            // The spread of the points this place was fitted from, weighted as they were.
            var spread = 0.0;
            foreach (var point in window)
            {
                var weight = Tricube(reach > 0 ? Math.Abs(point.X - x) / reach : 0);
                spread += weight * Square(point.Y - y);
            }

            var error = Math.Sqrt(spread / sw) / Math.Sqrt(Math.Max(1, window.Count));

            below.Add((x, y - (reaches * error)));
            above.Add((x, y + (reaches * error)));
        }

        return along.Count < 2 ? null : new PlotLine(along, below, above);
    }

    // ── How closely they move together ──────────────────────────────────────

    /// <summary>Pearson's r: how closely the two move together in a straight line.</summary>
    public static PlotCorrelation? Pearson(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count < 3) return null;

        var meanX = points.Average(point => point.X);
        var meanY = points.Average(point => point.Y);

        var sxx = points.Sum(point => Square(point.X - meanX));
        var syy = points.Sum(point => Square(point.Y - meanY));

        if (!(sxx > 0) || !(syy > 0)) return null;

        var r = points.Sum(point => (point.X - meanX) * (point.Y - meanY)) / Math.Sqrt(sxx * syy);
        r = Math.Clamp(r, -1, 1);

        return new PlotCorrelation(r, r * r, points.Count, Chance(r, points.Count));
    }

    /// <summary>
    /// Spearman's rho: Pearson's r over the ranks rather than the values, so it answers whether the two
    /// move together at all rather than whether they do so in a straight line.
    /// </summary>
    public static PlotCorrelation? Spearman(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count < 3) return null;

        var ranked = Ranks(points.Select(point => point.X).ToList())
            .Zip(Ranks(points.Select(point => point.Y).ToList()), (x, y) => (X: x, Y: y))
            .ToList();

        return Pearson(ranked);
    }

    /// <summary>
    /// Kendall's tau-b: of every pair of points, how many agree about which way round they are, less how
    /// many disagree — and ties counted as neither.
    /// </summary>
    public static PlotCorrelation? Kendall(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count < 3) return null;

        long agree = 0, differ = 0, tiedX = 0, tiedY = 0;

        for (var one = 0; one < points.Count; one++)
            for (var other = one + 1; other < points.Count; other++)
            {
                var acrossWay = Math.Sign(points[one].X - points[other].X);
                var upWay = Math.Sign(points[one].Y - points[other].Y);

                if (acrossWay == 0 && upWay == 0) continue;
                if (acrossWay == 0) { tiedX++; continue; }
                if (upWay == 0) { tiedY++; continue; }

                if (acrossWay == upWay) agree++;
                else differ++;
            }

        var pairs = Math.Sqrt((double)(agree + differ + tiedX) * (agree + differ + tiedY));
        if (!(pairs > 0)) return null;

        var tau = Math.Clamp((agree - differ) / pairs, -1, 1);

        return new PlotCorrelation(tau, tau * tau, points.Count, Chance(tau, points.Count));
    }

    /// <summary>What a method is called, which is how a block asks for one.</summary>
    public static PlotCorrelation? Of(PlotMethod method, IReadOnlyList<(double X, double Y)> points) => method switch
    {
        PlotMethod.Spearman => Spearman(points),
        PlotMethod.Kendall => Kendall(points),
        _ => Pearson(points),
    };

    // ── The arithmetic underneath ───────────────────────────────────────────

    private static double Square(double value) => value * value;

    /// <summary>The tricube weight: one at the middle, nothing at the edge, and flat at both.</summary>
    private static double Tricube(double away)
    {
        away = Math.Clamp(Math.Abs(away), 0, 1);
        var left = 1 - (away * away * away);

        return left * left * left;
    }

    /// <summary>The ranks of a set of values, ties sharing the rank they average to.</summary>
    private static IReadOnlyList<double> Ranks(IReadOnlyList<double> values)
    {
        var order = Enumerable.Range(0, values.Count).OrderBy(at => values[at]).ToList();
        var ranks = new double[values.Count];

        for (var at = 0; at < order.Count;)
        {
            var same = at;
            while (same + 1 < order.Count && values[order[same + 1]] == values[order[at]]) same++;

            var shared = ((at + same) / 2.0) + 1;
            for (var which = at; which <= same; which++) ranks[order[which]] = shared;

            at = same + 1;
        }

        return ranks;
    }

    /// <summary>
    /// How easily chance alone would have given a correlation this strong, two-tailed — from the t
    /// statistic r * sqrt((n - 2) / (1 - r^2)).
    /// </summary>
    private static double Chance(double r, int n)
    {
        if (n < 3) return 1;
        if (Math.Abs(r) >= 1) return 0;

        var t = Math.Abs(r) * Math.Sqrt((n - 2) / (1 - (r * r)));

        return Math.Clamp(2 * (1 - StudentTail(t, n - 2)), 0, 1);
    }

    /// <summary>How much of Student's t lies below <paramref name="t"/>, by the incomplete beta.</summary>
    private static double StudentTail(double t, int freedom)
    {
        var x = freedom / (freedom + (t * t));

        return 1 - (0.5 * Beta(x, freedom / 2.0, 0.5));
    }

    /// <summary>
    /// How far a value has to reach from the middle of Student's t to cover a share of it — found by
    /// halving, because a closed form for it is a great deal of arithmetic for a number drawn once.
    /// </summary>
    private static double Student(int freedom, double level)
    {
        if (freedom < 1) return 0;

        var want = 1 - ((1 - Math.Clamp(level, 0.5, 0.999)) / 2);

        double low = 0, high = 200;

        for (var which = 0; which < 80; which++)
        {
            var middle = (low + high) / 2;

            if (StudentTail(middle, freedom) < want) low = middle;
            else high = middle;
        }

        return (low + high) / 2;
    }

    /// <summary>The regularised incomplete beta, by its continued fraction.</summary>
    private static double Beta(double x, double a, double b)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;

        var front = Math.Exp((LogGamma(a + b) - LogGamma(a) - LogGamma(b))
                             + (a * Math.Log(x)) + (b * Math.Log(1 - x)));

        return x < (a + 1) / (a + b + 2)
            ? front * Fraction(x, a, b) / a
            : 1 - (Math.Exp((LogGamma(a + b) - LogGamma(a) - LogGamma(b))
                            + (b * Math.Log(1 - x)) + (a * Math.Log(x))) * Fraction(1 - x, b, a) / b);
    }

    /// <summary>Lentz's method for the beta's continued fraction.</summary>
    private static double Fraction(double x, double a, double b)
    {
        const double Tiny = 1e-30;

        double c = 1, d = 1 - ((a + b) * x / (a + 1));
        if (Math.Abs(d) < Tiny) d = Tiny;

        d = 1 / d;
        var held = d;

        for (var m = 1; m <= 300; m++)
        {
            var two = 2 * m;

            var numerator = m * (b - m) * x / ((a + two - 1) * (a + two));
            d = 1 + (numerator * d);
            if (Math.Abs(d) < Tiny) d = Tiny;

            c = 1 + (numerator / c);
            if (Math.Abs(c) < Tiny) c = Tiny;

            d = 1 / d;
            held *= d * c;

            numerator = -(a + m) * (a + b + m) * x / ((a + two) * (a + two + 1));
            d = 1 + (numerator * d);
            if (Math.Abs(d) < Tiny) d = Tiny;

            c = 1 + (numerator / c);
            if (Math.Abs(c) < Tiny) c = Tiny;

            d = 1 / d;

            var step = d * c;
            held *= step;

            if (Math.Abs(1 - step) < 1e-12) break;
        }

        return held;
    }

    /// <summary>Lanczos's approximation to the log of the gamma function.</summary>
    private static double LogGamma(double x)
    {
        double[] table =
        [
            76.18009172947146, -86.50532032941677, 24.01409824083091,
            -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5,
        ];

        var y = x;
        var tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);

        var series = 1.000000000190015;
        foreach (var term in table) series += term / ++y;

        return -tmp + Math.Log(2.5066282746310005 * series / x);
    }
}

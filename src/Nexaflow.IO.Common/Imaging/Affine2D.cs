namespace Nexaflow.IO.Common.Imaging;

/// <summary>
/// A 2D affine map, <c>x' = A·x + B·y + C</c>, <c>y' = D·x + E·y + F</c> — the 2×3 matrix OpenCV's warps take, row by row.
/// </summary>
public readonly record struct Affine2D(double A, double B, double C, double D, double E, double F)
{
    public static Affine2D Identity => new(1, 0, 0, 0, 1, 0);

    /// <summary>Where it takes the point <paramref name="x"/>, <paramref name="y"/>.</summary>
    public (double X, double Y) Map(double x, double y) => (A * x + B * y + C, D * x + E * y + F);

    /// <summary>The map that undoes this one, as OpenCV's <c>invertAffineTransform</c> makes it.</summary>
    public Affine2D Inverse()
    {
        double det = A * E - B * D;
        if (det == 0) throw new InvalidOperationException("A map that flattens the plane has no inverse.");
        double ia = E / det, ib = -B / det, id = -D / det, ie = A / det;
        return new(ia, ib, -(ia * C + ib * F), id, ie, -(id * C + ie * F));
    }

    /// <summary>Every entry multiplied by <paramref name="factor"/>, translation included — the map onto a picture
    /// <paramref name="factor"/> times the size.</summary>
    public Affine2D Scaled(double factor) => new(A * factor, B * factor, C * factor, D * factor, E * factor, F * factor);

    /// <summary>The same map, then moved by <paramref name="dx"/>, <paramref name="dy"/>.</summary>
    public Affine2D Moved(double dx, double dy) => this with { C = C + dx, F = F + dy };

    /// <summary>
    /// The rotation, uniform scale and translation that best takes <paramref name="from"/> onto <paramref name="to"/>, as
    /// OpenCV's <c>estimateAffinePartial2D</c> finds it with least-median-of-squares: the pair of points whose map leaves
    /// the smallest median squared error, the points that map within 2.5 robust standard deviations of it, then the least
    /// squares map over those. Every pair is tried where OpenCV draws a few at random, so a pair it may miss is never missed.
    /// </summary>
    public static Affine2D Similarity(IReadOnlyList<(double X, double Y)> from, IReadOnlyList<(double X, double Y)> to)
    {
        int n = from.Count;
        if (n != to.Count || n < 2) throw new ArgumentException("A similarity needs two or more pairs of points.");
        if (n == 2) return LeastSquares(from, to, [0, 1]);

        double bestMedian = double.MaxValue;
        Affine2D best = Identity;
        var errors = new double[n];
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (Math.Abs(from[i].X - from[j].X) + Math.Abs(from[i].Y - from[j].Y) < 1e-9) continue;   // no direction to fit
                var model = LeastSquares(from, to, [i, j]);
                for (int k = 0; k < n; k++) errors[k] = SquaredError(model, from[k], to[k]);
                double median = Median(errors);
                if (median < bestMedian) (bestMedian, best) = (median, model);
            }
        if (bestMedian == double.MaxValue) throw new ArgumentException("The points do not span a direction.");

        // OpenCV's LMeDS threshold: 2.5 robust standard deviations, from the median, never under a thousandth of a pixel.
        double sigma = Math.Max(2.5 * 1.4826 * (1 + 5.0 / (n - 2)) * Math.Sqrt(bestMedian), 0.001);
        var inliers = new List<int>(n);
        for (int k = 0; k < n; k++)
            if (SquaredError(best, from[k], to[k]) <= sigma * sigma) inliers.Add(k);
        return inliers.Count >= 2 ? LeastSquares(from, to, inliers) : best;
    }

    private static double SquaredError(Affine2D m, (double X, double Y) from, (double X, double Y) to)
    {
        var (x, y) = m.Map(from.X, from.Y);
        return (x - to.X) * (x - to.X) + (y - to.Y) * (y - to.Y);
    }

    private static double Median(double[] values)
    {
        // OpenCV takes the element at count/2 of the sorted errors.
        var sorted = (double[])values.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    /// <summary>The similarity <c>x' = a·x − b·y + tx</c>, <c>y' = b·x + a·y + ty</c> minimising the squared error over
    /// <paramref name="use"/>: linear in its four parameters, so solved exactly.</summary>
    private static Affine2D LeastSquares(IReadOnlyList<(double X, double Y)> from, IReadOnlyList<(double X, double Y)> to, IReadOnlyList<int> use)
    {
        double mx = 0, my = 0, ux = 0, uy = 0;
        foreach (int k in use) { mx += from[k].X; my += from[k].Y; ux += to[k].X; uy += to[k].Y; }
        mx /= use.Count; my /= use.Count; ux /= use.Count; uy /= use.Count;
        double sxx = 0, a = 0, b = 0;
        foreach (int k in use)
        {
            double x = from[k].X - mx, y = from[k].Y - my, u = to[k].X - ux, v = to[k].Y - uy;
            sxx += x * x + y * y;
            a += x * u + y * v;
            b += x * v - y * u;
        }
        a /= sxx;
        b /= sxx;
        return new(a, -b, ux - (a * mx - b * my), b, a, uy - (b * mx + a * my));
    }
}

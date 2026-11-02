namespace Nexaflow.Features.Images.Editing.Selection;

/// <summary>
/// The refinements a selection is put through, over one rectangle of coverage a byte a pixel, row by row. Each costs the
/// same whatever its radius — the blur three running boxes, growing and shrinking one exact distance transform — so a
/// wide feather on a large photo takes no longer than a narrow one. What lies past the rectangle is read as its edge.
/// </summary>
internal static class CoverageFilters
{
    /// <summary>Where a pixel counts as inside: half covered or more.</summary>
    private const byte Half = 128;

    /// <summary>The coverage blurred by a Gaussian of spread <paramref name="sigma"/>, made of three box blurs.</summary>
    public static void Blur(byte[] pixels, int width, int height, double sigma)
    {
        if (sigma <= 0) return;
        var across = new byte[pixels.Length];
        foreach (int radius in BoxesFor(sigma))
        {
            if (radius == 0) continue;
            BoxRows(pixels, across, width, height, radius);
            BoxColumns(across, pixels, width, height, radius);
        }
    }

    /// <summary>Every pixel within <paramref name="by"/> of an inside one covered too, its edge anti-aliased; nothing
    /// already covered is uncovered.</summary>
    public static void Grow(byte[] pixels, int width, int height, double by)
    {
        var distance = DistanceTo(pixels, width, height, inside: true);
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Math.Max(pixels[i], Ramp(by + 0.5 - distance[i]));
    }

    /// <summary>Every pixel within <paramref name="by"/> of an outside one uncovered too, its edge anti-aliased; nothing
    /// is covered more than it was.</summary>
    public static void Shrink(byte[] pixels, int width, int height, double by)
    {
        var distance = DistanceTo(pixels, width, height, inside: false);
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Math.Min(pixels[i], Ramp(distance[i] - by - 0.5));
    }

    /// <summary>Blurred by <paramref name="sigma"/>, cut at half to a crisp shape — which takes off what is smaller than
    /// the blur — and given back an anti-aliased edge.</summary>
    public static void Smooth(byte[] pixels, int width, int height, double sigma)
    {
        Blur(pixels, width, height, sigma);
        for (int i = 0; i < pixels.Length; i++) pixels[i] = pixels[i] >= Half ? (byte)255 : (byte)0;
        Blur(pixels, width, height, EdgeSigma);
    }

    /// <summary>The blur that gives a cut edge back its anti-aliasing: about a pixel across.</summary>
    private const double EdgeSigma = 0.6;

    /// <summary>0…1 as 0…255, clamped.</summary>
    private static byte Ramp(double share) => share <= 0 ? (byte)0 : share >= 1 ? (byte)255 : (byte)Math.Round(share * 255);

    // ── Box blur ────────────────────────────────────────────────────────────

    /// <summary>The radii of three box blurs that together spread as a Gaussian of <paramref name="sigma"/> does.</summary>
    internal static int[] BoxesFor(double sigma)
    {
        const int passes = 3;
        double ideal = Math.Sqrt(12 * sigma * sigma / passes + 1);
        int lower = (int)Math.Floor(ideal);
        if (lower % 2 == 0) lower--;
        int upper = lower + 2;
        double m = Math.Round((12 * sigma * sigma - passes * lower * lower - 4 * passes * lower - 3 * passes) / (-4 * lower - 4));
        var radii = new int[passes];
        for (int i = 0; i < passes; i++) radii[i] = ((i < m ? lower : upper) - 1) / 2;
        return radii;
    }

    private static void BoxRows(byte[] from, byte[] into, int width, int height, int radius)
    {
        int span = 2 * radius + 1;
        Parallel.For(0, height, y =>
        {
            int row = y * width;
            int sum = 0;
            for (int x = -radius; x <= radius; x++) sum += from[row + Math.Clamp(x, 0, width - 1)];
            for (int x = 0; x < width; x++)
            {
                into[row + x] = (byte)((sum + span / 2) / span);
                sum += from[row + Math.Min(x + radius + 1, width - 1)] - from[row + Math.Max(x - radius, 0)];
            }
        });
    }

    private static void BoxColumns(byte[] from, byte[] into, int width, int height, int radius)
    {
        int span = 2 * radius + 1;
        Parallel.For(0, width, x =>
        {
            int sum = 0;
            for (int y = -radius; y <= radius; y++) sum += from[Math.Clamp(y, 0, height - 1) * width + x];
            for (int y = 0; y < height; y++)
            {
                into[y * width + x] = (byte)((sum + span / 2) / span);
                sum += from[Math.Min(y + radius + 1, height - 1) * width + x] - from[Math.Max(y - radius, 0) * width + x];
            }
        });
    }

    // ── Distance ────────────────────────────────────────────────────────────

    /// <summary>
    /// How far each pixel is from the nearest pixel inside the coverage (<paramref name="inside"/>) or outside it, in
    /// pixels — 0 for one that is itself — by the exact Euclidean distance transform of Felzenszwalb and Huttenlocher:
    /// down every column, then along every row. Where there is no such pixel at all, the distance is past any radius.
    /// </summary>
    internal static float[] DistanceTo(byte[] pixels, int width, int height, bool inside)
    {
        var squared = new float[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
            squared[i] = (pixels[i] >= Half) == inside ? 0 : Far;

        Parallel.For(0, width, () => new Lines(height), (x, _, lines) =>
        {
            for (int y = 0; y < height; y++) lines.Values[y] = squared[y * width + x];
            lines.Transform(height);
            for (int y = 0; y < height; y++) squared[y * width + x] = lines.Distances[y];
            return lines;
        }, _ => { });
        Parallel.For(0, height, () => new Lines(width), (y, _, lines) =>
        {
            squared.AsSpan(y * width, width).CopyTo(lines.Values);
            lines.Transform(width);
            lines.Distances.AsSpan(0, width).CopyTo(squared.AsSpan(y * width, width));
            return lines;
        }, _ => { });

        for (int i = 0; i < squared.Length; i++) squared[i] = MathF.Sqrt(squared[i]);
        return squared;
    }

    /// <summary>Squared distance standing in for none at all: past any canvas.</summary>
    private const float Far = 1e20f;

    /// <summary>One line's working space for the one-dimensional transform, reused along a thread's lines.</summary>
    private sealed class Lines(int length)
    {
        public readonly float[] Values = new float[length];
        public readonly float[] Distances = new float[length];
        private readonly int[] _parabolas = new int[length];
        private readonly float[] _bounds = new float[length + 1];

        /// <summary>The lower envelope of the parabolas rooted at each value, sampled at each place.</summary>
        public void Transform(int n)
        {
            int k = 0;
            _parabolas[0] = 0;
            _bounds[0] = float.NegativeInfinity;
            _bounds[1] = float.PositiveInfinity;
            for (int q = 1; q < n; q++)
            {
                float s = Meet(q, _parabolas[k]);
                while (s <= _bounds[k])
                {
                    k--;
                    s = Meet(q, _parabolas[k]);
                }
                k++;
                _parabolas[k] = q;
                _bounds[k] = s;
                _bounds[k + 1] = float.PositiveInfinity;
            }
            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (_bounds[k + 1] < q) k++;
                int p = _parabolas[k];
                Distances[q] = (q - p) * (float)(q - p) + Values[p];
            }
        }

        private float Meet(int q, int p) => ((Values[q] + (float)q * q) - (Values[p] + (float)p * p)) / (2f * q - 2f * p);
    }
}

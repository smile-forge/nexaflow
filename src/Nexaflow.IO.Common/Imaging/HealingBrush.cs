namespace Nexaflow.IO.Common.Imaging;

/// <summary>
/// Heals what a mask covers with texture from elsewhere in the picture, as a healing brush does — no neural network: the
/// pixels a fixed offset away copied in, then their colour and light made to meet what is round the mask, so the copied
/// texture takes on the place it lands in (Pérez, Gangnet and Blake's Poisson image editing; Georgiev's healing brush).
/// Inside the mask the result keeps the copied pixels' differences one from the next and meets the picture at the mask's
/// edge: it solves Δf = Δg there with f the picture round it, g the copied pixels.
/// </summary>
public static class HealingBrush
{
    /// <summary>How far the solve goes: until each value's residual averages this much of a level.</summary>
    private const double Tolerance = 1e-3;

    /// <summary>
    /// <paramref name="rgba"/> — interleaved 8-bit RGBA, row by row — with what <paramref name="mask"/> covers (one byte a
    /// pixel, healed where it is not 0) healed from the pixels (<paramref name="offsetX"/>, <paramref name="offsetY"/>) away,
    /// a source past the picture's edge taken from the edge; every other pixel, and the alpha of all, as it was.
    /// </summary>
    public static byte[] Heal(ReadOnlySpan<byte> rgba, int width, int height, ReadOnlySpan<byte> mask, int offsetX, int offsetY,
                              CancellationToken ct = default)
    {
        Require(rgba.Length, width, height, mask.Length);
        var source = new float[rgba.Length];
        for (int i = 0; i < source.Length; i++) source[i] = rgba[i];
        var healed = Healed(source, width, height, mask, offsetX, offsetY, rounds: true, ct);
        var result = new byte[rgba.Length];
        for (int i = 0; i < result.Length; i++) result[i] = (byte)healed[i];
        return result;
    }

    /// <summary>
    /// <paramref name="rgba"/> — interleaved red, green, blue and alpha, not premultiplied, 1 white, as deep as floats hold —
    /// healed as the 8-bit brush heals, never rounded to its steps, and light past white kept.
    /// </summary>
    public static float[] Heal(ReadOnlySpan<float> rgba, int width, int height, ReadOnlySpan<byte> mask, int offsetX, int offsetY,
                               CancellationToken ct = default)
    {
        Require(rgba.Length, width, height, mask.Length);
        // Solved at the 8-bit brush's scale, which its tolerance is counted in.
        var source = new float[rgba.Length];
        for (int i = 0; i < source.Length; i++) source[i] = rgba[i] * 255;
        var healed = Healed(source, width, height, mask, offsetX, offsetY, rounds: false, ct);
        for (int i = 0; i < healed.Length; i++) healed[i] /= 255;
        return healed;
    }

    private static void Require(int length, int width, int height, int maskLength)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "A picture has a size.");
        if (length != width * height * 4) throw new ArgumentException("The pixels are not the size given.", "rgba");
        if (maskLength != width * height) throw new ArgumentException("The mask is not the picture's size.", "mask");
    }

    /// <summary><paramref name="source"/> — four values a pixel, 0 to 255 white — healed into a copy; where
    /// <paramref name="rounds"/>, each healed value is rounded to a whole step and clamped, as an 8-bit picture holds it.</summary>
    private static float[] Healed(float[] source, int width, int height, ReadOnlySpan<byte> mask, int offsetX, int offsetY, bool rounds,
                                  CancellationToken ct)
    {
        var result = (float[])source.Clone();
        var index = new int[width * height];
        var inside = new List<int>();
        for (int p = 0; p < mask.Length; p++)
        {
            index[p] = mask[p] != 0 ? inside.Count : -1;
            if (mask[p] != 0) inside.Add(p);
        }
        if (inside.Count == 0) return result;

        var system = new Membrane(width, height, index, [.. inside]);
        for (int c = 0; c < 3; c++)
        {
            ct.ThrowIfCancellationRequested();
            int channel = c;
            float Picture(int p) => source[p * 4 + channel];
            float Copied(int p)
            {
                int x = Math.Clamp(p % width + offsetX, 0, width - 1), y = Math.Clamp(p / width + offsetY, 0, height - 1);
                return source[(y * width + x) * 4 + channel];
            }
            var correction = system.Solve(n => Picture(n) - Copied(n), ct);
            for (int i = 0; i < system.Count; i++)
            {
                int p = system.Inside[i];
                double value = Copied(p) + correction[i];
                result[p * 4 + c] = rounds ? (float)Math.Clamp(Math.Round(value), 0, 255) : (float)value;
            }
        }
        return result;
    }

    /// <summary>
    /// The membrane over the mask: the smooth correction h — Δh = 0 inside — that takes each pixel just outside the mask
    /// to the value given for it, the copied texture plus h then meeting the picture there. Solved by conjugate gradients,
    /// each pixel's equation divided by its count of neighbours.
    /// </summary>
    private sealed class Membrane
    {
        public readonly int[] Inside;
        private readonly int[] _degree;        // each pixel's neighbours in the picture: past its edge the membrane is free
        private readonly int[] _around;        // each pixel's four neighbours inside the mask, by their index there; -1 where none
        private readonly List<(int At, int Pixel)> _edge = [];   // each pixel just outside the mask, by the inside pixel beside it

        public Membrane(int width, int height, int[] index, int[] inside)
        {
            Inside = inside;
            _degree = new int[inside.Length];
            _around = new int[inside.Length * 4];
            Array.Fill(_around, -1);
            for (int i = 0; i < inside.Length; i++)
            {
                int p = inside[i], x = p % width, y = p / width, k = 0;
                Span<int> beside = [x > 0 ? p - 1 : -1, x < width - 1 ? p + 1 : -1, y > 0 ? p - width : -1, y < height - 1 ? p + width : -1];
                foreach (int q in beside)
                {
                    if (q < 0) continue;
                    _degree[i]++;
                    if (index[q] >= 0) _around[i * 4 + k++] = index[q];
                    else _edge.Add((i, q));
                }
            }
        }

        public int Count => Inside.Length;

        /// <param name="edge">The value h takes at a pixel just outside the mask.</param>
        public double[] Solve(Func<int, float> edge, CancellationToken ct)
        {
            int n = Count;
            var h = new double[n];
            if (_edge.Count == 0) return h;   // the mask is the whole picture: nothing round it to meet
            var b = new double[n];
            foreach (var (at, pixel) in _edge) b[at] += edge(pixel);

            // Jacobi-preconditioned conjugate gradients on  degree·h_p − Σ h_n (n inside) = b_p.
            var r = (double[])b.Clone();
            var z = new double[n];
            for (int i = 0; i < n; i++) z[i] = r[i] / _degree[i];
            var d = (double[])z.Clone();
            var ad = new double[n];
            double rz = Dot(r, z), target = Tolerance * Tolerance * n;
            for (int iteration = 0; iteration < 20 * (int)Math.Sqrt(n) + 200 && Dot(r, r) > target; iteration++)
            {
                if (iteration % 32 == 0) ct.ThrowIfCancellationRequested();
                Apply(d, ad);
                double alpha = rz / Dot(d, ad);
                for (int i = 0; i < n; i++) (h[i], r[i]) = (h[i] + alpha * d[i], r[i] - alpha * ad[i]);
                for (int i = 0; i < n; i++) z[i] = r[i] / _degree[i];
                double next = Dot(r, z), beta = next / rz;
                rz = next;
                for (int i = 0; i < n; i++) d[i] = z[i] + beta * d[i];
            }
            return h;
        }

        private void Apply(double[] v, double[] into)
            => Parallel.For(0, Count, i =>
            {
                double sum = _degree[i] * v[i];
                for (int k = 0; k < 4; k++)
                    if (_around[i * 4 + k] is var j and >= 0) sum -= v[j];
                into[i] = sum;
            });

        private static double Dot(double[] a, double[] b)
        {
            double sum = 0;
            for (int i = 0; i < a.Length; i++) sum += a[i] * b[i];
            return sum;
        }
    }
}

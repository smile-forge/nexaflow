using Nexaflow.Features.Images.Editing.Recipe;

namespace Nexaflow.Features.Images.Editing.SceneDepth;

/// <summary>
/// A lens's blur drawn from a picture's scene depth: every part blurred by its own circle of confusion — a thin lens's,
/// which grows with how far the part's disparity is from the focus's — into the aperture's shape, the parts in front of the
/// focus spreading over what is behind them and the parts behind it never over what is in front.
/// </summary>
/// <remarks>
/// <para>The picture is worked in premultiplied linear light, so a highlight spreads into a bright disc as through a lens. Each
/// pixel's blur radius falls between two of a ladder of radii, one a third or so wider than the last, and the pixel is shared
/// between the two by how near it is to each — so the blur grows smoothly with depth, never in bands. Each rung on each side of
/// the focus is a layer: its pixels' light and coverage, blurred at the rung's radius through the aperture's shape, then laid
/// over the rungs behind it, farthest first. Light and coverage are laid down together and the light is divided by the
/// coverage at the end, so a pixel shared between two rungs keeps its light, the edge of a blurred layer never leaves a dark
/// seam, and what lay behind a sharp subject is filled from round it rather than with the subject's colour.</para>
/// <para>A rung's blur gathers each pixel from the rows of the aperture's shape, each row's span summed from a running total
/// of its row, so it costs the shape's height rather than its area. Past a few pixels' radius the layer is gathered at a
/// fraction of its size — a blur that wide holds no finer detail — and brought back up bilinearly.</para>
/// </remarks>
public static class LensBlur
{
    /// <summary>The radii below this are worked at full size; above, the layer is reduced to keep the shape this wide.</summary>
    private const float FullSizeRadius = 4;

    /// <summary>How much wider each rung of radii is than the last, past the first few whole pixels.</summary>
    private const float RungGrowth = 1.3f;

    // Planar channels a layer is blurred in: premultiplied red, green, blue, alpha, and membership.
    private const int Channels = 5;

    /// <summary>
    /// <paramref name="rgba"/> — premultiplied linear light, four values a pixel — blurred as a lens focused at nearness
    /// <paramref name="focus"/> blurs it: sharp within <paramref name="range"/> of it, then by <paramref name="maxRadius"/> pixels
    /// for each unit of nearness further, through <paramref name="shape"/>.
    /// </summary>
    /// <param name="nearness">Each pixel's nearness (<see cref="SceneDepthPlane"/>), 1 nearest.</param>
    public static float[] Apply(float[] rgba, float[] nearness, int width, int height, float focus, float range, float maxRadius, BokehShape shape,
                                CancellationToken ct = default)
    {
        int count = width * height;
        if (rgba.Length != count * 4 || nearness.Length != count) throw new ArgumentException("The picture and its depth are not the size given.");
        if (maxRadius < 0.5f) return (float[])rgba.Clone();

        var rungs = Rungs(maxRadius);
        int top = rungs.Length - 1, layers = 2 * top + 1;
        // Each pixel's two shares, by layer: layer `top` is the focus's rung, below it the rungs behind the focus from the widest
        // in, above it the rungs in front of it from the narrowest out — the order they are laid down in, farthest first.
        var firstLayer = new short[count];
        var firstShare = new float[count];
        var secondLayer = new short[count];
        Parallel.For(0, height, y =>
        {
            for (int i = y * width, end = i + width; i < end; i++)
            {
                float offset = nearness[i] - focus;
                float radius = Math.Max(0, Math.Abs(offset) - range) * maxRadius;
                int k = Rung(rungs, radius);
                float toNext = k < top ? (radius - rungs[k]) / (rungs[k + 1] - rungs[k]) : 0;
                int towards = offset > 0 ? 1 : -1;
                firstLayer[i] = (short)(top + towards * k);
                firstShare[i] = 1 - toNext;
                secondLayer[i] = (short)(k < top ? top + towards * (k + 1) : top + towards * k);
            }
        });

        // Where each layer has pixels, so each is blurred over only that and its reach.
        var bounds = new (int Left, int Top, int Right, int Bottom)[layers];
        Array.Fill(bounds, (width, height, -1, -1));
        var gate = new Lock();
        Parallel.For(0, height, () => NewBounds(layers, width, height), (y, _, local) =>
        {
            for (int x = 0, i = y * width; x < width; x++, i++)
            {
                Grow(ref local[firstLayer[i]], x, y);
                if (firstShare[i] < 1) Grow(ref local[secondLayer[i]], x, y);
            }
            return local;
        }, local =>
        {
            lock (gate)
                for (int l = 0; l < layers; l++)
                    if (local[l].Right >= 0)
                    {
                        Grow(ref bounds[l], local[l].Left, local[l].Top);
                        Grow(ref bounds[l], local[l].Right, local[l].Bottom);
                    }
        });

        var light = new float[count * 4];
        var cover = new float[count];
        for (int layer = 0; layer < layers; layer++)
        {
            ct.ThrowIfCancellationRequested();
            if (bounds[layer].Right >= 0) Lay(layer, rungs[Math.Abs(layer - top)], bounds[layer]);
        }

        var result = new float[count * 4];
        Parallel.For(0, height, y =>
        {
            for (int i = y * width, end = i + width; i < end; i++)
            {
                float u = cover[i];
                if (u <= 1e-6f) continue;
                for (int c = 0; c < 4; c++) result[i * 4 + c] = light[i * 4 + c] / u;
            }
        });
        return result;

        // One layer: its pixels' shares of light and coverage, blurred at its rung's radius and laid over what is behind it.
        void Lay(int layer, float radius, (int Left, int Top, int Right, int Bottom) has)
        {
            int reach = (int)MathF.Ceiling(radius) + 1;
            int x0 = Math.Max(0, has.Left - reach), y0 = Math.Max(0, has.Top - reach);
            int x1 = Math.Min(width, has.Right + 1 + reach), y1 = Math.Min(height, has.Bottom + 1 + reach);
            int w0 = x1 - x0, h0 = y1 - y0, plane = w0 * h0;

            var planes = new float[Channels * plane];
            Parallel.For(0, h0, row =>
            {
                for (int col = 0; col < w0; col++)
                {
                    int i = (row + y0) * width + col + x0, j = row * w0 + col;
                    float w = (firstLayer[i] == layer ? firstShare[i] : 0) + (secondLayer[i] == layer && firstLayer[i] != layer ? 1 - firstShare[i] : 0);
                    if (w <= 0) continue;
                    for (int c = 0; c < 4; c++) planes[c * plane + j] = rgba[i * 4 + c] * w;
                    planes[4 * plane + j] = w;
                }
            });
            var blurred = radius < 0.5f ? planes : Blur(planes, w0, h0, radius, shape);

            Parallel.For(0, h0, row =>
            {
                for (int col = 0; col < w0; col++)
                {
                    int i = (row + y0) * width + col + x0, j = row * w0 + col;
                    float us = blurred[4 * plane + j];
                    if (us <= 0) continue;
                    float keep = 1 - Math.Min(1, us);
                    for (int c = 0; c < 4; c++) light[i * 4 + c] = blurred[c * plane + j] + light[i * 4 + c] * keep;
                    cover[i] = us + cover[i] * keep;
                }
            });
        }
    }

    private static (int Left, int Top, int Right, int Bottom)[] NewBounds(int layers, int width, int height)
    {
        var bounds = new (int, int, int, int)[layers];
        Array.Fill(bounds, (width, height, -1, -1));
        return bounds;
    }

    private static void Grow(ref (int Left, int Top, int Right, int Bottom) bounds, int x, int y)
        => bounds = (Math.Min(bounds.Left, x), Math.Min(bounds.Top, y), Math.Max(bounds.Right, x), Math.Max(bounds.Bottom, y));

    /// <summary>The ladder of radii up to <paramref name="maxRadius"/>: 0, the whole pixels to <see cref="FullSizeRadius"/>, then
    /// each <see cref="RungGrowth"/> times the last, the last the largest.</summary>
    internal static float[] Rungs(float maxRadius)
    {
        var rungs = new List<float> { 0 };
        for (float r = 1; r < maxRadius; r = r < FullSizeRadius ? r + 1 : r * RungGrowth) rungs.Add(r);
        rungs.Add(maxRadius);
        return [.. rungs];
    }

    /// <summary>The rung at or beneath <paramref name="radius"/>.</summary>
    private static int Rung(float[] rungs, float radius)
    {
        int k = Array.BinarySearch(rungs, radius);
        return k >= 0 ? k : Math.Max(0, ~k - 1);
    }

    /// <summary>
    /// Every channel of <paramref name="planes"/> blurred through <paramref name="shape"/> at <paramref name="radius"/>, the shape's
    /// weights summing to 1; past the edge, nothing. A wide radius is gathered at a fraction of the size and brought back up.
    /// </summary>
    internal static float[] Blur(float[] planes, int width, int height, float radius, BokehShape shape)
    {
        int factor = radius <= FullSizeRadius ? 1 : (int)MathF.Round(radius / FullSizeRadius);
        if (factor <= 1) return Gather(planes, width, height, radius, shape);
        int w = (width + factor - 1) / factor, h = (height + factor - 1) / factor;
        var reduced = Reduced(planes, width, height, factor, w, h);
        // A reduced pixel stands for factor² of the picture's: its gathered values are of the same scale, so nothing is rescaled.
        var gathered = Gather(reduced, w, h, radius / factor, shape);
        return Enlarged(gathered, w, h, factor, width, height);
    }

    /// <summary>Each channel averaged over <paramref name="factor"/>² blocks — past the edge counted as nothing.</summary>
    private static float[] Reduced(float[] planes, int width, int height, int factor, int w, int h)
    {
        var reduced = new float[Channels * w * h];
        float share = 1f / (factor * factor);
        Parallel.For(0, Channels * h, line =>
        {
            int c = line / h, y = line % h;
            var source = planes.AsSpan(c * width * height, width * height);
            for (int x = 0; x < w; x++)
            {
                float sum = 0;
                for (int dy = 0; dy < factor; dy++)
                {
                    int sy = y * factor + dy;
                    if (sy >= height) break;
                    for (int dx = 0; dx < factor; dx++)
                    {
                        int sx = x * factor + dx;
                        if (sx >= width) break;
                        sum += source[sy * width + sx];
                    }
                }
                reduced[c * w * h + y * w + x] = sum * share;
            }
        });
        return reduced;
    }

    /// <summary>Each channel brought back from <paramref name="w"/> × <paramref name="h"/> to full size, bilinearly, block centres on
    /// block centres.</summary>
    private static float[] Enlarged(float[] reduced, int w, int h, int factor, int width, int height)
    {
        var full = new float[Channels * width * height];
        Parallel.For(0, height, y =>
        {
            float ry = Math.Clamp((y + 0.5f) / factor - 0.5f, 0, h - 1);
            int ya = (int)ry, yb = Math.Min(ya + 1, h - 1);
            float fy = ry - ya;
            for (int x = 0; x < width; x++)
            {
                float rx = Math.Clamp((x + 0.5f) / factor - 0.5f, 0, w - 1);
                int xa = (int)rx, xb = Math.Min(xa + 1, w - 1);
                float fx = rx - xa;
                for (int c = 0; c < Channels; c++)
                {
                    int o = c * w * h;
                    float top = reduced[o + ya * w + xa] * (1 - fx) + reduced[o + ya * w + xb] * fx;
                    float bottom = reduced[o + yb * w + xa] * (1 - fx) + reduced[o + yb * w + xb] * fx;
                    full[c * width * height + y * width + x] = top * (1 - fy) + bottom * fy;
                }
            }
        });
        return full;
    }

    /// <summary>
    /// Each channel gathered through the shape's rows: for each row of the shape, the span it covers summed from the row's running
    /// total — a fraction of the pixels at either end, so the shape's edge is smooth — weighed so the whole shape sums to 1.
    /// </summary>
    private static float[] Gather(float[] planes, int width, int height, float radius, BokehShape shape)
    {
        var rows = ShapeRows(radius, shape);
        double area = 0;
        foreach (var (_, half) in rows) area += 2 * half;
        float norm = (float)(1 / area);

        // Running totals: for each channel and row, the sum of the values left of each position.
        var totals = new float[Channels * height * (width + 1)];
        Parallel.For(0, Channels * height, line =>
        {
            int c = line / height, y = line % height;
            var source = planes.AsSpan(c * width * height + y * width, width);
            var total = totals.AsSpan(line * (width + 1), width + 1);
            float sum = 0;
            total[0] = 0;
            for (int x = 0; x < width; x++) total[x + 1] = sum += source[x];
        });

        var result = new float[Channels * width * height];
        Parallel.For(0, height, y =>
        {
            for (int c = 0; c < Channels; c++)
            {
                var into = result.AsSpan(c * width * height + y * width, width);
                foreach (var (dy, half) in rows)
                {
                    int sy = y + dy;
                    if ((uint)sy >= (uint)height) continue;
                    var source = planes.AsSpan(c * width * height + sy * width, width);
                    var total = totals.AsSpan((c * height + sy) * (width + 1), width + 1);
                    for (int x = 0; x < width; x++)
                    {
                        float centre = x + 0.5f;
                        into[x] += Integral(total, source, width, centre + half) - Integral(total, source, width, centre - half);
                    }
                }
                for (int x = 0; x < width; x++) into[x] *= norm;
            }
        });
        return result;
    }

    /// <summary>The row's values integrated from its left edge to <paramref name="u"/>, each pixel a unit wide: its running total,
    /// and the part of the pixel <paramref name="u"/> falls in; nothing past either edge.</summary>
    private static float Integral(ReadOnlySpan<float> total, ReadOnlySpan<float> row, int width, float u)
    {
        if (u <= 0) return 0;
        if (u >= width) return total[width];
        int i = (int)u;
        return total[i] + (u - i) * row[i];
    }

    /// <summary>The shape's rows at <paramref name="radius"/>: each row's offset and the half-width of its span, at least half a
    /// pixel so every row it reaches counts.</summary>
    internal static List<(int Dy, float Half)> ShapeRows(float radius, BokehShape shape)
    {
        var rows = new List<(int, float)>();
        float halfHeight = shape == BokehShape.Hexagon ? radius * MathF.Sqrt(3) / 2 : radius;
        int reach = (int)MathF.Round(halfHeight);
        for (int dy = -reach; dy <= reach; dy++)
        {
            float d = Math.Min(Math.Abs(dy), halfHeight);
            float half = shape == BokehShape.Hexagon ? radius - d / MathF.Sqrt(3) : MathF.Sqrt(Math.Max(0, radius * radius - d * d));
            rows.Add((dy, Math.Max(0.5f, half)));
        }
        return rows;
    }
}

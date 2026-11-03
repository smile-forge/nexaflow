using System.Buffers;

namespace Nexaflow.Features.Images.Editing.SceneDepth;

/// <summary>
/// A picture's depth set right at a subject's outline. The depth plane is coarser than the photo and soft besides, so where a
/// subject meets what lies behind it the depth on the canvas runs from one to the other over several pixels: the background
/// just outside the outline reads as near as the subject, and the subject's rim as far as the background. A lens blur drawn
/// from that leaves a halo of sharp background round a sharp subject and softens the subject's own edge. Given the subject's
/// outline — picked out by picture selection — each pixel within reach of it takes the depth of its own side, read from that
/// side's pixels farther in, where the depth is the plane's own; the outline's soft pixels share the two by how much of them
/// the subject covers.
/// </summary>
/// <remarks>
/// Every reach is a box sum — a running total along each row, then down each column — so the cost is the canvas's size,
/// whatever the reach. A side with none of its own pixels in reach — a strand of hair, a thin arm — takes the depth the rest of
/// that side has, the subject's as a whole; what is outside such a strand keeps the depth it has.
/// </remarks>
public static class SubjectEdges
{
    /// <summary>How many of the depth plane's own pixels its depth runs across an edge: farther from the outline, the plane is
    /// trusted as it is.</summary>
    public const float PlaneBleed = 3;

    /// <summary>How many reaches out a side's own depth is gathered from.</summary>
    private const int GatherReaches = 2;

    private static readonly ArrayPool<float> Pool = ArrayPool<float>.Shared;

    /// <summary>How far, in canvas pixels, the plane's depth runs across an edge: <see cref="PlaneBleed"/> of its pixels at the
    /// size they are drawn on the canvas, and never less than two.</summary>
    public static int Reach(double canvasPixelsPerPlanePixel) => Math.Max(2, (int)Math.Ceiling(PlaneBleed * canvasPixelsPerPlanePixel));

    /// <summary>
    /// <paramref name="nearness"/> set right at the outline of <paramref name="subject"/>, in place: each pixel within
    /// <paramref name="reach"/> of it given its own side's depth.
    /// </summary>
    /// <param name="nearness">Each canvas pixel's nearness, in its first <c>width × height</c> values.</param>
    /// <param name="subject">How much of each canvas pixel the subject covers, 0…1, in its first <c>width × height</c> values.</param>
    public static void Refine(float[] nearness, float[] subject, int width, int height, int reach)
    {
        int count = width * height;
        if (nearness.Length < count || subject.Length < count) throw new ArgumentException("The depth and the subject are not the size given.");
        if (count == 0 || reach < 1) return;

        var inside = Pool.Rent(count);
        var near = Pool.Rent(count);
        var temp = Pool.Rent(count);
        try
        {
            Parallel.For(0, height, y =>
            {
                for (int i = y * width, end = i + width; i < end; i++) inside[i] = subject[i] >= 0.5f ? 1 : 0;
            });
            // The share of each pixel's square of reach the subject holds: 1 deep inside, 0 well outside, between at the outline.
            BoxSum(inside, near, temp, width, height, reach);
            float full = (2 * reach + 1) * (2 * reach + 1);

            // Each side's trusted pixels — a whole reach from the outline — and the subject's depth as a whole.
            var ownIn = Pool.Rent(count);
            var weightIn = Pool.Rent(count);
            var ownOut = Pool.Rent(count);
            var weightOut = Pool.Rent(count);
            try
            {
                double subjectSum = 0, subjectCount = 0, rimSum = 0, rimCount = 0;
                var gate = new Lock();
                bool anyRim = false;
                Parallel.For(0, height, () => (Sum: 0d, Count: 0d, RimSum: 0d, RimCount: 0d, Rim: false), (y, _, local) =>
                {
                    for (int i = y * width, end = i + width; i < end; i++)
                    {
                        float held = near[i] / Window(i % width, i / width, width, height, reach, full);
                        bool trustedIn = inside[i] == 1 && held >= 1 - 1e-4f, trustedOut = inside[i] == 0 && held <= 1e-4f;
                        weightIn[i] = trustedIn ? 1 : 0;
                        ownIn[i] = trustedIn ? nearness[i] : 0;
                        weightOut[i] = trustedOut ? 1 : 0;
                        ownOut[i] = trustedOut ? nearness[i] : 0;
                        if (trustedIn) { local.Sum += nearness[i]; local.Count++; }
                        else if (inside[i] == 1) { local.RimSum += nearness[i]; local.RimCount++; }
                        if (!trustedIn && !trustedOut) local.Rim = true;
                    }
                    return local;
                }, local =>
                {
                    lock (gate)
                    {
                        subjectSum += local.Sum;
                        subjectCount += local.Count;
                        rimSum += local.RimSum;
                        rimCount += local.RimCount;
                        anyRim |= local.Rim;
                    }
                });
                if (!anyRim || subjectCount + rimCount == 0) return;
                float wholeSubject = (float)(subjectCount > 0 ? subjectSum / subjectCount : rimSum / rimCount);

                int gather = GatherReaches * reach;
                BoxSum(ownIn, ownIn, temp, width, height, gather);
                BoxSum(weightIn, weightIn, temp, width, height, gather);
                BoxSum(ownOut, ownOut, temp, width, height, gather);
                BoxSum(weightOut, weightOut, temp, width, height, gather);

                Parallel.For(0, height, y =>
                {
                    for (int x = 0, i = y * width; x < width; x++, i++)
                    {
                        float held = near[i] / Window(x, y, width, height, reach, full);
                        float covered = Math.Clamp(subject[i], 0, 1);
                        // Within reach of the outline — or on it — the depth is the plane's blend of both sides: set it right.
                        if (held <= 1e-4f && covered <= 0 || held >= 1 - 1e-4f && covered >= 1) continue;
                        float own = nearness[i];
                        float subjectSide = weightIn[i] > 0 ? ownIn[i] / weightIn[i] : wholeSubject;
                        float behind = weightOut[i] > 0 ? ownOut[i] / weightOut[i] : own;
                        nearness[i] = covered * subjectSide + (1 - covered) * behind;
                    }
                });
            }
            finally
            {
                Pool.Return(ownIn);
                Pool.Return(weightIn);
                Pool.Return(ownOut);
                Pool.Return(weightOut);
            }
        }
        finally
        {
            Pool.Return(inside);
            Pool.Return(near);
            Pool.Return(temp);
        }
    }

    /// <summary>How many pixels the square of <paramref name="reach"/> round (<paramref name="x"/>, <paramref name="y"/>) holds
    /// on the canvas — a whole square, short where it meets an edge.</summary>
    private static float Window(int x, int y, int width, int height, int reach, float full)
    {
        if (x >= reach && y >= reach && x < width - reach && y < height - reach) return full;
        int across = Math.Min(width - 1, x + reach) - Math.Max(0, x - reach) + 1;
        int down = Math.Min(height - 1, y + reach) - Math.Max(0, y - reach) + 1;
        return across * down;
    }

    /// <summary>
    /// Each value of <paramref name="source"/> summed over the square of <paramref name="reach"/> round it — what lies past the
    /// canvas's edge counted as nothing — into <paramref name="into"/>, which may be <paramref name="source"/> itself. Along each
    /// row, then down each column in strips side by side, each a running total.
    /// </summary>
    internal static void BoxSum(float[] source, float[] into, float[] temp, int width, int height, int reach)
    {
        Parallel.For(0, height, y =>
        {
            var row = source.AsSpan(y * width, width);
            var summed = temp.AsSpan(y * width, width);
            double running = 0;
            for (int x = 0; x <= Math.Min(reach, width - 1); x++) running += row[x];
            for (int x = 0; x < width; x++)
            {
                summed[x] = (float)running;
                int enters = x + reach + 1, leaves = x - reach;
                if (enters < width) running += row[enters];
                if (leaves >= 0) running -= row[leaves];
            }
        });

        const int Strip = 64;
        Parallel.For(0, (width + Strip - 1) / Strip, strip =>
        {
            int left = strip * Strip, columns = Math.Min(Strip, width - left);
            Span<double> running = stackalloc double[Strip];
            running.Clear();
            for (int y = 0; y <= Math.Min(reach, height - 1); y++)
                for (int c = 0; c < columns; c++) running[c] += temp[y * width + left + c];
            for (int y = 0; y < height; y++)
            {
                for (int c = 0; c < columns; c++) into[y * width + left + c] = (float)running[c];
                int enters = y + reach + 1, leaves = y - reach;
                if (enters < height) for (int c = 0; c < columns; c++) running[c] += temp[enters * width + left + c];
                if (leaves >= 0) for (int c = 0; c < columns; c++) running[c] -= temp[leaves * width + left + c];
            }
        });
    }
}

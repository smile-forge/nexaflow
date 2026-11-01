using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Selection;

/// <summary>
/// The outline round a coverage where it is half covered — the line marching ants walk — as closed loops, by marching
/// squares over the pixel centres. Each crossing is placed between two centres where the coverage passes half, so an
/// anti-aliased edge gives a smooth line; past the canvas is read as uncovered, so a selection reaching the edge is
/// closed along it.
/// </summary>
public static class CoverageOutline
{
    /// <summary>Where a pixel counts as inside: half covered or more.</summary>
    private const int Half = 128;

    /// <summary>The loops round <paramref name="coverage"/>, in its pixels (a pixel's centre at +0.5); none where nothing is covered.</summary>
    public static IReadOnlyList<SKPoint[]> Trace(TiledCoverage coverage)
    {
        var bounds = coverage.Bounds;
        if (bounds.IsEmpty) return [];

        // The samples: the bounds and a ring of uncovered pixels round them, so every loop closes.
        int left = bounds.Left - 1, top = bounds.Top - 1;
        int columns = bounds.Width + 2, rows = bounds.Height + 2;
        var samples = new byte[columns * rows];
        var inner = coverage.Read(bounds);
        for (int y = 0; y < bounds.Height; y++)
            inner.AsSpan(y * bounds.Width, bounds.Width).CopyTo(samples.AsSpan((y + 1) * columns + 1));

        // Each crossed edge between two samples, joined to the two edges its cells join it to.
        var joins = new Dictionary<long, (long First, long Second)>();
        var places = new Dictionary<long, SKPoint>();
        for (int y = 0; y < rows - 1; y++)
            for (int x = 0; x < columns - 1; x++)
            {
                int tl = samples[y * columns + x], tr = samples[y * columns + x + 1];
                int bl = samples[(y + 1) * columns + x], br = samples[(y + 1) * columns + x + 1];
                int corners = (tl >= Half ? 8 : 0) | (tr >= Half ? 4 : 0) | (br >= Half ? 2 : 0) | (bl >= Half ? 1 : 0);
                if (corners is 0 or 15) continue;

                long upper = Horizontal(x, y, columns), lower = Horizontal(x, y + 1, columns);
                long west = Vertical(x, y, columns), east = Vertical(x + 1, y, columns);
                bool centreInside = (tl + tr + bl + br) >= 4 * Half;
                switch (corners)
                {
                    case 1 or 14: Join(west, lower); break;
                    case 2 or 13: Join(lower, east); break;
                    case 3 or 12: Join(west, east); break;
                    case 4 or 11: Join(upper, east); break;
                    case 6 or 9: Join(upper, lower); break;
                    case 7 or 8: Join(west, upper); break;
                    case 5 when centreInside: Join(west, upper); Join(lower, east); break;
                    case 5: Join(west, lower); Join(upper, east); break;
                    case 10 when centreInside: Join(upper, east); Join(west, lower); break;
                    case 10: Join(west, upper); Join(lower, east); break;
                }
            }

        void Join(long a, long b)
        {
            Place(a);
            Place(b);
            joins[a] = joins.TryGetValue(a, out var ja) ? (ja.First, b) : (b, long.MinValue);
            joins[b] = joins.TryGetValue(b, out var jb) ? (jb.First, a) : (a, long.MinValue);
        }

        void Place(long edge)
        {
            if (places.ContainsKey(edge)) return;
            long index = edge >> 1;
            int x = (int)(index % columns), y = (int)(index / columns);
            bool horizontal = (edge & 1) == 0;
            int from = samples[y * columns + x], to = horizontal ? samples[y * columns + x + 1] : samples[(y + 1) * columns + x];
            float t = (float)((Half - 0.5 - from) / (to - from));
            places[edge] = horizontal
                ? new SKPoint(left + x + 0.5f + t, top + y + 0.5f)
                : new SKPoint(left + x + 0.5f, top + y + 0.5f + t);
        }

        var loops = new List<SKPoint[]>();
        var walked = new HashSet<long>();
        foreach (var start in joins.Keys)
        {
            if (!walked.Add(start)) continue;
            var loop = new List<SKPoint> { places[start] };
            long previous = start, at = joins[start].First;
            while (at != start && walked.Add(at))
            {
                loop.Add(places[at]);
                var (first, second) = joins[at];
                long next = first == previous ? second : first;
                previous = at;
                at = next;
            }
            if (loop.Count >= 3) loops.Add(Simplified(loop));
        }
        return loops;
    }

    private static long Horizontal(int x, int y, int columns) => ((long)y * columns + x) << 1;

    private static long Vertical(int x, int y, int columns) => (((long)y * columns + x) << 1) | 1;

    /// <summary>The loop without the points that lie on the line between their neighbours.</summary>
    private static SKPoint[] Simplified(List<SKPoint> loop)
    {
        const float straight = 1e-3f;
        var kept = new List<SKPoint>(loop.Count);
        for (int i = 0; i < loop.Count; i++)
        {
            SKPoint before = loop[(i + loop.Count - 1) % loop.Count], here = loop[i], after = loop[(i + 1) % loop.Count];
            float cross = (here.X - before.X) * (after.Y - before.Y) - (here.Y - before.Y) * (after.X - before.X);
            if (Math.Abs(cross) > straight) kept.Add(here);
        }
        return kept.Count >= 3 ? [.. kept] : [.. loop];
    }
}

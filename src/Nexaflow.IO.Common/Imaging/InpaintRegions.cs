namespace Nexaflow.IO.Common.Imaging;

/// <summary>
/// Where a mask's fill is worked out: round each part of the mask, with as much picture again on every side — the
/// surroundings a fill is taken from — at least <see cref="LeastSide"/> pixels each way, parts whose surroundings overlap
/// worked as one. A region past <see cref="MostWorkPixels"/> is worked smaller, its fill brought back up to its size.
/// So scratches across a photo are filled from what is round each, a rubbed-out person from the street round them, and a
/// fill costs what its holes need rather than what the photo holds.
/// </summary>
public static class InpaintRegions
{
    /// <summary>The fewest pixels a region spans each way, where the picture has them: enough surroundings for a small hole.</summary>
    public const int LeastSide = 512;

    /// <summary>The most pixels a region is worked at — 1536 square: an inpainter trained on small crops holds up to about
    /// that, and its memory grows with it.</summary>
    public const long MostWorkPixels = 1536L * 1536;

    /// <summary>The regions <paramref name="mask"/> — one byte a pixel, filled where it is not 0 — is worked in, none when
    /// nothing is filled.</summary>
    public static IReadOnlyList<InpaintRegion> Of(ReadOnlySpan<byte> mask, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "A picture has a size.");
        if (mask.Length != width * height) throw new ArgumentException("The mask is not the picture's size.", nameof(mask));

        var boxes = Parts(mask, width, height).Select(p => Surroundings(p, width, height)).ToList();
        Merge(boxes);
        return [.. boxes.OrderBy(b => b.Top).ThenBy(b => b.Left).Select(b => Worked(b))];
    }

    /// <summary>The box round each 8-connected part of the mask.</summary>
    private static List<Box> Parts(ReadOnlySpan<byte> mask, int width, int height)
    {
        var parts = new List<Box>();
        var seen = new bool[mask.Length];
        var stack = new Stack<int>();
        for (int start = 0; start < mask.Length; start++)
        {
            if (mask[start] == 0 || seen[start]) continue;
            int left = width, top = height, right = -1, bottom = -1;
            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int at = stack.Pop(), x = at % width, y = at / width;
                (left, top, right, bottom) = (Math.Min(left, x), Math.Min(top, y), Math.Max(right, x), Math.Max(bottom, y));
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
                        int next = ny * width + nx;
                        if (mask[next] == 0 || seen[next]) continue;
                        seen[next] = true;
                        stack.Push(next);
                    }
            }
            parts.Add(new Box(left, top, right + 1, bottom + 1));
        }
        return parts;
    }

    /// <summary>A part's box grown by its own width and height on every side, to at least <see cref="LeastSide"/> about its
    /// centre, inside the picture.</summary>
    private static Box Surroundings(Box part, int width, int height)
    {
        int w = part.Right - part.Left, h = part.Bottom - part.Top;
        var (left, right) = Grown(part.Left - w, part.Right + w, width);
        var (top, bottom) = Grown(part.Top - h, part.Bottom + h, height);
        return new Box(left, top, right, bottom);

        static (int Low, int High) Grown(int low, int high, int size)
        {
            int span = Math.Min(size, Math.Max(LeastSide, high - low));
            int centre = (low + high) / 2;
            int start = Math.Clamp(centre - span / 2, 0, size - span);
            return (start, start + span);
        }
    }

    /// <summary>Joins boxes that overlap until none do.</summary>
    private static void Merge(List<Box> boxes)
    {
        for (bool joined = true; joined;)
        {
            joined = false;
            for (int i = 0; i < boxes.Count && !joined; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    if (!boxes[i].Overlaps(boxes[j])) continue;
                    boxes[i] = boxes[i].Union(boxes[j]);
                    boxes.RemoveAt(j);
                    joined = true;
                    break;
                }
        }
    }

    private static InpaintRegion Worked(Box box)
    {
        int w = box.Right - box.Left, h = box.Bottom - box.Top;
        long pixels = (long)w * h;
        if (pixels <= MostWorkPixels) return new InpaintRegion(box.Left, box.Top, w, h, w, h);
        double shrink = Math.Sqrt((double)MostWorkPixels / pixels);
        return new InpaintRegion(box.Left, box.Top, w, h, Math.Max(1, (int)(w * shrink)), Math.Max(1, (int)(h * shrink)));
    }

    private readonly record struct Box(int Left, int Top, int Right, int Bottom)
    {
        public bool Overlaps(Box other) => Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

        public Box Union(Box other)
            => new(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }
}

/// <summary>
/// A region of a picture a fill is worked in: <see cref="Width"/> × <see cref="Height"/> pixels from
/// (<see cref="Left"/>, <see cref="Top"/>), worked at <see cref="WorkWidth"/> × <see cref="WorkHeight"/> — smaller where
/// the region is larger than an inpainter is worked at.
/// </summary>
public readonly record struct InpaintRegion(int Left, int Top, int Width, int Height, int WorkWidth, int WorkHeight)
{
    /// <summary>Whether the region is worked smaller than it is.</summary>
    public bool Shrunk => WorkWidth != Width || WorkHeight != Height;
}

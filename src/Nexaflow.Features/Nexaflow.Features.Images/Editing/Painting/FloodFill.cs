using System.Collections;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Selection;

namespace Nexaflow.Features.Images.Editing.Painting;

/// <summary>
/// What the Fill tool reaches from a pixel: every pixel within the tolerance of the one clicked — joined to it, where the fill
/// is contiguous — as coverage over the canvas. Pixels are read a tile at a time, as the reach comes to them, and the reach is
/// written a tile at a time, so a fill costs what it covers.
/// </summary>
public static class FloodFill
{
    private const int TileSize = 256;
    private const int TileShift = 8;
    private const int TileMask = TileSize - 1;

    /// <summary>What a fill at (<paramref name="x"/>, <paramref name="y"/>) reaches on a layer's pixels: the colour and
    /// transparency of each within <paramref name="tolerance"/> (0–255, on every channel) of the one there.</summary>
    public static TiledCoverage Reach(TiledPixels pixels, int width, int height, int x, int y, int tolerance, bool contiguous)
    {
        var tiles = new Dictionary<(int, int), byte[]>();
        uint Sample(int px, int py)
        {
            var key = (px >> TileShift, py >> TileShift);
            if (!tiles.TryGetValue(key, out var tile))
            {
                var rect = new SkiaSharp.SKRectI(key.Item1 << TileShift, key.Item2 << TileShift,
                                                 Math.Min(width, (key.Item1 + 1) << TileShift), Math.Min(height, (key.Item2 + 1) << TileShift));
                tiles[key] = tile = Padded(pixels.Read(rect), rect.Width, rect.Height, 4);
            }
            int i = ((py & TileMask) * TileSize + (px & TileMask)) * 4;
            return (uint)(tile[i] | tile[i + 1] << 8 | tile[i + 2] << 16 | tile[i + 3] << 24);
        }
        return Reach(width, height, x, y, contiguous, Sample, (a, b) => Within(a, b, tolerance, 4));
    }

    /// <summary>What a fill at (<paramref name="x"/>, <paramref name="y"/>) reaches on a mask: each value within
    /// <paramref name="tolerance"/> of the one there.</summary>
    public static TiledCoverage Reach(TiledCoverage mask, int x, int y, int tolerance, bool contiguous)
    {
        var tiles = new Dictionary<(int, int), byte[]>();
        uint Sample(int px, int py)
        {
            var key = (px >> TileShift, py >> TileShift);
            if (!tiles.TryGetValue(key, out var tile))
            {
                var rect = new SkiaSharp.SKRectI(key.Item1 << TileShift, key.Item2 << TileShift,
                                                 Math.Min(mask.Width, (key.Item1 + 1) << TileShift), Math.Min(mask.Height, (key.Item2 + 1) << TileShift));
                tiles[key] = tile = Padded(mask.Read(rect), rect.Width, rect.Height, 1);
            }
            return tile[(py & TileMask) * TileSize + (px & TileMask)];
        }
        return Reach(mask.Width, mask.Height, x, y, contiguous, Sample, (a, b) => Within(a, b, tolerance, 1));
    }

    private static bool Within(uint a, uint b, int tolerance, int channels)
    {
        for (int c = 0; c < channels; c++)
            if (Math.Abs((int)(a >> (c * 8) & 0xFF) - (int)(b >> (c * 8) & 0xFF)) > tolerance) return false;
        return true;
    }

    // A tile read at the canvas's edge, laid into a whole tile so it is indexed as any other.
    private static byte[] Padded(byte[] read, int width, int height, int channels)
    {
        if (width == TileSize && height == TileSize) return read;
        var tile = new byte[TileSize * TileSize * channels];
        for (int row = 0; row < height; row++)
            Array.Copy(read, row * width * channels, tile, row * TileSize * channels, width * channels);
        return tile;
    }

    private static TiledCoverage Reach(int width, int height, int x, int y, bool contiguous, Func<int, int, uint> sample, Func<uint, uint, bool> near)
    {
        var reached = TiledCoverage.Clear(width, height);
        if (x < 0 || y < 0 || x >= width || y >= height) return reached;
        uint seed = sample(x, y);
        var output = new Dictionary<(int, int), byte[]>();
        void Mark(int px, int py)
        {
            var key = (px >> TileShift, py >> TileShift);
            if (!output.TryGetValue(key, out var tile)) output[key] = tile = new byte[TileSize * TileSize];
            tile[(py & TileMask) * TileSize + (px & TileMask)] = 255;
        }

        if (contiguous)
        {
            // Scanline flood: each run of matching pixels filled whole, the rows above and below seeded from it.
            var visited = new BitArray(checked(width * height));
            var pending = new Stack<(int X, int Y)>();
            pending.Push((x, y));
            while (pending.Count > 0)
            {
                var (sx, sy) = pending.Pop();
                if (visited[sy * width + sx] || !near(sample(sx, sy), seed)) continue;
                int left = sx, right = sx;
                while (left > 0 && !visited[sy * width + left - 1] && near(sample(left - 1, sy), seed)) left--;
                while (right < width - 1 && !visited[sy * width + right + 1] && near(sample(right + 1, sy), seed)) right++;
                for (int px = left; px <= right; px++)
                {
                    visited[sy * width + px] = true;
                    Mark(px, sy);
                    if (sy > 0 && !visited[(sy - 1) * width + px]) pending.Push((px, sy - 1));
                    if (sy < height - 1 && !visited[(sy + 1) * width + px]) pending.Push((px, sy + 1));
                }
            }
        }
        else
        {
            for (int py = 0; py < height; py++)
                for (int px = 0; px < width; px++)
                    if (near(sample(px, py), seed)) Mark(px, py);
        }

        foreach (var ((column, row), tile) in output)
        {
            var rect = new SkiaSharp.SKRectI(column << TileShift, row << TileShift,
                                             Math.Min(width, (column + 1) << TileShift), Math.Min(height, (row + 1) << TileShift));
            var values = new byte[rect.Width * rect.Height];
            for (int r = 0; r < rect.Height; r++) Array.Copy(tile, r * TileSize, values, r * rect.Width, rect.Width);
            reached = reached.Written(rect, values);
        }
        return reached;
    }
}

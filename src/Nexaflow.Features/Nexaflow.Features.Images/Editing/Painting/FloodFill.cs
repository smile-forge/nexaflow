using System.Collections;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Selection;

namespace Nexaflow.Features.Images.Editing.Painting;

/// <summary>
/// What the Fill tool and selecting by colour reach from a pixel: every pixel within the tolerance of the one clicked — joined
/// to it, where the reach is contiguous — as coverage over the canvas. Tiled pixels are read a tile at a time, as the reach
/// comes to them, and the reach is written a tile at a time, so a fill costs what it covers. Pixels and masks of any depth
/// are compared as values, so a 16-bit picture's tolerance is as fine as its steps; the tolerance is in 8-bit steps, 0–255.
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
        if (x < 0 || y < 0 || x >= width || y >= height) return TiledCoverage.Clear(width, height);
        var tiles = new Dictionary<(int, int), float[]>();
        float[] TileAt(int px, int py)
        {
            var key = (px >> TileShift, py >> TileShift);
            if (!tiles.TryGetValue(key, out var tile))
            {
                var rect = new SkiaSharp.SKRectI(key.Item1 << TileShift, key.Item2 << TileShift,
                                                 Math.Min(width, (key.Item1 + 1) << TileShift), Math.Min(height, (key.Item2 + 1) << TileShift));
                tiles[key] = tile = Padded(pixels.ReadPremul(rect), rect.Width, rect.Height, 4);
            }
            return tile;
        }
        int seedAt = ((y & TileMask) * TileSize + (x & TileMask)) * 4;
        var seedTile = TileAt(x, y);
        float sr = seedTile[seedAt], sg = seedTile[seedAt + 1], sb = seedTile[seedAt + 2], sa = seedTile[seedAt + 3];
        float reach = ToleranceOf(tolerance, pixels.Format.Colour.Depth);
        bool Matches(int px, int py)
        {
            var tile = TileAt(px, py);
            int i = ((py & TileMask) * TileSize + (px & TileMask)) * 4;
            return Math.Abs(tile[i] - sr) <= reach && Math.Abs(tile[i + 1] - sg) <= reach
                && Math.Abs(tile[i + 2] - sb) <= reach && Math.Abs(tile[i + 3] - sa) <= reach;
        }
        return Reach(width, height, x, y, contiguous, Matches);
    }

    /// <summary>What a fill at (<paramref name="x"/>, <paramref name="y"/>) reaches on a mask: each value within
    /// <paramref name="tolerance"/> (0–255) of the one there.</summary>
    public static TiledCoverage Reach(TiledCoverage mask, int x, int y, int tolerance, bool contiguous)
    {
        if (x < 0 || y < 0 || x >= mask.Width || y >= mask.Height) return TiledCoverage.Clear(mask.Width, mask.Height);
        var tiles = new Dictionary<(int, int), float[]>();
        float ValueAt(int px, int py)
        {
            var key = (px >> TileShift, py >> TileShift);
            if (!tiles.TryGetValue(key, out var tile))
            {
                var rect = new SkiaSharp.SKRectI(key.Item1 << TileShift, key.Item2 << TileShift,
                                                 Math.Min(mask.Width, (key.Item1 + 1) << TileShift), Math.Min(mask.Height, (key.Item2 + 1) << TileShift));
                tiles[key] = tile = Padded(mask.ReadValues(rect), rect.Width, rect.Height, 1);
            }
            return tile[(py & TileMask) * TileSize + (px & TileMask)];
        }
        float seed = ValueAt(x, y), reach = ToleranceOf(tolerance, mask.Depth);
        return Reach(mask.Width, mask.Height, x, y, contiguous, (px, py) => Math.Abs(ValueAt(px, py) - seed) <= reach);
    }

    /// <summary>What a reach at (<paramref name="x"/>, <paramref name="y"/>) covers on a picture's pixels held whole — interleaved
    /// 8-bit RGBA, row after row, as a picture tool is handed them: each within <paramref name="tolerance"/> of the one there.</summary>
    public static TiledCoverage Reach(byte[] rgba, int width, int height, int x, int y, int tolerance, bool contiguous)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return TiledCoverage.Clear(width, height);
        int seed = (y * width + x) * 4;
        bool Matches(int px, int py)
        {
            int i = (py * width + px) * 4;
            for (int c = 0; c < 4; c++)
                if (Math.Abs(rgba[i + c] - rgba[seed + c]) > tolerance) return false;
            return true;
        }
        return Reach(width, height, x, y, contiguous, Matches);
    }

    /// <summary>How far a value may lie from the one clicked: <paramref name="tolerance"/> 8-bit steps, and half a step of
    /// <paramref name="depth"/> more, so a difference of exactly the tolerance is in and, at 0, only equal values are.</summary>
    private static float ToleranceOf(int tolerance, ChannelDepth depth)
        => tolerance / 255f + depth switch { ChannelDepth.U8 => 0.5f / 255, ChannelDepth.U16 => 0.5f / 65535, _ => 1e-6f };

    // A tile read at the canvas's edge, laid into a whole tile so it is indexed as any other.
    private static T[] Padded<T>(T[] read, int width, int height, int channels)
    {
        if (width == TileSize && height == TileSize) return read;
        var tile = new T[TileSize * TileSize * channels];
        for (int row = 0; row < height; row++)
            Array.Copy(read, row * width * channels, tile, row * TileSize * channels, width * channels);
        return tile;
    }

    private static TiledCoverage Reach(int width, int height, int x, int y, bool contiguous, Func<int, int, bool> matches)
    {
        var reached = TiledCoverage.Clear(width, height);
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
                if (visited[sy * width + sx] || !matches(sx, sy)) continue;
                int left = sx, right = sx;
                while (left > 0 && !visited[sy * width + left - 1] && matches(left - 1, sy)) left--;
                while (right < width - 1 && !visited[sy * width + right + 1] && matches(right + 1, sy)) right++;
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
                    if (matches(px, py)) Mark(px, py);
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

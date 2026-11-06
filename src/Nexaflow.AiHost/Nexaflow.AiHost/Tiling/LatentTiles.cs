using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Tiling;

/// <summary>A tile of a latent plane, in latent cells: where it starts and how large it is.</summary>
internal readonly record struct LatentTile(int X, int Y, int Width, int Height);

/// <summary>
/// A latent plane cut into tiles that overlap their neighbours, for a part whose peak grows with what it reads at once — a
/// VAE's decoder or encoder: each tile is run alone and what they give back is blended (<see cref="TileBlend"/>). Every
/// tile but those of a plane narrower than <see cref="Side"/> is <see cref="Side"/> square, the last of a row or column
/// moved back to end at the plane's edge, so a part sees one size of tile throughout.
/// </summary>
internal sealed class LatentTiles
{
    private LatentTiles(int width, int height, int side, int overlap, IReadOnlyList<LatentTile> tiles)
    {
        Width = width;
        Height = height;
        Side = side;
        Overlap = overlap;
        Tiles = tiles;
    }

    public int Width { get; }
    public int Height { get; }
    public int Side { get; }

    /// <summary>How many cells neighbouring tiles share at the least: what each blends across.</summary>
    public int Overlap { get; }

    /// <summary>Row by row.</summary>
    public IReadOnlyList<LatentTile> Tiles { get; }

    /// <param name="side">The side of a tile, in cells.</param>
    /// <param name="overlap">The cells neighbouring tiles share at the least; less than half of <paramref name="side"/>.</param>
    public static LatentTiles Over(int width, int height, int side, int overlap)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(side, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(overlap);
        if (overlap * 2 >= side) throw new ArgumentOutOfRangeException(nameof(overlap), overlap, $"an overlap of {overlap} leaves a tile of {side} nothing of its own");

        var tiles = new List<LatentTile>();
        foreach (var (y, h) in Starts(height, side, overlap))
            foreach (var (x, w) in Starts(width, side, overlap))
                tiles.Add(new LatentTile(x, y, w, h));
        return new LatentTiles(width, height, side, overlap, tiles);
    }

    /// <summary>Where the tiles along one side start, and how long each is.</summary>
    private static IEnumerable<(int Start, int Length)> Starts(int length, int side, int overlap)
    {
        if (length <= side)
        {
            yield return (0, length);
            yield break;
        }
        int step = side - overlap;
        for (int start = 0; ; start += step)
        {
            if (start + side >= length)
            {
                yield return (length - side, side);
                yield break;
            }
            yield return (start, side);
        }
    }
}

/// <summary>
/// What tiles of a <see cref="LatentTiles"/> give back, blended into the whole: each tile's values weighted by a ramp across
/// the cells it shares with a neighbour, rising from the tile's edge — and not at the plane's own edge, where nothing is
/// shared — so a seam is a gradual hand-over from one tile to the next, and every value is divided by the weights it was
/// given, which sum to one.
/// </summary>
/// <param name="channels">How many planes a tile gives back.</param>
/// <param name="scale">How many values a cell is given back as along each side: 8 or 16 pixels for a VAE's decoder, 1 for a
/// part that keeps the latent's size.</param>
internal sealed class TileBlend(LatentTiles tiles, int channels, int scale)
{
    private readonly int _width = tiles.Width * scale;
    private readonly int _height = tiles.Height * scale;
    private readonly float[] _sum = new float[channels * tiles.Width * scale * tiles.Height * scale];
    private readonly float[] _weight = new float[tiles.Width * scale * tiles.Height * scale];

    public int Width => _width;
    public int Height => _height;

    /// <summary>Adds what <paramref name="tile"/> gave back: planar, <c>[channels, tile.Height·scale, tile.Width·scale]</c>.</summary>
    public void Add(LatentTile tile, ReadOnlySpan<float> made)
    {
        int w = tile.Width * scale, h = tile.Height * scale, plane = w * h;
        if (made.Length != channels * plane) throw new ArgumentException($"a tile of {tile.Width}×{tile.Height} cells gives back {channels * plane} values, not {made.Length}", nameof(made));

        var across = Ramp(tile.X, tile.Width, tiles.Width);
        var down = Ramp(tile.Y, tile.Height, tiles.Height);
        int x0 = tile.X * scale, y0 = tile.Y * scale, whole = _width * _height;
        for (int y = 0; y < h; y++)
        {
            int row = (y0 + y) * _width + x0;
            for (int x = 0; x < w; x++)
            {
                float weight = across[x] * down[y];
                _weight[row + x] += weight;
                for (int c = 0; c < channels; c++)
                    _sum[c * whole + row + x] += weight * made[c * plane + y * w + x];
            }
        }
    }

    /// <summary>The whole plane, planar <c>[channels, Height, Width]</c>, once every tile has been added.</summary>
    public float[] Result()
    {
        int whole = _width * _height;
        var result = new float[_sum.Length];
        for (int i = 0; i < whole; i++)
        {
            if (_weight[i] <= 0) throw new InvalidOperationException("a tile was not added");
            float share = 1f / _weight[i];
            for (int c = 0; c < channels; c++) result[c * whole + i] = _sum[c * whole + i] * share;
        }
        return result;
    }

    /// <summary>A tile's weight along one side, value by value: rising across the overlap from an edge it shares with a
    /// neighbour, 1 across the rest and at the plane's own edge.</summary>
    private float[] Ramp(int start, int length, int planeLength)
    {
        int n = length * scale, ramp = tiles.Overlap * scale;
        var weights = new float[n];
        bool sharedBefore = start > 0, sharedAfter = start + length < planeLength;
        for (int i = 0; i < n; i++)
        {
            float w = 1f;
            if (sharedBefore && i < ramp) w = Math.Min(w, (i + 0.5f) / ramp);
            if (sharedAfter && n - 1 - i < ramp) w = Math.Min(w, (n - i - 0.5f) / ramp);
            weights[i] = w;
        }
        return weights;
    }
}

/// <summary>
/// A latent decoded in tiles (<see cref="ModelSpec.TileCells"/>): cut into squares of so many cells overlapping by an eighth, each
/// decoded alone, and blended back (<see cref="TileBlend"/>) — so a decoder holds one tile's work at a time, not the picture's.
/// The blend is what a whole decode gives to within the decoder's own normalisation, which each tile does alone: measured on
/// Z-Image's decoder, 37–40 dB from the whole picture.
/// </summary>
internal static class LatentTiling
{
    /// <param name="scale">How many pixels a cell decodes to, along each side.</param>
    /// <param name="decodeTile">Decodes one tile — whether it is the last — into planar RGB, <c>[3, height·scale, width·scale]</c>.</param>
    /// <returns>The picture, planar RGB.</returns>
    public static float[] Decode(int rows, int cols, int scale, int cells, Func<LatentTile, bool, float[]> decodeTile)
    {
        var grid = LatentTiles.Over(cols, rows, cells, Math.Max(1, cells / 8));
        var blend = new TileBlend(grid, 3, scale);
        for (int i = 0; i < grid.Tiles.Count; i++) blend.Add(grid.Tiles[i], decodeTile(grid.Tiles[i], i == grid.Tiles.Count - 1));
        return blend.Result();
    }

    /// <summary>A tile's cells out of a planar latent, <c>[channels, rows, cols]</c>.</summary>
    public static float[] Planar(ReadOnlySpan<float> latent, int channels, int rows, int cols, LatentTile tile)
    {
        var cut = new float[channels * tile.Height * tile.Width];
        for (int c = 0; c < channels; c++)
            for (int y = 0; y < tile.Height; y++)
                latent.Slice(c * rows * cols + (tile.Y + y) * cols + tile.X, tile.Width).CopyTo(cut.AsSpan(c * tile.Height * tile.Width + y * tile.Width));
        return cut;
    }

    /// <summary>A tile's cells out of an interleaved latent, <c>[rows, cols, channels]</c>.</summary>
    public static float[] Interleaved(ReadOnlySpan<float> latent, int channels, int cols, LatentTile tile)
    {
        var cut = new float[tile.Height * tile.Width * channels];
        for (int y = 0; y < tile.Height; y++)
            latent.Slice(((tile.Y + y) * cols + tile.X) * channels, tile.Width * channels).CopyTo(cut.AsSpan(y * tile.Width * channels));
        return cut;
    }

    /// <summary>Interleaved 8-bit RGB as planar values.</summary>
    public static float[] PlanarOf(ReadOnlySpan<byte> rgb)
    {
        int plane = rgb.Length / 3;
        var planar = new float[rgb.Length];
        for (int p = 0; p < plane; p++)
            for (int c = 0; c < 3; c++) planar[c * plane + p] = rgb[p * 3 + c];
        return planar;
    }

    /// <summary>Planar values as interleaved 8-bit RGB, rounded to the nearest.</summary>
    public static byte[] RgbOf(ReadOnlySpan<float> planar)
    {
        int plane = planar.Length / 3;
        var rgb = new byte[planar.Length];
        for (int p = 0; p < plane; p++)
            for (int c = 0; c < 3; c++) rgb[p * 3 + c] = (byte)Math.Clamp(MathF.Round(planar[c * plane + p]), 0, 255);
        return rgb;
    }
}

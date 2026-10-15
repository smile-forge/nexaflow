using Nexaflow.IO.Common.Imaging;

namespace Nexaflow.AiHost.Restoring;

/// <summary>
/// A whole picture through an upscaler as Real-ESRGAN's own <c>RealESRGANer.enhance</c> takes it: padded by reflection
/// to what the model divides, cut into tiles that each read a margin of their neighbours, each tile's margin dropped from
/// what comes back, and the picture brought to the size asked for with Lanczos. An alpha channel goes through the model
/// too, as grey, unless the picture is opaque.
/// </summary>
internal static class PictureUpscaling
{
    /// <summary>
    /// The side of the square a tile reads, before its margin: 512 pixels through the 2x model, which folds each 2×2 into
    /// channels and so works at a quarter of the tile; 256 through the 4x one, which works at the tile's full size and would
    /// otherwise peak near 8 GB — at 256 it peaks under 3 GB, so either fits a 4 GB GPU.
    /// </summary>
    public static int TileSide(int factor) => factor == 4 ? 256 : 512;

    /// <summary>How far past its own square a tile reads, so its edge is made from what surrounds it.</summary>
    public const int TilePad = 24;

    /// <summary>A tile of the padded picture: what it reads, and the square of it kept.</summary>
    public readonly record struct TileArea(int ReadX, int ReadY, int ReadWidth, int ReadHeight, int KeepX, int KeepY, int KeepWidth, int KeepHeight);

    /// <summary>The tiles of <paramref name="side"/> pixels a <paramref name="width"/> × <paramref name="height"/> picture is
    /// cut into, row by row.</summary>
    public static IReadOnlyList<TileArea> Tiles(int width, int height, int side)
    {
    var tiles = new List<TileArea>();
    for (int y = 0; y < height; y += side)
    for (int x = 0; x < width; x += side)
    {
    int endX = Math.Min(x + side, width), endY = Math.Min(y + side, height);
                int readX = Math.Max(x - TilePad, 0), readY = Math.Max(y - TilePad, 0);
                int readEndX = Math.Min(endX + TilePad, width), readEndY = Math.Min(endY + TilePad, height);
                tiles.Add(new(readX, readY, readEndX - readX, readEndY - readY, x, y, endX - x, endY - y));
            }
        return tiles;
    }

    /// <summary>How far a picture is padded at its right and bottom so the model divides it: to even sides for the 2x
    /// model, which folds each 2×2 into channels; not at all for the 4x one.</summary>
    public static (int Right, int Bottom) ModPad(int width, int height, int factor)
        => factor == 2 ? (width % 2, height % 2) : (0, 0);

    /// <summary>
    /// Upscales interleaved RGBA by <paramref name="factor"/>, then brings it to <paramref name="scale"/> times its size.
    /// Each tile is read straight from the picture and written straight into the result, so nothing of the picture is held
    /// as floats but the tile at work.
    /// </summary>
    /// <param name="upscaleTile">Runs one tile — planar RGB, its width and height, and whether it is the picture's last —
    /// and gives back the tile <paramref name="factor"/> times the size.</param>
    /// <param name="tileDone">Each tile as it is done, of how many.</param>
    public static async Task<byte[]> UpscaleAsync(byte[] rgba, int width, int height, int factor, int scale,
                                                  Func<float[], int, int, bool, Task<float[]>> upscaleTile, Func<int, int, Task> tileDone)
    {
        bool opaque = IsOpaque(rgba);
        var (padRight, padBottom) = ModPad(width, height, factor);
        var tiles = Tiles(width + padRight, height + padBottom, TileSide(factor));
        int total = tiles.Count * (opaque ? 1 : 2), done = 0;

        var output = new byte[width * factor * height * factor * 4];
        if (opaque)
            for (int i = 3; i < output.Length; i += 4) output[i] = 255;
        await ThroughTiles(rgba, width, height, factor, tiles, alpha: false, output, upscaleTile, () => tileDone(++done, total), lastOfAll: opaque);
        if (!opaque)
            await ThroughTiles(rgba, width, height, factor, tiles, alpha: true, output, upscaleTile, () => tileDone(++done, total), lastOfAll: true);

        int ow = width * factor, oh = height * factor;
        return scale == factor ? output : PictureResampling.ResizeLanczos(output, ow, oh, 4, width * scale, height * scale);
    }

    /// <summary>
    /// Each tile through the upscaler, its own square of what comes back written into <paramref name="output"/> — the colour
    /// into its RGB, rounded as NumPy rounds, or, for <paramref name="alpha"/>, the grey the alpha went through as, read back
    /// as OpenCV reads grey from BGR, into its alpha. What falls in the padding is dropped.
    /// </summary>
    private static async Task ThroughTiles(byte[] rgba, int width, int height, int factor, IReadOnlyList<TileArea> tiles, bool alpha, byte[] output,
                                           Func<float[], int, int, bool, Task<float[]>> upscaleTile, Func<Task> tileDone, bool lastOfAll)
    {
        int ow = width * factor, oh = height * factor;
        for (int i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            var made = await upscaleTile(TileInput(rgba, width, height, t, alpha), t.ReadWidth, t.ReadHeight, lastOfAll && i == tiles.Count - 1);
            int mw = t.ReadWidth * factor, plane = mw * t.ReadHeight * factor;
            int fromX = (t.KeepX - t.ReadX) * factor, fromY = (t.KeepY - t.ReadY) * factor;
            int toX = t.KeepX * factor, toY = t.KeepY * factor;
            int keepWidth = Math.Min(t.KeepWidth * factor, ow - toX), keepHeight = Math.Min(t.KeepHeight * factor, oh - toY);
            Parallel.For(0, keepHeight, y =>
            {
                for (int x = 0; x < keepWidth; x++)
                {
                    int from = (fromY + y) * mw + fromX + x, to = ((toY + y) * ow + toX + x) * 4;
                    if (alpha) output[to + 3] = ToByte(0.299f * made[from] + 0.587f * made[plane + from] + 0.114f * made[2 * plane + from]);
                    else
                        for (int c = 0; c < 3; c++) output[to + c] = ToByte(made[c * plane + from]);
                }
            });
            await tileDone();
        }
    }

    /// <summary>
    /// What a tile reads, as planar values from 0 to 1: the picture's colour, or its alpha as grey, past its right and bottom
    /// edges reflected about the edge pixel — as PyTorch's <c>reflect</c> pad makes the padded picture.
    /// </summary>
    private static float[] TileInput(byte[] rgba, int width, int height, TileArea t, bool alpha)
    {
        int plane = t.ReadWidth * t.ReadHeight;
        var input = new float[3 * plane];
        for (int y = 0; y < t.ReadHeight; y++)
        {
            int sy = Reflect(t.ReadY + y, height);
            for (int x = 0; x < t.ReadWidth; x++)
            {
                int at = (sy * width + Reflect(t.ReadX + x, width)) * 4;
                for (int c = 0; c < 3; c++) input[c * plane + y * t.ReadWidth + x] = (alpha ? rgba[at + 3] : rgba[at + c]) / 255f;
            }
        }
        return input;
    }

    private static int Reflect(int i, int size) => i < size ? i : 2 * size - 2 - i;

    private static async Task<float[]> ThroughTiles(float[] planar, int width, int height, int factor, IReadOnlyList<TileArea> tiles,
                                                    Func<float[], int, int, bool, Task<float[]>> upscaleTile, Func<Task> tileDone, bool lastOfAll)
    {
        int ow = width * factor, oh = height * factor;
        var output = new float[3 * ow * oh];
        for (int i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i];
            var input = new float[3 * t.ReadWidth * t.ReadHeight];
            for (int c = 0; c < 3; c++)
                for (int y = 0; y < t.ReadHeight; y++)
                    planar.AsSpan(c * width * height + (t.ReadY + y) * width + t.ReadX, t.ReadWidth)
                          .CopyTo(input.AsSpan(c * t.ReadWidth * t.ReadHeight + y * t.ReadWidth));

            var made = await upscaleTile(input, t.ReadWidth, t.ReadHeight, lastOfAll && i == tiles.Count - 1);
            int mw = t.ReadWidth * factor, mh = t.ReadHeight * factor;
            int fromX = (t.KeepX - t.ReadX) * factor, fromY = (t.KeepY - t.ReadY) * factor;
            for (int c = 0; c < 3; c++)
                for (int y = 0; y < t.KeepHeight * factor; y++)
                    made.AsSpan(c * mw * mh + (fromY + y) * mw + fromX, t.KeepWidth * factor)
                        .CopyTo(output.AsSpan(c * ow * oh + (t.KeepY * factor + y) * ow + t.KeepX * factor));
            await tileDone();
        }
        return output;
    }

    private static bool IsOpaque(byte[] rgba)
    {
        for (int i = 3; i < rgba.Length; i += 4)
            if (rgba[i] != 255) return false;
        return true;
    }

    /// <summary>The picture's colour as planar values from 0 to 1, as the face detector reads it.</summary>
    internal static float[] Planar(byte[] rgba, int width, int height)
    {
        int plane = width * height;
        var planar = new float[3 * plane];
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int at = (y * width + x) * 4;
                for (int c = 0; c < 3; c++) planar[c * plane + y * width + x] = rgba[at + c] / 255f;
            }
        });
        return planar;
    }

    private static byte ToByte(float v) => (byte)Math.Clamp(MathF.Round(Math.Clamp(v, 0f, 1f) * 255f, MidpointRounding.ToEven), 0, 255);
}

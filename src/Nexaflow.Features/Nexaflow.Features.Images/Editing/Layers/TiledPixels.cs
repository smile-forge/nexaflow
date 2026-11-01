using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Nexaflow.Features.Images.Editing.Selection;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Layers;

/// <summary>Where a tile sits: its column and row, counted in tiles from the canvas's top-left — negative past it.</summary>
public readonly record struct TileKey(int Column, int Row);

/// <summary>
/// A pixel layer's pixels: premultiplied 8-bit RGBA held in square tiles of <see cref="TileSize"/> pixels, in canvas
/// coordinates, only the tiles painted — which may lie past the canvas's edges. A tile is never changed once made: every
/// change makes new pixels holding new tiles only where it changed something and sharing every other, so an undo step costs
/// the tiles it changed. A tile wholly transparent is not held.
/// </summary>
public sealed class TiledPixels
{
    public const int TileSize = 256;

    private const int TileShift = 8, TileMask = TileSize - 1, RowBytes = TileSize * 4, TileBytes = TileSize * RowBytes;

    private static readonly ConditionalWeakTable<byte[], SKImage> Images = new();

    // Never written once the pixels are made.
    private readonly ImmutableDictionary<TileKey, byte[]> _tiles;
    private SKRectI? _bounds;

    private TiledPixels(ImmutableDictionary<TileKey, byte[]> tiles) => _tiles = tiles;

    public static TiledPixels Empty { get; } = new(ImmutableDictionary<TileKey, byte[]>.Empty);

    /// <summary>Nothing painted anywhere.</summary>
    public bool IsEmpty => _tiles.IsEmpty;

    /// <summary>The tiles held.</summary>
    public IEnumerable<TileKey> Tiles => _tiles.Keys;

    /// <summary>The smallest rectangle holding every pixel not wholly transparent; empty where none is.</summary>
    public SKRectI Bounds => _bounds ??= MeasureBounds();

    /// <summary>The pixels of <paramref name="bitmap"/> placed with its top-left at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public static TiledPixels FromBitmap(SKBitmap bitmap, int x, int y)
    {
        using var premul = AsPremulRgba(bitmap);
        var rect = new SKRectI(x, y, x + premul.Width, y + premul.Height);
        var pixels = new byte[premul.Width * premul.Height * 4];
        var origin = premul.GetPixels();
        for (int row = 0; row < premul.Height; row++)
            Marshal.Copy(origin + row * premul.RowBytes, pixels, row * premul.Width * 4, premul.Width * 4);
        return Empty.Written(rect, pixels);
    }

    /// <summary>The pixels of <paramref name="rect"/> as a premultiplied RGBA bitmap the caller owns; transparent where nothing is painted.</summary>
    public SKBitmap ToBitmap(SKRectI rect)
    {
        var bitmap = new SKBitmap(new SKImageInfo(rect.Width, rect.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var pixels = Read(rect);
        var origin = bitmap.GetPixels();
        for (int row = 0; row < rect.Height; row++)
            Marshal.Copy(pixels, row * rect.Width * 4, origin + row * bitmap.RowBytes, rect.Width * 4);
        return bitmap;
    }

    /// <summary>The pixels of <paramref name="rect"/>, premultiplied RGBA, row by row; transparent where nothing is painted.</summary>
    public byte[] Read(SKRectI rect)
    {
        var pixels = new byte[rect.Width * rect.Height * 4];
        ReadInto(rect, pixels);
        return pixels;
    }

    /// <summary>The pixels of <paramref name="rect"/> into <paramref name="into"/>, premultiplied RGBA, row by row.</summary>
    public void ReadInto(SKRectI rect, Span<byte> into)
    {
        if (into.Length < rect.Width * rect.Height * 4) throw new ArgumentException("There is not room for the pixels.", nameof(into));
        into[..(rect.Width * rect.Height * 4)].Clear();
        if (rect.IsEmpty) return;
        foreach (var (key, tile, overlap) in Overlapping(rect))
        {
            if (tile is null) continue;
            for (int y = overlap.Top; y < overlap.Bottom; y++)
                tile.AsSpan(Offset(overlap.Left, y), overlap.Width * 4)
                    .CopyTo(into.Slice(((y - rect.Top) * rect.Width + overlap.Left - rect.Left) * 4, overlap.Width * 4));
        }
    }

    /// <summary>
    /// These pixels with <paramref name="rect"/> given by <paramref name="pixels"/> — premultiplied RGBA, row by row: a
    /// tile it does not touch is shared, one it covers made anew, one it covers part of copied with that part written.
    /// </summary>
    public TiledPixels Written(SKRectI rect, ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length < rect.Width * rect.Height * 4) throw new ArgumentException("The pixels do not fill the rectangle.", nameof(pixels));
        if (rect.IsEmpty) return this;
        var tiles = _tiles.ToBuilder();
        foreach (var (key, held, overlap) in Overlapping(rect))
        {
            var tile = overlap == TileRect(key) ? new byte[TileBytes] : held is null ? new byte[TileBytes] : (byte[])held.Clone();
            for (int y = overlap.Top; y < overlap.Bottom; y++)
                pixels.Slice(((y - rect.Top) * rect.Width + overlap.Left - rect.Left) * 4, overlap.Width * 4)
                      .CopyTo(tile.AsSpan(Offset(overlap.Left, y)));
            Put(tiles, key, tile);
        }
        return new(tiles.ToImmutable());
    }

    /// <summary>
    /// <paramref name="colour"/> laid over these pixels as much as <paramref name="where"/> covers each — a canvas-sized
    /// coverage, read as uncovered past the canvas. Where <paramref name="keepAlpha"/>, the colour only recolours what is
    /// painted, as much as it is painted: the transparency stays as it was.
    /// </summary>
    public TiledPixels Filled(TiledCoverage where, SKColor colour, bool keepAlpha = false)
    {
        var area = where.Bounds;
        if (area.IsEmpty) return this;
        int ca = colour.Alpha, cr = colour.Red, cg = colour.Green, cb = colour.Blue;
        return Changed(area, where, keepAlpha, (px, cover) =>
        {
            for (int i = 0, p = 0; i < cover.Length; i++, p += 4)
            {
                int a = cover[i];
                if (a == 0) continue;
                if (keepAlpha)
                {
                    int da = px[p + 3];
                    int sr = cr * da / 255, sg = cg * da / 255, sb = cb * da / 255;
                    px[p] = (byte)((px[p] * (255 - a) + sr * a + 127) / 255);
                    px[p + 1] = (byte)((px[p + 1] * (255 - a) + sg * a + 127) / 255);
                    px[p + 2] = (byte)((px[p + 2] * (255 - a) + sb * a + 127) / 255);
                    continue;
                }
                int sa = ca * a / 255;
                int inverse = 255 - sa;
                px[p] = (byte)((cr * sa + px[p] * inverse + 127) / 255);
                px[p + 1] = (byte)((cg * sa + px[p + 1] * inverse + 127) / 255);
                px[p + 2] = (byte)((cb * sa + px[p + 2] * inverse + 127) / 255);
                px[p + 3] = (byte)(sa + (px[p + 3] * inverse + 127) / 255);
            }
        });
    }

    /// <summary>These pixels cleared as much as <paramref name="where"/> covers each — a canvas-sized coverage, read as
    /// uncovered past the canvas.</summary>
    public TiledPixels Cleared(TiledCoverage where)
    {
        var area = SKRectI.Intersect(where.Bounds, Bounds);
        if (area.IsEmpty) return this;
        return Changed(area, where, keepAlpha: false, (px, cover) =>
        {
            for (int i = 0, p = 0; i < cover.Length; i++, p += 4)
            {
                int keep = 255 - cover[i];
                if (keep == 255) continue;
                for (int c = 0; c < 4; c++) px[p + c] = (byte)((px[p + c] * keep + 127) / 255);
            }
        });
    }

    /// <summary>These pixels kept only as much as <paramref name="where"/> covers each — a canvas-sized coverage, read as
    /// uncovered past the canvas, so whatever lies past it goes.</summary>
    public TiledPixels KeptWhere(TiledCoverage where)
    {
        var painted = Bounds;
        if (painted.IsEmpty) return this;
        var canvas = new SKRectI(0, 0, where.Width, where.Height);
        var kept = Changed(SKRectI.Intersect(painted, canvas), where, keepAlpha: false, (px, cover) =>
        {
            for (int i = 0, p = 0; i < cover.Length; i++, p += 4)
            {
                int keep = cover[i];
                if (keep == 255) continue;
                for (int c = 0; c < 4; c++) px[p + c] = (byte)((px[p + c] * keep + 127) / 255);
            }
        });
        // Past the canvas nothing is covered: whole tiles there go, and the parts of edge tiles past it are cleared.
        var tiles = kept._tiles.ToBuilder();
        foreach (var key in kept._tiles.Keys)
        {
            var rect = TileRect(key);
            if (canvas.Contains(rect)) continue;
            var inside = SKRectI.Intersect(rect, canvas);
            if (inside.IsEmpty) { tiles.Remove(key); continue; }
            var tile = (byte[])kept._tiles[key].Clone();
            for (int y = rect.Top; y < rect.Bottom; y++)
                for (int x = rect.Left; x < rect.Right; x++)
                    if (!inside.Contains(x, y)) tile.AsSpan(Offset(x, y), 4).Clear();
            Put(tiles, key, tile);
        }
        return new(tiles.ToImmutable());
    }

    /// <summary>How opaque each pixel is, as coverage over a <paramref name="width"/> × <paramref name="height"/> canvas.</summary>
    public TiledCoverage Alpha(int width, int height)
    {
        var canvas = new SKRectI(0, 0, width, height);
        var area = SKRectI.Intersect(Bounds, canvas);
        var coverage = TiledCoverage.Clear(width, height);
        if (area.IsEmpty) return coverage;
        var pixels = Read(area);
        var alpha = new byte[area.Width * area.Height];
        for (int i = 0; i < alpha.Length; i++) alpha[i] = pixels[i * 4 + 3];
        return coverage.Written(area, alpha);
    }

    /// <summary>The bytes of the tiles these pixels hold that <paramref name="other"/> does not share — what keeping these
    /// as an undo step costs beside it.</summary>
    public long BytesNotIn(TiledPixels? other)
    {
        long bytes = 0;
        foreach (var (key, tile) in _tiles)
            if (other is null || !other._tiles.TryGetValue(key, out var theirs) || !ReferenceEquals(theirs, tile)) bytes += TileBytes;
        return bytes;
    }

    /// <summary>Whether this and <paramref name="other"/> hold the very same tile at <paramref name="key"/>, or neither holds one.</summary>
    public bool SharesTile(TiledPixels other, TileKey key)
    {
        bool mine = _tiles.TryGetValue(key, out var a), theirs = other._tiles.TryGetValue(key, out var b);
        return mine == theirs && ReferenceEquals(a, b);
    }

    /// <summary>The rectangles, in canvas pixels, of every tile this and <paramref name="other"/> do not share — where the two differ.</summary>
    public IEnumerable<SKRectI> ChangedTileRects(TiledPixels other)
    {
        foreach (var (key, tile) in _tiles)
            if (!other._tiles.TryGetValue(key, out var theirs) || !ReferenceEquals(theirs, tile)) yield return TileRect(key);
        foreach (var key in other._tiles.Keys)
            if (!_tiles.ContainsKey(key)) yield return TileRect(key);
    }

    /// <summary>These pixels moved by whole pixels: by whole tiles the tiles are shared, re-keyed; otherwise each is re-cut.</summary>
    public TiledPixels Moved(int dx, int dy)
    {
        if (dx == 0 && dy == 0 || IsEmpty) return this;
        if ((dx & TileMask) == 0 && (dy & TileMask) == 0)
            return new(_tiles.ToImmutableDictionary(p => new TileKey(p.Key.Column + (dx >> TileShift), p.Key.Row + (dy >> TileShift)), p => p.Value));
        var moved = Empty;
        foreach (var key in _tiles.Keys)
        {
            var rect = TileRect(key);
            moved = moved.Written(new SKRectI(rect.Left + dx, rect.Top + dy, rect.Right + dx, rect.Bottom + dy), Read(rect));
        }
        return moved;
    }

    /// <summary>
    /// Draws the tiles that meet <paramref name="clip"/> (in canvas pixels) onto <paramref name="canvas"/>, at their places in
    /// its current matrix, with <paramref name="paint"/>. Each tile is made a Skia image once, the first time it is drawn.
    /// </summary>
    public void DrawTo(SKCanvas canvas, SKRect clip, SKSamplingOptions sampling, SKPaint? paint = null)
    {
        foreach (var (key, tile) in _tiles)
        {
            var rect = TileRect(key);
            if (!clip.IntersectsWith(rect)) continue;
            var image = Images.GetValue(tile, ImageOf);
            canvas.DrawImage(image, rect.Left, rect.Top, sampling, paint);
        }
    }

    private static SKImage ImageOf(byte[] tile)
        => SKImage.FromPixelCopy(new SKImageInfo(TileSize, TileSize, SKColorType.Rgba8888, SKAlphaType.Premul), tile)
           ?? throw new InvalidOperationException("A tile could not be made an image.");

    // ── Tiles ───────────────────────────────────────────────────────────────

    private static SKRectI TileRect(TileKey key)
        => new(key.Column << TileShift, key.Row << TileShift, (key.Column + 1) << TileShift, (key.Row + 1) << TileShift);

    private static int Offset(int x, int y) => ((y & TileMask) * TileSize + (x & TileMask)) * 4;

    private IEnumerable<(TileKey Key, byte[]? Tile, SKRectI Overlap)> Overlapping(SKRectI rect)
    {
        for (int row = rect.Top >> TileShift; row <= (rect.Bottom - 1) >> TileShift; row++)
            for (int column = rect.Left >> TileShift; column <= (rect.Right - 1) >> TileShift; column++)
            {
                var key = new TileKey(column, row);
                yield return (key, _tiles.GetValueOrDefault(key), SKRectI.Intersect(TileRect(key), rect));
            }
    }

    /// <summary>A tile just made put in its place — or, wholly transparent, its place emptied.</summary>
    private static void Put(ImmutableDictionary<TileKey, byte[]>.Builder tiles, TileKey key, byte[] tile)
    {
        bool painted = false;
        for (int p = 3; p < TileBytes; p += 4)
            if (tile[p] != 0) { painted = true; break; }
        if (painted) tiles[key] = tile;
        else tiles.Remove(key);
    }

    /// <summary>These pixels with <paramref name="area"/> changed by <paramref name="change"/>, handed each tile's pixels in that
    /// area and the coverage of <paramref name="where"/> over them, row by row.</summary>
    private TiledPixels Changed(SKRectI area, TiledCoverage where, bool keepAlpha, Action<byte[], byte[]> change)
    {
        var canvas = new SKRectI(0, 0, where.Width, where.Height);
        area = SKRectI.Intersect(area, canvas);
        if (area.IsEmpty) return this;
        var tiles = _tiles.ToBuilder();
        foreach (var (key, held, overlap) in Overlapping(area))
        {
            if (held is null && keepAlpha) continue;   // nothing painted to recolour
            var pixels = new byte[overlap.Width * overlap.Height * 4];
            if (held is not null)
                for (int y = overlap.Top; y < overlap.Bottom; y++)
                    held.AsSpan(Offset(overlap.Left, y), overlap.Width * 4).CopyTo(pixels.AsSpan((y - overlap.Top) * overlap.Width * 4));
            var cover = where.Read(overlap);
            change(pixels, cover);
            var tile = held is null ? new byte[TileBytes] : (byte[])held.Clone();
            for (int y = overlap.Top; y < overlap.Bottom; y++)
                pixels.AsSpan((y - overlap.Top) * overlap.Width * 4, overlap.Width * 4).CopyTo(tile.AsSpan(Offset(overlap.Left, y)));
            Put(tiles, key, tile);
        }
        return new(tiles.ToImmutable());
    }

    private SKRectI MeasureBounds()
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var (key, tile) in _tiles)
        {
            var rect = TileRect(key);
            for (int y = 0; y < TileSize; y++)
                for (int x = 0; x < TileSize; x++)
                {
                    if (tile[(y * TileSize + x) * 4 + 3] == 0) continue;
                    left = Math.Min(left, rect.Left + x);
                    right = Math.Max(right, rect.Left + x);
                    top = Math.Min(top, rect.Top + y);
                    bottom = Math.Max(bottom, rect.Top + y);
                }
        }
        return right == int.MinValue ? SKRectI.Empty : new SKRectI(left, top, right + 1, bottom + 1);
    }

    /// <summary><paramref name="bitmap"/> as premultiplied RGBA, converted where it is not; the caller owns the result.</summary>
    private static SKBitmap AsPremulRgba(SKBitmap bitmap)
    {
        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var converted = new SKBitmap(info);
        using var image = SKImage.FromBitmap(bitmap);
        if (image is null || !image.ReadPixels(info, converted.GetPixels(), converted.RowBytes, 0, 0))
        {
            converted.Dispose();
            throw new InvalidOperationException("The picture could not be read as pixels.");
        }
        return converted;
    }
}

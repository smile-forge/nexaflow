using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Nexaflow.Features.Images.Editing.Selection;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Layers;

/// <summary>Where a tile sits: its column and row, counted in tiles from the canvas's top-left — negative past it.</summary>
public readonly record struct TileKey(int Column, int Row);

/// <summary>
/// A pixel layer's pixels, held in square tiles of <see cref="TileSize"/> pixels in its picture's <see cref="Format"/> — 8-bit
/// premultiplied colour, or deeper colour or grey — in canvas coordinates, only the tiles painted, which may lie past the
/// canvas's edges. A tile is never changed once made: every change makes new pixels holding new tiles only where it changed
/// something and sharing every other, so an undo step costs the tiles it changed. A tile wholly transparent is not held.
/// </summary>
public sealed class TiledPixels
{
    public const int TileSize = TileFormat.TileSize;

    private const int TileShift = 8, TileMask = TileSize - 1;

    // Never written once the pixels are made.
    private readonly ImmutableDictionary<TileKey, byte[]> _tiles;
    private SKRectI? _bounds;

    private TiledPixels(TileFormat format, ImmutableDictionary<TileKey, byte[]> tiles)
    {
        Format = format;
        _tiles = tiles;
    }

    /// <summary>Nothing painted, in 8-bit colour.</summary>
    public static TiledPixels Empty { get; } = new(TileFormat.Rgb8, ImmutableDictionary<TileKey, byte[]>.Empty);

    /// <summary>Nothing painted, in <paramref name="format"/>.</summary>
    public static TiledPixels EmptyIn(TileFormat format) => format == TileFormat.Rgb8 ? Empty : new(format, ImmutableDictionary<TileKey, byte[]>.Empty);

    /// <summary>How each tile's bytes hold its pixels.</summary>
    public TileFormat Format { get; }

    /// <summary>Nothing painted anywhere.</summary>
    public bool IsEmpty => _tiles.IsEmpty;

    /// <summary>The tiles held.</summary>
    public IEnumerable<TileKey> Tiles => _tiles.Keys;

    /// <summary>The smallest rectangle holding every pixel not wholly transparent; empty where none is.</summary>
    public SKRectI Bounds => _bounds ??= MeasureBounds();

    /// <summary>The pixels of <paramref name="bitmap"/> placed with its top-left at (<paramref name="x"/>, <paramref name="y"/>), in 8-bit colour.</summary>
    public static TiledPixels FromBitmap(SKBitmap bitmap, int x, int y) => FromBitmap(bitmap, x, y, TileFormat.Rgb8);

    /// <summary>The pixels of <paramref name="bitmap"/> placed with its top-left at (<paramref name="x"/>, <paramref name="y"/>),
    /// converted into <paramref name="format"/> from whatever colour, depth and space the bitmap is in.</summary>
    public static TiledPixels FromBitmap(SKBitmap bitmap, int x, int y, TileFormat format)
        => EmptyIn(format).Written(new SKRectI(x, y, x + bitmap.Width, y + bitmap.Height), format.PixelsOf(bitmap));

    /// <summary>The pixels of <paramref name="rect"/> as a bitmap of <see cref="TileFormat.BitmapInfo"/> the caller owns —
    /// 8-bit premultiplied colour, or deeper colour not premultiplied; transparent where nothing is painted.</summary>
    public SKBitmap ToBitmap(SKRectI rect) => Format.BitmapOf(Read(rect), rect.Width, rect.Height);

    /// <summary>The pixels of <paramref name="rect"/> in this <see cref="Format"/>, row by row; transparent where nothing is painted.</summary>
    public byte[] Read(SKRectI rect)
    {
        var pixels = new byte[rect.Width * rect.Height * Format.PixelBytes];
        ReadInto(rect, pixels);
        return pixels;
    }

    /// <summary>The pixels of <paramref name="rect"/> into <paramref name="into"/>, in this <see cref="Format"/>, row by row.</summary>
    public void ReadInto(SKRectI rect, Span<byte> into)
    {
        int size = Format.PixelBytes;
        if (into.Length < rect.Width * rect.Height * size) throw new ArgumentException("There is not room for the pixels.", nameof(into));
        into[..(rect.Width * rect.Height * size)].Clear();
        if (rect.IsEmpty) return;
        foreach (var (key, tile, overlap) in Overlapping(rect))
        {
            if (tile is null) continue;
            for (int y = overlap.Top; y < overlap.Bottom; y++)
                tile.AsSpan(Offset(overlap.Left, y), overlap.Width * size)
                    .CopyTo(into.Slice(((y - rect.Top) * rect.Width + overlap.Left - rect.Left) * size, overlap.Width * size));
        }
    }

    /// <summary>The pixels of <paramref name="rect"/> as premultiplied red, green, blue and alpha, four floats a pixel in the
    /// picture's values — <see cref="TileFormat.ToPremul"/> — row by row.</summary>
    public float[] ReadPremul(SKRectI rect)
    {
        var values = new float[rect.Width * rect.Height * 4];
        Format.ToPremul(Read(rect), values);
        return values;
    }

    /// <summary>
    /// These pixels with <paramref name="rect"/> given by <paramref name="pixels"/> — in this <see cref="Format"/>, row by row: a
    /// tile it does not touch is shared, one it covers made anew, one it covers part of copied with that part written.
    /// </summary>
    public TiledPixels Written(SKRectI rect, ReadOnlySpan<byte> pixels)
    {
        int size = Format.PixelBytes;
        if (pixels.Length < rect.Width * rect.Height * size) throw new ArgumentException("The pixels do not fill the rectangle.", nameof(pixels));
        if (rect.IsEmpty) return this;
        var tiles = _tiles.ToBuilder();
        foreach (var (key, held, overlap) in Overlapping(rect))
        {
            var tile = overlap == TileRect(key) || held is null ? new byte[Format.TileBytes] : (byte[])held.Clone();
            for (int y = overlap.Top; y < overlap.Bottom; y++)
                pixels.Slice(((y - rect.Top) * rect.Width + overlap.Left - rect.Left) * size, overlap.Width * size)
                      .CopyTo(tile.AsSpan(Offset(overlap.Left, y)));
            Put(tiles, key, tile);
        }
        return new(Format, tiles.ToImmutable());
    }

    /// <summary>These pixels with <paramref name="rect"/> given by <paramref name="premul"/> — premultiplied red, green, blue and
    /// alpha, four floats a pixel in the picture's values, row by row — written in this <see cref="Format"/>.</summary>
    public TiledPixels WrittenPremul(SKRectI rect, ReadOnlySpan<float> premul)
    {
        var pixels = new byte[rect.Width * rect.Height * Format.PixelBytes];
        Format.FromPremul(premul, pixels);
        return Written(rect, pixels);
    }

    /// <summary>
    /// <paramref name="colour"/> — in the picture's values, not premultiplied — laid over these pixels as much as
    /// <paramref name="where"/> covers each: a canvas-sized coverage, read as uncovered past the canvas. Where
    /// <paramref name="keepAlpha"/>, the colour only recolours what is painted, as much as it is painted: the transparency
    /// stays as it was.
    /// </summary>
    public TiledPixels Filled(TiledCoverage where, SKColorF colour, bool keepAlpha = false)
    {
        var area = where.Bounds;
        if (area.IsEmpty) return this;
        float ca = Math.Clamp(colour.Alpha, 0, 1), cr = colour.Red, cg = colour.Green, cb = colour.Blue;
        return Changed(area, where, keepAlpha, (px, cover) =>
        {
            for (int i = 0, p = 0; i < cover.Length; i++, p += 4)
            {
                float a = cover[i];
                if (a <= 0) continue;
                if (keepAlpha)
                {
                    float da = px[p + 3];
                    px[p] = px[p] * (1 - a) + cr * da * a;
                    px[p + 1] = px[p + 1] * (1 - a) + cg * da * a;
                    px[p + 2] = px[p + 2] * (1 - a) + cb * da * a;
                    continue;
                }
                float sa = ca * a, inverse = 1 - sa;
                px[p] = cr * sa + px[p] * inverse;
                px[p + 1] = cg * sa + px[p + 1] * inverse;
                px[p + 2] = cb * sa + px[p + 2] * inverse;
                px[p + 3] = sa + px[p + 3] * inverse;
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
                float keep = 1 - cover[i];
                if (keep >= 1) continue;
                for (int c = 0; c < 4; c++) px[p + c] *= keep;
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
                float keep = cover[i];
                if (keep >= 1) continue;
                for (int c = 0; c < 4; c++) px[p + c] *= keep;
            }
        });
        // Past the canvas nothing is covered: whole tiles there go, and the parts of edge tiles past it are cleared.
        int size = Format.PixelBytes;
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
                    if (!inside.Contains(x, y)) tile.AsSpan(Offset(x, y), size).Clear();
            Put(tiles, key, tile);
        }
        return new(Format, tiles.ToImmutable());
    }

    /// <summary>How opaque each pixel is, as coverage over a <paramref name="width"/> × <paramref name="height"/> canvas, held
    /// as deep as these pixels are.</summary>
    public TiledCoverage Alpha(int width, int height)
    {
        var canvas = new SKRectI(0, 0, width, height);
        var area = SKRectI.Intersect(Bounds, canvas);
        var coverage = TiledCoverage.Clear(width, height, Format.Colour.Depth);
        if (area.IsEmpty) return coverage;
        var premul = ReadPremul(area);
        var alpha = new float[area.Width * area.Height];
        for (int i = 0; i < alpha.Length; i++) alpha[i] = premul[i * 4 + 3];
        return coverage.WrittenValues(area, alpha);
    }

    /// <summary>
    /// These pixels in <paramref name="format"/>: every tile carried across — colour made grey, grey spread to colour, light
    /// made linear or encoded again where one of the two is a 32-bit format and the other is not, and clamped to 0…1 where
    /// the new format holds no more.
    /// </summary>
    public TiledPixels ConvertedTo(TileFormat format)
    {
        if (format == Format) return this;
        bool fromLinear = Format.Colour.IsLinear, toLinear = format.Colour.IsLinear;
        var tiles = ImmutableDictionary.CreateBuilder<TileKey, byte[]>();
        var values = new float[TileSize * TileSize * 4];
        foreach (var (key, tile) in _tiles)
        {
            Format.ToPremul(tile, values);
            if (fromLinear != toLinear) Retransferred(values, toLinear);
            var carried = new byte[format.TileBytes];
            format.FromPremul(values, carried);
            Put(tiles, key, carried, format);
        }
        return new(format, tiles.ToImmutable());
    }

    /// <summary>Premultiplied values put through sRGB's curve, or out of it: each colour unpremultiplied, carried, premultiplied again.</summary>
    private static void Retransferred(Span<float> premul, bool toLinear)
    {
        for (int p = 0; p < premul.Length; p += 4)
        {
            float a = premul[p + 3];
            if (a <= 0) continue;
            for (int c = 0; c < 3; c++)
            {
                float straight = premul[p + c] / a;
                premul[p + c] = (toLinear ? PictureColour.ToLinear(straight) : PictureColour.ToEncoded(straight)) * a;
            }
        }
    }

    /// <summary>The bytes of the tiles these pixels hold that <paramref name="other"/> does not share — what keeping these
    /// as an undo step costs beside it.</summary>
    public long BytesNotIn(TiledPixels? other)
    {
        long bytes = 0;
        foreach (var (key, tile) in _tiles)
            if (other is null || !other._tiles.TryGetValue(key, out var theirs) || !ReferenceEquals(theirs, tile)) bytes += tile.Length;
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
            return new(Format, _tiles.ToImmutableDictionary(p => new TileKey(p.Key.Column + (dx >> TileShift), p.Key.Row + (dy >> TileShift)), p => p.Value));
        var moved = EmptyIn(Format);
        foreach (var key in _tiles.Keys)
        {
            var rect = TileRect(key);
            moved = moved.Written(new SKRectI(rect.Left + dx, rect.Top + dy, rect.Right + dx, rect.Bottom + dy), Read(rect));
        }
        return moved;
    }

    /// <summary>
    /// Draws the tiles that meet <paramref name="clip"/> (in canvas pixels) onto <paramref name="canvas"/>, at their places in
    /// its current matrix, with <paramref name="paint"/> — each as its <see cref="Format"/> draws it.
    /// </summary>
    public void DrawTo(SKCanvas canvas, SKRect clip, SKSamplingOptions sampling, SKPaint? paint = null)
    {
        foreach (var (key, tile) in _tiles)
        {
            var rect = TileRect(key);
            if (!clip.IntersectsWith(rect)) continue;
            Format.Draw(canvas, tile, rect.Left, rect.Top, sampling, paint);
        }
    }

    // ── Tiles ───────────────────────────────────────────────────────────────

    private static SKRectI TileRect(TileKey key)
        => new(key.Column << TileShift, key.Row << TileShift, (key.Column + 1) << TileShift, (key.Row + 1) << TileShift);

    private int Offset(int x, int y) => ((y & TileMask) * TileSize + (x & TileMask)) * Format.PixelBytes;

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
    private void Put(ImmutableDictionary<TileKey, byte[]>.Builder tiles, TileKey key, byte[] tile) => Put(tiles, key, tile, Format);

    private static void Put(ImmutableDictionary<TileKey, byte[]>.Builder tiles, TileKey key, byte[] tile, TileFormat format)
    {
        if (format.AnyPainted(tile)) tiles[key] = tile;
        else tiles.Remove(key);
    }

    /// <summary>These pixels with <paramref name="area"/> changed by <paramref name="change"/>, handed each tile's pixels in that
    /// area as premultiplied values and the coverage of <paramref name="where"/> over them, 0 to 1, row by row.</summary>
    private TiledPixels Changed(SKRectI area, TiledCoverage where, bool keepAlpha, Action<float[], float[]> change)
    {
        var canvas = new SKRectI(0, 0, where.Width, where.Height);
        area = SKRectI.Intersect(area, canvas);
        if (area.IsEmpty) return this;
        int size = Format.PixelBytes;
        var tiles = _tiles.ToBuilder();
        foreach (var (key, held, overlap) in Overlapping(area))
        {
            if (held is null && keepAlpha) continue;   // nothing painted to recolour
            var pixels = new byte[overlap.Width * overlap.Height * size];
            if (held is not null)
                for (int y = overlap.Top; y < overlap.Bottom; y++)
                    held.AsSpan(Offset(overlap.Left, y), overlap.Width * size).CopyTo(pixels.AsSpan((y - overlap.Top) * overlap.Width * size));
            var premul = new float[overlap.Width * overlap.Height * 4];
            Format.ToPremul(pixels, premul);
            change(premul, where.ReadValues(overlap));
            Format.FromPremul(premul, pixels);
            var tile = held is null ? new byte[Format.TileBytes] : (byte[])held.Clone();
            for (int y = overlap.Top; y < overlap.Bottom; y++)
                pixels.AsSpan((y - overlap.Top) * overlap.Width * size, overlap.Width * size).CopyTo(tile.AsSpan(Offset(overlap.Left, y)));
            Put(tiles, key, tile);
        }
        return new(Format, tiles.ToImmutable());
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
                    if (Format.IsClearAt(tile, y * TileSize + x)) continue;
                    left = Math.Min(left, rect.Left + x);
                    right = Math.Max(right, rect.Left + x);
                    top = Math.Min(top, rect.Top + y);
                    bottom = Math.Max(bottom, rect.Top + y);
                }
        }
        return right == int.MinValue ? SKRectI.Empty : new SKRectI(left, top, right + 1, bottom + 1);
    }

    /// <summary>
    /// These pixels drawn through <paramref name="matrix"/> onto a <paramref name="width"/> × <paramref name="height"/> canvas —
    /// scaled, turned, flipped — resampled once, as one picture so no seam shows between tiles; what falls off the canvas goes.
    /// Resampled on a surface as deep as the picture.
    /// </summary>
    public TiledPixels Transformed(SKMatrix matrix, int width, int height)
    {
        if (IsEmpty) return this;
        var bounds = Bounds;
        var mapped = matrix.MapRect(SKRect.Create(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
        var area = SKRectI.Intersect(SKRectI.Round(SKRect.Inflate(mapped, 1, 1)), new SKRectI(0, 0, width, height));
        if (area.IsEmpty) return EmptyIn(Format);
        using var source = ToBitmap(bounds);
        using var target = new SKBitmap(Format.Colour.SurfaceInfo(area.Width, area.Height));
        target.Erase(SKColors.Transparent);
        using (var canvas = new SKCanvas(target))
        using (var image = SKImage.FromPixelCopy(source.Info, source.GetPixels(), source.RowBytes))
        using (var paint = new SKPaint { IsAntialias = true })
        {
            canvas.Translate(-area.Left, -area.Top);
            canvas.Concat(matrix);
            canvas.DrawImage(image, bounds.Left, bounds.Top, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        }
        return FromBitmap(target, area.Left, area.Top, Format);
    }

    /// <summary>These pixels with <paramref name="above"/> laid over them, as a layer merged down onto this one at full opacity
    /// lays its pixels — only the tiles it holds are touched.</summary>
    public TiledPixels MergedWith(TiledPixels above)
    {
        if (above.IsEmpty) return this;
        above = above.ConvertedTo(Format);
        var tiles = _tiles.ToBuilder();
        var top = new float[TileSize * TileSize * 4];
        var bottom = new float[TileSize * TileSize * 4];
        foreach (var (key, upper) in above._tiles)
        {
            if (!_tiles.TryGetValue(key, out var lower))
            {
                Put(tiles, key, upper);
                continue;
            }
            Format.ToPremul(upper, top);
            Format.ToPremul(lower, bottom);
            for (int p = 0; p < top.Length; p += 4)
            {
                float keep = 1 - top[p + 3];
                for (int c = 0; c < 4; c++) top[p + c] += bottom[p + c] * keep;
            }
            var merged = new byte[Format.TileBytes];
            Format.FromPremul(top, merged);
            Put(tiles, key, merged);
        }
        return new TiledPixels(Format, tiles.ToImmutable());
    }
}

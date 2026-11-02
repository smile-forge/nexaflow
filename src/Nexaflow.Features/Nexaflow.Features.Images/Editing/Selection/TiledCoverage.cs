using System.Numerics;
using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;

namespace Nexaflow.Features.Images.Editing.Selection;

/// <summary>
/// Coverage over a canvas, a byte a pixel — 255 wholly covered, 0 not at all — held in square tiles of
/// <see cref="TileSize"/> pixels that are never changed once made: every change makes a new coverage holding new tiles
/// only where it changed something and sharing every other, so a history of selections and masks costs the tiles each
/// step changed. A tile not held is <see cref="Outside"/> throughout; a tile wholly covered or wholly clear is one tile
/// shared by every coverage.
/// </summary>
/// <remarks>
/// A tile is always <see cref="TileSize"/>² bytes, row by row, the ones at the canvas's right and bottom edges included;
/// what lies past the canvas in those is never read.
/// </remarks>
public sealed class TiledCoverage
{
    public const int TileSize = 256;

    private const int TileShift = 8, TileMask = TileSize - 1, TileArea = TileSize * TileSize;

    private static readonly byte[] CoveredTile = Uniform(255);
    private static readonly byte[] ClearTile = Uniform(0);

    // Never written once the coverage is made: a slot is null (Outside), CoveredTile, ClearTile or a tile of its own.
    private readonly byte[]?[] _tiles;
    private SKRectI? _bounds;

    private TiledCoverage(int width, int height, byte outside, byte[]?[] tiles)
    {
        Width = width;
        Height = height;
        Outside = outside;
        _tiles = tiles;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>The coverage of every pixel in a tile that is not held.</summary>
    public byte Outside { get; }

    public int Columns => (Width + TileMask) >> TileShift;

    public int Rows => (Height + TileMask) >> TileShift;

    /// <summary>Nothing covered, over a <paramref name="width"/> × <paramref name="height"/> canvas.</summary>
    public static TiledCoverage Clear(int width, int height) => new(width, height, 0, NewSlots(width, height));

    /// <summary>Everything covered, over a <paramref name="width"/> × <paramref name="height"/> canvas.</summary>
    public static TiledCoverage Covered(int width, int height) => new(width, height, 255, NewSlots(width, height));

    /// <summary>The coverage <paramref name="coverage"/> holds, a byte a pixel, row by row.</summary>
    public static TiledCoverage FromBytes(ReadOnlySpan<byte> coverage, int width, int height)
    {
        if (coverage.Length != width * height) throw new ArgumentException("The coverage is not the size given.", nameof(coverage));
        return Clear(width, height).Written(new SKRectI(0, 0, width, height), coverage);
    }

    /// <summary>Nothing is covered anywhere — whatever the outside, where every tile is held clear.</summary>
    public bool IsEmpty => Array.TrueForAll(_tiles, t => UniformOf(t, Outside) == 0);

    /// <summary>The coverage of one pixel.</summary>
    public byte this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is off the canvas.");
            var tile = _tiles[(y >> TileShift) * Columns + (x >> TileShift)];
            return tile is null ? Outside : tile[((y & TileMask) << TileShift) | (x & TileMask)];
        }
    }

    /// <summary>The smallest rectangle holding every pixel covered at all; empty where none is.</summary>
    public SKRectI Bounds => _bounds ??= MeasureBounds();

    /// <summary>The rectangle round every tile that is held — outside it, every pixel is <see cref="Outside"/>; empty
    /// where no tile is held.</summary>
    public SKRectI HeldBounds
    {
        get
        {
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            for (int row = 0; row < Rows; row++)
                for (int column = 0; column < Columns; column++)
                {
                    if (_tiles[row * Columns + column] is null) continue;
                    left = Math.Min(left, column);
                    top = Math.Min(top, row);
                    right = Math.Max(right, column);
                    bottom = Math.Max(bottom, row);
                }
            return right < 0 ? SKRectI.Empty : Clip(new SKRectI(left << TileShift, top << TileShift, (right + 1) << TileShift, (bottom + 1) << TileShift));
        }
    }

    /// <summary>Whether this and <paramref name="other"/> hold the very same tile at (<paramref name="column"/>,
    /// <paramref name="row"/>) — what a change that left it alone shares.</summary>
    public bool SharesTile(TiledCoverage other, int column, int row)
    {
        int slot = row * Columns + column;
        return other.Width == Width && other.Height == Height && ReferenceEquals(_tiles[slot], other._tiles[slot])
               && (_tiles[slot] is not null || Outside == other.Outside);
    }

    /// <summary>The bytes of the tiles this coverage holds of its own that <paramref name="other"/> does not share — what keeping
    /// it as an undo step costs beside it. The tiles every coverage shares cost nothing.</summary>
    public long BytesNotIn(TiledCoverage? other)
    {
        long bytes = 0;
        bool comparable = other is not null && other.Width == Width && other.Height == Height;
        for (int slot = 0; slot < _tiles.Length; slot++)
        {
            var tile = _tiles[slot];
            if (tile is null || ReferenceEquals(tile, CoveredTile) || ReferenceEquals(tile, ClearTile)) continue;
            if (comparable && ReferenceEquals(tile, other!._tiles[slot])) continue;
            bytes += TileArea;
        }
        return bytes;
    }

    /// <summary>This coverage drawn at <paramref name="width"/> × <paramref name="height"/>: averaged down, or smoothly up.</summary>
    public TiledCoverage Resampled(int width, int height)
    {
        if (width == Width && height == Height) return this;
        if (IsEmpty) return Clear(width, height);
        using var source = SKImage.FromPixelCopy(new SKImageInfo(Width, Height, SKColorType.Alpha8, SKAlphaType.Premul), ToBytes())
                           ?? throw new InvalidOperationException("The coverage could not be made an image.");
        var info = new SKImageInfo(width, height, SKColorType.Alpha8, SKAlphaType.Premul);
        using var scaled = new SKBitmap(info);
        using (var pixmap = scaled.PeekPixels())
            if (!source.ScalePixels(pixmap, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)))
                throw new InvalidOperationException("The coverage could not be resampled.");
        var bytes = new byte[width * height];
        var origin = scaled.GetPixels();
        for (int y = 0; y < height; y++)
            System.Runtime.InteropServices.Marshal.Copy(origin + y * scaled.RowBytes, bytes, y * width, width);
        return FromBytes(bytes, width, height);
    }

    /// <summary>
    /// This coverage moved by whole pixels onto a <paramref name="width"/> × <paramref name="height"/> canvas: what lands on it
    /// kept as it was, the rest of the new canvas <see cref="Outside"/> — a mask that reveals all still reveals the canvas grown.
    /// </summary>
    public TiledCoverage Shifted(int dx, int dy, int width, int height)
    {
        if (dx == 0 && dy == 0 && width == Width && height == Height) return this;
        var shifted = new TiledCoverage(width, height, Outside, NewSlots(width, height));
        var from = SKRectI.Intersect(new SKRectI(0, 0, Width, Height), new SKRectI(-dx, -dy, width - dx, height - dy));
        if (from.IsEmpty) return shifted;
        return shifted.Written(new SKRectI(from.Left + dx, from.Top + dy, from.Right + dx, from.Bottom + dy), Read(from));
    }

    /// <summary>The whole canvas, a byte a pixel, row by row.</summary>
    public byte[] ToBytes() => Read(new SKRectI(0, 0, Width, Height));

    /// <summary>The pixels of <paramref name="rect"/> — on the canvas — a byte each, row by row.</summary>
    public byte[] Read(SKRectI rect)
    {
        var pixels = new byte[rect.Width * rect.Height];
        ReadInto(rect, pixels);
        return pixels;
    }

    /// <summary>The pixels of <paramref name="rect"/> — on the canvas — into <paramref name="into"/>, a byte each, row by row.</summary>
    public void ReadInto(SKRectI rect, Span<byte> into)
    {
        RequireOnCanvas(rect);
        if (into.Length < rect.Width * rect.Height) throw new ArgumentException("There is not room for the pixels.", nameof(into));
        for (int row = rect.Top >> TileShift; row <= (rect.Bottom - 1) >> TileShift; row++)
            for (int column = rect.Left >> TileShift; column <= (rect.Right - 1) >> TileShift; column++)
            {
                var overlap = SKRectI.Intersect(TileRect(column, row), rect);
                var tile = _tiles[row * Columns + column];
                for (int y = overlap.Top; y < overlap.Bottom; y++)
                {
                    var line = into.Slice((y - rect.Top) * rect.Width + overlap.Left - rect.Left, overlap.Width);
                    if (tile is null) line.Fill(Outside);
                    else tile.AsSpan(((y & TileMask) << TileShift) | (overlap.Left & TileMask), overlap.Width).CopyTo(line);
                }
            }
    }

    /// <summary>
    /// This coverage with the pixels of <paramref name="rect"/> — on the canvas — given by <paramref name="pixels"/>, a byte
    /// each, row by row: a tile <paramref name="rect"/> does not touch is shared, one it covers is made anew, and one it
    /// covers part of is copied with that part written.
    /// </summary>
    public TiledCoverage Written(SKRectI rect, ReadOnlySpan<byte> pixels)
    {
        RequireOnCanvas(rect);
        if (pixels.Length < rect.Width * rect.Height) throw new ArgumentException("The pixels do not fill the rectangle.", nameof(pixels));
        var tiles = (byte[]?[])_tiles.Clone();
        if (rect.IsEmpty) return new(Width, Height, Outside, tiles);
        for (int row = rect.Top >> TileShift; row <= (rect.Bottom - 1) >> TileShift; row++)
            for (int column = rect.Left >> TileShift; column <= (rect.Right - 1) >> TileShift; column++)
            {
                var whole = TileRect(column, row);
                var overlap = SKRectI.Intersect(whole, rect);
                int slot = row * Columns + column;
                var tile = overlap == whole ? new byte[TileArea] : Materialized(_tiles[slot], Outside);
                for (int y = overlap.Top; y < overlap.Bottom; y++)
                    pixels.Slice((y - rect.Top) * rect.Width + overlap.Left - rect.Left, overlap.Width)
                          .CopyTo(tile.AsSpan(((y & TileMask) << TileShift) | (overlap.Left & TileMask)));
                tiles[slot] = Normalized(tile, whole, Outside);
            }
        return new(Width, Height, Outside, tiles);
    }

    /// <summary>
    /// <paramref name="shape"/> — the same size — met with this as <paramref name="mode"/> says: replacing it, the more
    /// covered of the two, this as much as the shape leaves uncovered, or the less covered of the two. A tile where the
    /// answer is plain from either side alone is shared, never computed.
    /// </summary>
    public TiledCoverage Combined(TiledCoverage shape, RegionMode mode)
    {
        if (shape.Width != Width || shape.Height != Height) throw new ArgumentException("The coverages are not the same size.", nameof(shape));
        if (mode == RegionMode.Replace) return shape;

        byte outside = Combine(Outside, shape.Outside, mode);
        var tiles = new byte[]?[_tiles.Length];
        for (int row = 0; row < Rows; row++)
            for (int column = 0; column < Columns; column++)
            {
                int slot = row * Columns + column;
                byte[]? a = _tiles[slot], b = shape._tiles[slot];
                int? ua = UniformOf(a, Outside), ub = UniformOf(b, shape.Outside);
                tiles[slot] = (ua, ub, mode) switch
                {
                    ({ } x, { } y, _) => UniformSlot(Combine((byte)x, (byte)y, mode), outside),
                    (0, _, RegionMode.Add) => Carried(b, shape.Outside, outside),
                    (_, 0, RegionMode.Add or RegionMode.Subtract) => Carried(a, Outside, outside),
                    (255, _, RegionMode.Intersect) => Carried(b, shape.Outside, outside),
                    (_, 255, RegionMode.Intersect) => Carried(a, Outside, outside),
                    (255, _, RegionMode.Add) or (_, 255, RegionMode.Add) => UniformSlot(255, outside),
                    (0, _, RegionMode.Subtract or RegionMode.Intersect) or (_, 0, RegionMode.Intersect) or (_, 255, RegionMode.Subtract)
                        => UniformSlot(0, outside),
                    _ => Normalized(CombineTiles(Materialized(a, Outside), Materialized(b, shape.Outside), mode), TileRect(column, row), outside),
                };
            }
        return new(Width, Height, outside, tiles);
    }

    /// <summary>Every pixel's coverage turned over: what was covered is not, and what was not is.</summary>
    public TiledCoverage Inverted()
    {
        var tiles = new byte[]?[_tiles.Length];
        for (int slot = 0; slot < tiles.Length; slot++)
        {
            var tile = _tiles[slot];
            if (tile is null || ReferenceEquals(tile, CoveredTile) || ReferenceEquals(tile, ClearTile))
            {
                tiles[slot] = tile is null ? null : ReferenceEquals(tile, CoveredTile) ? ClearTile : CoveredTile;
                continue;
            }
            var inverted = new byte[TileArea];
            var from = tile.AsSpan();
            int i = 0;
            for (; i <= TileArea - Vector<byte>.Count; i += Vector<byte>.Count)
                Vector.OnesComplement(new Vector<byte>(from.Slice(i))).CopyTo(inverted.AsSpan(i));
            for (; i < TileArea; i++) inverted[i] = (byte)~from[i];
            tiles[slot] = inverted;
        }
        return new(Width, Height, (byte)(255 - Outside), tiles);
    }

    /// <summary>This coverage with <paramref name="value"/> laid over it as much as <paramref name="where"/> covers — a mask filled
    /// with a grey where something is selected.</summary>
    public TiledCoverage LaidOver(TiledCoverage where, byte value)
        => Combined(where, RegionMode.Subtract).Combined(where.ScaledBy(value), RegionMode.Add);

    /// <summary>Every value times <paramref name="factor"/> / 255.</summary>
    public TiledCoverage ScaledBy(byte factor)
    {
        if (factor == 255) return this;
        var bytes = ToBytes();
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)((bytes[i] * factor + 127) / 255);
        return FromBytes(bytes, Width, Height);
    }

    /// <summary>The edge softened: blurred so it fades over about <paramref name="radius"/> pixels each way — a Gaussian
    /// whose spread is half the radius. The canvas's edge is no edge: what touches it stays as covered there.</summary>
    public TiledCoverage Feathered(double radius)
    {
        if (radius <= 0) return this;
        double sigma = radius / 2;
        return Filtered((int)Math.Ceiling(3 * sigma) + 1, (pixels, w, h) => CoverageFilters.Blur(pixels, w, h, sigma));
    }

    /// <summary>Grown outward by <paramref name="by"/> pixels with round corners — or, by a negative amount, shrunk inward by
    /// as much. The canvas's edge is no edge: nothing shrinks away from it.</summary>
    public TiledCoverage Grown(double by)
    {
        if (by == 0) return this;
        return Filtered((int)Math.Ceiling(Math.Abs(by)) + 2, by > 0
            ? (pixels, w, h) => CoverageFilters.Grow(pixels, w, h, by)
            : (pixels, w, h) => CoverageFilters.Shrink(pixels, w, h, -by));
    }

    /// <summary>The outline smoothed: corners and jagged steps smaller than about <paramref name="radius"/> pixels rounded
    /// off, the edge left crisp.</summary>
    public TiledCoverage Smoothed(double radius)
    {
        if (radius <= 0) return this;
        double sigma = radius / 2;
        return Filtered((int)Math.Ceiling(3 * sigma) + 2, (pixels, w, h) => CoverageFilters.Smooth(pixels, w, h, sigma));
    }

    /// <summary>
    /// What <paramref name="filter"/> makes of the held tiles and <paramref name="reach"/> pixels round them — far enough
    /// that every pixel it could change is inside, and every pixel at that rectangle's edge is <see cref="Outside"/>, so a
    /// filter that carries its edge outward reads what lies past it truly. Uniform coverage is left as it is.
    /// </summary>
    private TiledCoverage Filtered(int reach, Action<byte[], int, int> filter)
    {
        var held = HeldBounds;
        if (held.IsEmpty) return this;
        var rect = Clip(SKRectI.Inflate(held, reach, reach));
        var pixels = Read(rect);
        filter(pixels, rect.Width, rect.Height);
        return Written(rect, pixels);
    }

    // ── Tiles ───────────────────────────────────────────────────────────────

    private static byte[]?[] NewSlots(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), $"A canvas is at least a pixel each way, not {width}×{height}.");
        return new byte[]?[((width + TileMask) >> TileShift) * ((height + TileMask) >> TileShift)];
    }

    private static byte[] Uniform(byte value)
    {
        var tile = new byte[TileArea];
        if (value != 0) tile.AsSpan().Fill(value);
        return tile;
    }

    private SKRectI TileRect(int column, int row)
        => Clip(new SKRectI(column << TileShift, row << TileShift, (column + 1) << TileShift, (row + 1) << TileShift));

    private SKRectI Clip(SKRectI rect) => SKRectI.Intersect(rect, new SKRectI(0, 0, Width, Height));

    private void RequireOnCanvas(SKRectI rect)
    {
        if (rect.Left < 0 || rect.Top < 0 || rect.Right > Width || rect.Bottom > Height || rect.Width < 0 || rect.Height < 0)
            throw new ArgumentOutOfRangeException(nameof(rect), $"{rect} is not on the {Width}×{Height} canvas.");
    }

    /// <summary>A tile's pixels to write into: a copy of what it holds, or <paramref name="outside"/> throughout.</summary>
    private static byte[] Materialized(byte[]? tile, byte outside)
    {
        if (tile is not null) return (byte[])tile.Clone();
        return Uniform(outside);
    }

    /// <summary>The one value every pixel of <paramref name="tile"/> holds, where it is known without reading it.</summary>
    private static int? UniformOf(byte[]? tile, byte outside)
        => tile is null ? outside : ReferenceEquals(tile, CoveredTile) ? 255 : ReferenceEquals(tile, ClearTile) ? 0 : null;

    /// <summary>The slot for a tile holding <paramref name="value"/> throughout, in a coverage whose outside is <paramref name="outside"/>.</summary>
    private static byte[]? UniformSlot(byte value, byte outside)
        => value == outside ? null : value == 255 ? CoveredTile : value == 0 ? ClearTile : Uniform(value);

    /// <summary>A tile carried from a coverage whose outside is <paramref name="from"/> into one whose outside is <paramref name="to"/>.</summary>
    private static byte[]? Carried(byte[]? tile, byte from, byte to) => UniformOf(tile, from) is { } value ? UniformSlot((byte)value, to) : tile;

    /// <summary>The slot for a tile just made: none, or a shared one, where it holds one value over <paramref name="area"/>.</summary>
    private static byte[]? Normalized(byte[] tile, SKRectI area, byte outside)
    {
        byte first = tile[((area.Top & TileMask) << TileShift) | (area.Left & TileMask)];
        int width = area.Width;
        if (width == TileSize && area.Height == TileSize)
        {
            if (tile.AsSpan().IndexOfAnyExcept(first) >= 0) return tile;
        }
        else
        {
            for (int y = 0; y < area.Height; y++)
                if (tile.AsSpan((y << TileShift), width).IndexOfAnyExcept(first) >= 0) return tile;
        }
        return first == outside ? null : first == 255 ? CoveredTile : first == 0 ? ClearTile : tile;
    }

    private static byte Combine(byte a, byte b, RegionMode mode) => mode switch
    {
        RegionMode.Replace => b,
        RegionMode.Add => Math.Max(a, b),
        RegionMode.Intersect => Math.Min(a, b),
        RegionMode.Subtract => (byte)((a * (255 - b) + 127) / 255),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static byte[] CombineTiles(byte[] a, byte[] b, RegionMode mode)
    {
        var into = new byte[TileArea];
        int i = 0;
        if (mode is RegionMode.Add or RegionMode.Intersect)
            for (; i <= TileArea - Vector<byte>.Count; i += Vector<byte>.Count)
            {
                Vector<byte> va = new(a.AsSpan(i)), vb = new(b.AsSpan(i));
                (mode == RegionMode.Add ? Vector.Max(va, vb) : Vector.Min(va, vb)).CopyTo(into.AsSpan(i));
            }
        for (; i < TileArea; i++) into[i] = Combine(a[i], b[i], mode);
        return into;
    }

    private SKRectI MeasureBounds()
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        for (int row = 0; row < Rows; row++)
            for (int column = 0; column < Columns; column++)
            {
                var tile = _tiles[row * Columns + column];
                if (UniformOf(tile, Outside) == 0) continue;
                var area = TileRect(column, row);
                if (UniformOf(tile, Outside) is not null)
                {
                    left = Math.Min(left, area.Left);
                    top = Math.Min(top, area.Top);
                    right = Math.Max(right, area.Right - 1);
                    bottom = Math.Max(bottom, area.Bottom - 1);
                    continue;
                }
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    var line = tile.AsSpan(((y & TileMask) << TileShift) | (area.Left & TileMask), area.Width);
                    int first = line.IndexOfAnyExcept((byte)0);
                    if (first < 0) continue;
                    left = Math.Min(left, area.Left + first);
                    right = Math.Max(right, area.Left + line.LastIndexOfAnyExcept((byte)0));
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        return right < 0 ? SKRectI.Empty : new SKRectI(left, top, right + 1, bottom + 1);
    }
}

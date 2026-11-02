using System.Numerics;
using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Nexaflow.Features.Images.Editing.Layers;

namespace Nexaflow.Features.Images.Editing.Selection;

/// <summary>
/// Coverage over a canvas — 1 wholly covered, 0 not at all — a sample a pixel at its <see cref="Depth"/>: a byte for a
/// selection and an 8-bit picture's masks, 16 bits or a float for a deeper picture's masks. Held in square tiles of
/// <see cref="TileSize"/> pixels that are never changed once made: every change makes a new coverage holding new tiles
/// only where it changed something and sharing every other, so a history of selections and masks costs the tiles each
/// step changed. A tile not held is <see cref="OutsideValue"/> throughout; a tile wholly covered or wholly clear is one
/// tile shared by every coverage of its depth.
/// </summary>
/// <remarks>
/// <para>A tile is always <see cref="TileSize"/>² samples, row by row, the ones at the canvas's right and bottom edges
/// included; what lies past the canvas in those is never read.</para>
/// <para>The byte members — <see cref="Read(SKRectI)"/>, <see cref="Written(SKRectI, ReadOnlySpan{byte})"/>, the indexer,
/// <see cref="Outside"/> — are its 8-bit view, exact for an 8-bit coverage; the value members are exact at every depth.</para>
/// </remarks>
public sealed class TiledCoverage
{
    public const int TileSize = 256;

    private const int TileShift = 8, TileMask = TileSize - 1, TileArea = TileSize * TileSize;

    private static readonly byte[][] CoveredTiles = [Uniform(ChannelDepth.U8, 1), Uniform(ChannelDepth.U16, 1), Uniform(ChannelDepth.F32, 1)];
    private static readonly byte[][] ClearTiles = [Uniform(ChannelDepth.U8, 0), Uniform(ChannelDepth.U16, 0), Uniform(ChannelDepth.F32, 0)];

    // Never written once the coverage is made: a slot is null (Outside), the covered tile, the clear tile or a tile of its own.
    private readonly byte[]?[] _tiles;
    private SKRectI? _bounds;

    private TiledCoverage(int width, int height, ChannelDepth depth, float outside, byte[]?[] tiles)
    {
        Width = width;
        Height = height;
        Depth = depth;
        OutsideValue = Math.Clamp(Quantized(depth, outside), 0, 1);
        _tiles = tiles;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>How deep each pixel's coverage is held.</summary>
    public ChannelDepth Depth { get; }

    /// <summary>The coverage of every pixel in a tile that is not held, 0 to 1.</summary>
    public float OutsideValue { get; }

    /// <summary>The coverage of every pixel in a tile that is not held, 0 to 255.</summary>
    public byte Outside => ToByte(OutsideValue);

    public int Columns => (Width + TileMask) >> TileShift;

    public int Rows => (Height + TileMask) >> TileShift;

    private byte[] CoveredTile => CoveredTiles[(int)Depth];

    private byte[] ClearTile => ClearTiles[(int)Depth];

    private int SampleBytes => SampleBytesOf(Depth);

    /// <summary>Nothing covered, over a <paramref name="width"/> × <paramref name="height"/> canvas.</summary>
    public static TiledCoverage Clear(int width, int height, ChannelDepth depth = ChannelDepth.U8) => new(width, height, depth, 0, NewSlots(width, height));

    /// <summary>Everything covered, over a <paramref name="width"/> × <paramref name="height"/> canvas.</summary>
    public static TiledCoverage Covered(int width, int height, ChannelDepth depth = ChannelDepth.U8) => new(width, height, depth, 1, NewSlots(width, height));

    /// <summary>The 8-bit coverage <paramref name="coverage"/> holds, a byte a pixel, row by row.</summary>
    public static TiledCoverage FromBytes(ReadOnlySpan<byte> coverage, int width, int height)
    {
        if (coverage.Length != width * height) throw new ArgumentException("The coverage is not the size given.", nameof(coverage));
        return Clear(width, height).Written(new SKRectI(0, 0, width, height), coverage);
    }

    /// <summary>The coverage <paramref name="values"/> holds, 0 to 1 a pixel, row by row, held at <paramref name="depth"/>.</summary>
    public static TiledCoverage FromValues(ReadOnlySpan<float> values, int width, int height, ChannelDepth depth)
    {
        if (values.Length != width * height) throw new ArgumentException("The coverage is not the size given.", nameof(values));
        return Clear(width, height, depth).WrittenValues(new SKRectI(0, 0, width, height), values);
    }

    /// <summary>Nothing is covered anywhere — whatever the outside, where every tile is held clear.</summary>
    public bool IsEmpty => Array.TrueForAll(_tiles, t => UniformOf(t) == 0);

    /// <summary>The coverage of one pixel, 0 to 255.</summary>
    public byte this[int x, int y] => ToByte(ValueAt(x, y));

    /// <summary>The coverage of one pixel, 0 to 1.</summary>
    public float ValueAt(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is off the canvas.");
        var tile = _tiles[(y >> TileShift) * Columns + (x >> TileShift)];
        return tile is null ? OutsideValue : Sample(tile, ((y & TileMask) << TileShift) | (x & TileMask));
    }

    /// <summary>The smallest rectangle holding every pixel covered at all; empty where none is.</summary>
    public SKRectI Bounds => _bounds ??= MeasureBounds();

    /// <summary>The rectangle round every tile that is held — outside it, every pixel is <see cref="OutsideValue"/>; empty
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
        return other.Width == Width && other.Height == Height && other.Depth == Depth && ReferenceEquals(_tiles[slot], other._tiles[slot])
               && (_tiles[slot] is not null || OutsideValue == other.OutsideValue);
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
            bytes += tile.Length;
        }
        return bytes;
    }

    /// <summary>This coverage held at <paramref name="depth"/>: every value carried across, as exact as the deeper of the two
    /// holds it; the tiles every coverage shares stay shared.</summary>
    public TiledCoverage AtDepth(ChannelDepth depth)
    {
        if (depth == Depth) return this;
        var tiles = new byte[]?[_tiles.Length];
        var values = new float[TileArea];
        for (int slot = 0; slot < tiles.Length; slot++)
        {
            var tile = _tiles[slot];
            if (tile is null) continue;
            if (ReferenceEquals(tile, CoveredTile)) { tiles[slot] = CoveredTiles[(int)depth]; continue; }
            if (ReferenceEquals(tile, ClearTile)) { tiles[slot] = ClearTiles[(int)depth]; continue; }
            ToValues(Depth, tile, values);
            var carried = new byte[TileArea * SampleBytesOf(depth)];
            FromValues(depth, values, carried);
            tiles[slot] = carried;
        }
        return new(Width, Height, depth, OutsideValue, tiles);
    }

    /// <summary>This coverage drawn at <paramref name="width"/> × <paramref name="height"/>: averaged down, or smoothly up.</summary>
    public TiledCoverage Resampled(int width, int height)
    {
        if (width == Width && height == Height) return this;
        if (IsEmpty) return Clear(width, height, Depth);
        if (Depth == ChannelDepth.U8)
        {
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
        // Deeper coverage is carried as the alpha of a float picture, which keeps every value it holds.
        var values = ReadValues(new SKRectI(0, 0, Width, Height));
        var deep = new float[values.Length * 4];
        for (int i = 0; i < values.Length; i++) deep[i * 4 + 3] = values[i];
        using var from = SKImage.FromPixelCopy(new SKImageInfo(Width, Height, SKColorType.RgbaF32, SKAlphaType.Premul),
                                               System.Runtime.InteropServices.MemoryMarshal.AsBytes(deep.AsSpan()))
                         ?? throw new InvalidOperationException("The coverage could not be made an image.");
        var into = new SKImageInfo(width, height, SKColorType.RgbaF32, SKAlphaType.Premul);
        using var resized = new SKBitmap(into);
        using (var pixmap = resized.PeekPixels())
            if (!from.ScalePixels(pixmap, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)))
                throw new InvalidOperationException("The coverage could not be resampled.");
        var floats = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(resized.GetPixelSpan());
        var result = new float[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                result[y * width + x] = floats[y * resized.RowBytes / 4 + x * 4 + 3];
        return FromValues(result, width, height, Depth);
    }

    /// <summary>
    /// This coverage moved by whole pixels onto a <paramref name="width"/> × <paramref name="height"/> canvas: what lands on it
    /// kept as it was, the rest of the new canvas <see cref="OutsideValue"/> — a mask that reveals all still reveals the canvas grown.
    /// </summary>
    public TiledCoverage Shifted(int dx, int dy, int width, int height)
    {
        if (dx == 0 && dy == 0 && width == Width && height == Height) return this;
        var shifted = new TiledCoverage(width, height, Depth, OutsideValue, NewSlots(width, height));
        var from = SKRectI.Intersect(new SKRectI(0, 0, Width, Height), new SKRectI(-dx, -dy, width - dx, height - dy));
        if (from.IsEmpty) return shifted;
        return shifted.WrittenSamples(new SKRectI(from.Left + dx, from.Top + dy, from.Right + dx, from.Bottom + dy), ReadSamples(from));
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
        if (into.Length < rect.Width * rect.Height) throw new ArgumentException("There is not room for the pixels.", nameof(into));
        if (Depth == ChannelDepth.U8)
        {
            ReadSamplesInto(rect, into);
            return;
        }
        var values = ReadValues(rect);
        for (int i = 0; i < values.Length; i++) into[i] = ToByte(values[i]);
    }

    /// <summary>The pixels of <paramref name="rect"/> — on the canvas — 0 to 1 each, row by row.</summary>
    public float[] ReadValues(SKRectI rect)
    {
        var values = new float[rect.Width * rect.Height];
        ReadValuesInto(rect, values);
        return values;
    }

    /// <summary>The pixels of <paramref name="rect"/> — on the canvas — into <paramref name="into"/>, 0 to 1 each, row by row.</summary>
    public void ReadValuesInto(SKRectI rect, Span<float> into)
    {
        if (into.Length < rect.Width * rect.Height) throw new ArgumentException("There is not room for the pixels.", nameof(into));
        var samples = ReadSamples(rect);
        ToValues(Depth, samples, into[..(rect.Width * rect.Height)]);
    }

    /// <summary>
    /// This coverage with the pixels of <paramref name="rect"/> — on the canvas — given by <paramref name="pixels"/>, a byte
    /// each, row by row: a tile <paramref name="rect"/> does not touch is shared, one it covers is made anew, and one it
    /// covers part of is copied with that part written.
    /// </summary>
    public TiledCoverage Written(SKRectI rect, ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length < rect.Width * rect.Height) throw new ArgumentException("The pixels do not fill the rectangle.", nameof(pixels));
        if (Depth == ChannelDepth.U8) return WrittenSamples(rect, pixels);
        var values = new float[rect.Width * rect.Height];
        ToValues(ChannelDepth.U8, pixels[..values.Length], values);
        return WrittenValues(rect, values);
    }

    /// <summary>This coverage with the pixels of <paramref name="rect"/> — on the canvas — given by <paramref name="values"/>,
    /// 0 to 1 each, row by row, held as deep as this coverage is.</summary>
    public TiledCoverage WrittenValues(SKRectI rect, ReadOnlySpan<float> values)
    {
        if (values.Length < rect.Width * rect.Height) throw new ArgumentException("The values do not fill the rectangle.", nameof(values));
        var samples = new byte[rect.Width * rect.Height * SampleBytes];
        FromValues(Depth, values[..(rect.Width * rect.Height)], samples);
        return WrittenSamples(rect, samples);
    }

    /// <summary>
    /// <paramref name="shape"/> — the same size — met with this as <paramref name="mode"/> says: replacing it, the more
    /// covered of the two, this as much as the shape leaves uncovered, or the less covered of the two. The answer is held as
    /// deep as this coverage; a tile where it is plain from either side alone is shared, never computed.
    /// </summary>
    public TiledCoverage Combined(TiledCoverage shape, RegionMode mode)
    {
        if (shape.Width != Width || shape.Height != Height) throw new ArgumentException("The coverages are not the same size.", nameof(shape));
        shape = shape.AtDepth(Depth);
        if (mode == RegionMode.Replace) return shape;

        float outside = Quantized(Depth, Combine(OutsideValue, shape.OutsideValue, mode));
        var tiles = new byte[]?[_tiles.Length];
        for (int row = 0; row < Rows; row++)
            for (int column = 0; column < Columns; column++)
            {
                int slot = row * Columns + column;
                byte[]? a = _tiles[slot], b = shape._tiles[slot];
                float? ua = UniformOf(a), ub = shape.UniformOf(b);
                tiles[slot] = (ua, ub, mode) switch
                {
                    ({ } x, { } y, _) => UniformSlot(Combine(x, y, mode), outside),
                    (0, _, RegionMode.Add) => shape.Carried(b, outside),
                    (_, 0, RegionMode.Add or RegionMode.Subtract) => Carried(a, outside),
                    (1, _, RegionMode.Intersect) => shape.Carried(b, outside),
                    (_, 1, RegionMode.Intersect) => Carried(a, outside),
                    (1, _, RegionMode.Add) or (_, 1, RegionMode.Add) => UniformSlot(1, outside),
                    (0, _, RegionMode.Subtract or RegionMode.Intersect) or (_, 0, RegionMode.Intersect) or (_, 1, RegionMode.Subtract)
                        => UniformSlot(0, outside),
                    _ => Normalized(CombineTiles(Materialized(a), shape.Materialized(b), mode), TileRect(column, row), outside),
                };
            }
        return new(Width, Height, Depth, outside, tiles);
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
            tiles[slot] = InvertedTile(tile);
        }
        return new(Width, Height, Depth, 1 - OutsideValue, tiles);
    }

    private byte[] InvertedTile(byte[] tile)
    {
        var inverted = new byte[tile.Length];
        switch (Depth)
        {
            case ChannelDepth.U8:
            {
                var from = tile.AsSpan();
                int i = 0;
                for (; i <= TileArea - Vector<byte>.Count; i += Vector<byte>.Count)
                    Vector.OnesComplement(new Vector<byte>(from.Slice(i))).CopyTo(inverted.AsSpan(i));
                for (; i < TileArea; i++) inverted[i] = (byte)~from[i];
                break;
            }
            case ChannelDepth.U16:
            {
                var from = MemoryMarshal.Cast<byte, ushort>(tile);
                var into = MemoryMarshal.Cast<byte, ushort>(inverted.AsSpan());
                for (int i = 0; i < TileArea; i++) into[i] = (ushort)~from[i];
                break;
            }
            default:
            {
                var from = MemoryMarshal.Cast<byte, float>(tile);
                var into = MemoryMarshal.Cast<byte, float>(inverted.AsSpan());
                for (int i = 0; i < TileArea; i++) into[i] = 1 - from[i];
                break;
            }
        }
        return inverted;
    }

    /// <summary>This coverage with <paramref name="value"/> (0 to 255) laid over it as much as <paramref name="where"/> covers —
    /// a mask filled with a grey where something is selected.</summary>
    public TiledCoverage LaidOver(TiledCoverage where, byte value) => LaidOver(where, value / 255f);

    /// <summary>This coverage with <paramref name="value"/> (0 to 1) laid over it as much as <paramref name="where"/> covers —
    /// a mask filled with a grey where something is selected.</summary>
    public TiledCoverage LaidOver(TiledCoverage where, float value)
    {
        var deep = where.AtDepth(Depth);
        return Combined(deep, RegionMode.Subtract).Combined(deep.ScaledBy(value), RegionMode.Add);
    }

    /// <summary>Every value times <paramref name="factor"/> / 255.</summary>
    public TiledCoverage ScaledBy(byte factor)
    {
        if (factor == 255) return this;
        if (Depth != ChannelDepth.U8) return ScaledBy(factor / 255f);
        var bytes = ToBytes();
        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)((bytes[i] * factor + 127) / 255);
        return FromBytes(bytes, Width, Height);
    }

    /// <summary>Every value times <paramref name="factor"/>, 0 to 1.</summary>
    public TiledCoverage ScaledBy(float factor)
    {
        if (factor >= 1) return this;
        if (Depth == ChannelDepth.U8 && factor * 255 == MathF.Round(factor * 255)) return ScaledBy((byte)(factor * 255));
        var values = ReadValues(new SKRectI(0, 0, Width, Height));
        for (int i = 0; i < values.Length; i++) values[i] *= factor;
        return FromValues(values, Width, Height, Depth);
    }

    /// <summary>The edge softened: blurred so it fades over about <paramref name="radius"/> pixels each way — a Gaussian
    /// whose spread is half the radius. The canvas's edge is no edge: what touches it stays as covered there.</summary>
    public TiledCoverage Feathered(double radius)
    {
        if (radius <= 0) return this;
        double sigma = radius / 2;
        return Filtered((int)Math.Ceiling(3 * sigma) + 1,
            (pixels, w, h) => CoverageFilters.Blur(pixels, w, h, sigma),
            (values, w, h) => CoverageFilters.Blur(values, w, h, sigma));
    }

    /// <summary>Grown outward by <paramref name="by"/> pixels with round corners — or, by a negative amount, shrunk inward by
    /// as much. The canvas's edge is no edge: nothing shrinks away from it.</summary>
    public TiledCoverage Grown(double by)
    {
        if (by == 0) return this;
        int reach = (int)Math.Ceiling(Math.Abs(by)) + 2;
        return by > 0
            ? Filtered(reach, (pixels, w, h) => CoverageFilters.Grow(pixels, w, h, by), (values, w, h) => CoverageFilters.Grow(values, w, h, by))
            : Filtered(reach, (pixels, w, h) => CoverageFilters.Shrink(pixels, w, h, -by), (values, w, h) => CoverageFilters.Shrink(values, w, h, -by));
    }

    /// <summary>The outline smoothed: corners and jagged steps smaller than about <paramref name="radius"/> pixels rounded
    /// off, the edge left crisp.</summary>
    public TiledCoverage Smoothed(double radius)
    {
        if (radius <= 0) return this;
        double sigma = radius / 2;
        return Filtered((int)Math.Ceiling(3 * sigma) + 2,
            (pixels, w, h) => CoverageFilters.Smooth(pixels, w, h, sigma),
            (values, w, h) => CoverageFilters.Smooth(values, w, h, sigma));
    }

    /// <summary>
    /// What a filter makes of the held tiles and <paramref name="reach"/> pixels round them — far enough that every pixel it
    /// could change is inside, and every pixel at that rectangle's edge is <see cref="OutsideValue"/>, so a filter that
    /// carries its edge outward reads what lies past it truly. Uniform coverage is left as it is. An 8-bit coverage is
    /// filtered a byte a pixel, a deeper one as values.
    /// </summary>
    private TiledCoverage Filtered(int reach, Action<byte[], int, int> filter, Action<float[], int, int> deepFilter)
    {
        var held = HeldBounds;
        if (held.IsEmpty) return this;
        var rect = Clip(SKRectI.Inflate(held, reach, reach));
        if (Depth == ChannelDepth.U8)
        {
            var pixels = Read(rect);
            filter(pixels, rect.Width, rect.Height);
            return Written(rect, pixels);
        }
        var values = ReadValues(rect);
        deepFilter(values, rect.Width, rect.Height);
        return WrittenValues(rect, values);
    }

    // ── Samples ─────────────────────────────────────────────────────────────

    /// <summary>The samples of <paramref name="rect"/> as they are held, row by row.</summary>
    private byte[] ReadSamples(SKRectI rect)
    {
        var samples = new byte[rect.Width * rect.Height * SampleBytes];
        ReadSamplesInto(rect, samples);
        return samples;
    }

    private void ReadSamplesInto(SKRectI rect, Span<byte> into)
    {
        RequireOnCanvas(rect);
        int size = SampleBytes;
        Span<byte> outside = stackalloc byte[size];
        FromValues(Depth, [OutsideValue], outside);
        for (int row = rect.Top >> TileShift; row <= (rect.Bottom - 1) >> TileShift; row++)
            for (int column = rect.Left >> TileShift; column <= (rect.Right - 1) >> TileShift; column++)
            {
                var overlap = SKRectI.Intersect(TileRect(column, row), rect);
                var tile = _tiles[row * Columns + column];
                for (int y = overlap.Top; y < overlap.Bottom; y++)
                {
                    var line = into.Slice(((y - rect.Top) * rect.Width + overlap.Left - rect.Left) * size, overlap.Width * size);
                    if (tile is null) Fill(line, outside);
                    else tile.AsSpan((((y & TileMask) << TileShift) | (overlap.Left & TileMask)) * size, overlap.Width * size).CopyTo(line);
                }
            }
    }

    /// <summary>This coverage with the samples of <paramref name="rect"/> given as they are held, row by row.</summary>
    private TiledCoverage WrittenSamples(SKRectI rect, ReadOnlySpan<byte> samples)
    {
        RequireOnCanvas(rect);
        int size = SampleBytes;
        var tiles = (byte[]?[])_tiles.Clone();
        if (rect.IsEmpty) return new(Width, Height, Depth, OutsideValue, tiles);
        for (int row = rect.Top >> TileShift; row <= (rect.Bottom - 1) >> TileShift; row++)
            for (int column = rect.Left >> TileShift; column <= (rect.Right - 1) >> TileShift; column++)
            {
                var whole = TileRect(column, row);
                var overlap = SKRectI.Intersect(whole, rect);
                int slot = row * Columns + column;
                var tile = overlap == whole ? new byte[TileArea * size] : Materialized(_tiles[slot]);
                for (int y = overlap.Top; y < overlap.Bottom; y++)
                    samples.Slice(((y - rect.Top) * rect.Width + overlap.Left - rect.Left) * size, overlap.Width * size)
                           .CopyTo(tile.AsSpan((((y & TileMask) << TileShift) | (overlap.Left & TileMask)) * size));
                tiles[slot] = Normalized(tile, whole, OutsideValue);
            }
        return new(Width, Height, Depth, OutsideValue, tiles);
    }

    private static void Fill(Span<byte> line, ReadOnlySpan<byte> sample)
    {
        if (sample.Length == 1) { line.Fill(sample[0]); return; }
        for (int i = 0; i < line.Length; i += sample.Length) sample.CopyTo(line[i..]);
    }

    private static int SampleBytesOf(ChannelDepth depth) => depth switch { ChannelDepth.U8 => 1, ChannelDepth.U16 => 2, _ => 4 };

    private float Sample(byte[] tile, int index) => Depth switch
    {
        ChannelDepth.U8 => tile[index] / 255f,
        ChannelDepth.U16 => BinaryPrimitives.ReadUInt16LittleEndian(tile.AsSpan(index * 2)) / 65535f,
        _ => BinaryPrimitives.ReadSingleLittleEndian(tile.AsSpan(index * 4)),
    };

    private static void ToValues(ChannelDepth depth, ReadOnlySpan<byte> samples, Span<float> values)
    {
        switch (depth)
        {
            case ChannelDepth.U8:
                for (int i = 0; i < values.Length; i++) values[i] = samples[i] / 255f;
                break;
            case ChannelDepth.U16:
            {
                var deep = MemoryMarshal.Cast<byte, ushort>(samples);
                for (int i = 0; i < values.Length; i++) values[i] = deep[i] / 65535f;
                break;
            }
            default:
                MemoryMarshal.Cast<byte, float>(samples)[..values.Length].CopyTo(values);
                break;
        }
    }

    private static void FromValues(ChannelDepth depth, ReadOnlySpan<float> values, Span<byte> samples)
    {
        switch (depth)
        {
            case ChannelDepth.U8:
                for (int i = 0; i < values.Length; i++) samples[i] = ToByte(values[i]);
                break;
            case ChannelDepth.U16:
            {
                var deep = MemoryMarshal.Cast<byte, ushort>(samples);
                for (int i = 0; i < values.Length; i++) deep[i] = (ushort)Math.Clamp((int)MathF.Round(values[i] * 65535), 0, 65535);
                break;
            }
            default:
            {
                var deep = MemoryMarshal.Cast<byte, float>(samples);
                for (int i = 0; i < values.Length; i++) deep[i] = Math.Clamp(values[i], 0, 1);
                break;
            }
        }
    }

    private static byte ToByte(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);

    /// <summary><paramref name="value"/> as <paramref name="depth"/> holds it — so a uniform tile and the outside compare equal
    /// to the samples they stand for.</summary>
    private static float Quantized(ChannelDepth depth, float value) => depth switch
    {
        ChannelDepth.U8 => ToByte(value) / 255f,
        ChannelDepth.U16 => (float)Math.Clamp((int)MathF.Round(value * 65535), 0, 65535) / 65535f,
        _ => value,
    };

    // ── Tiles ───────────────────────────────────────────────────────────────

    private static byte[]?[] NewSlots(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), $"A canvas is at least a pixel each way, not {width}×{height}.");
        return new byte[]?[((width + TileMask) >> TileShift) * ((height + TileMask) >> TileShift)];
    }

    private static byte[] Uniform(ChannelDepth depth, float value)
    {
        var tile = new byte[TileArea * SampleBytesOf(depth)];
        if (value == 0) return tile;
        Span<byte> sample = stackalloc byte[SampleBytesOf(depth)];
        FromValues(depth, [value], sample);
        Fill(tile, sample);
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

    /// <summary>A tile's samples to write into: a copy of what it holds, or the outside throughout.</summary>
    private byte[] Materialized(byte[]? tile) => tile is not null ? (byte[])tile.Clone() : Uniform(Depth, OutsideValue);

    /// <summary>The one value every pixel of <paramref name="tile"/> holds, where it is known without reading it.</summary>
    private float? UniformOf(byte[]? tile)
        => tile is null ? OutsideValue : ReferenceEquals(tile, CoveredTile) ? 1 : ReferenceEquals(tile, ClearTile) ? 0 : null;

    /// <summary>The slot for a tile holding <paramref name="value"/> throughout, in a coverage whose outside is <paramref name="outside"/>.</summary>
    private byte[]? UniformSlot(float value, float outside)
    {
        value = Quantized(Depth, value);
        return value == outside ? null : value == 1 ? CoveredTile : value == 0 ? ClearTile : Uniform(Depth, value);
    }

    /// <summary>A tile of this coverage carried into one whose outside is <paramref name="to"/>.</summary>
    private byte[]? Carried(byte[]? tile, float to) => UniformOf(tile) is { } value ? UniformSlot(value, to) : tile;

    /// <summary>The slot for a tile just made: none, or a shared one, where it holds one value over <paramref name="area"/>.</summary>
    private byte[]? Normalized(byte[] tile, SKRectI area, float outside)
    {
        int size = SampleBytes;
        var first = tile.AsSpan((((area.Top & TileMask) << TileShift) | (area.Left & TileMask)) * size, size).ToArray();
        int width = area.Width;
        for (int y = 0; y < area.Height; y++)
        {
            var line = tile.AsSpan(((((area.Top & TileMask) + y) << TileShift) | (area.Left & TileMask)) * size, width * size);
            if (size == 1)
            {
                if (line.IndexOfAnyExcept(first[0]) >= 0) return tile;
                continue;
            }
            for (int i = 0; i < line.Length; i += size)
                if (!line.Slice(i, size).SequenceEqual(first)) return tile;
        }
        float value = Sample(first, 0);
        return value == outside ? null : value == 1 ? CoveredTile : value == 0 ? ClearTile : tile;
    }

    private static float Combine(float a, float b, RegionMode mode) => mode switch
    {
        RegionMode.Replace => b,
        RegionMode.Add => Math.Max(a, b),
        RegionMode.Intersect => Math.Min(a, b),
        RegionMode.Subtract => a * (1 - b),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private static byte CombineBytes(byte a, byte b, RegionMode mode) => mode switch
    {
        RegionMode.Replace => b,
        RegionMode.Add => Math.Max(a, b),
        RegionMode.Intersect => Math.Min(a, b),
        RegionMode.Subtract => (byte)((a * (255 - b) + 127) / 255),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    private byte[] CombineTiles(byte[] a, byte[] b, RegionMode mode)
    {
        var into = new byte[a.Length];
        if (Depth == ChannelDepth.U8)
        {
            int i = 0;
            if (mode is RegionMode.Add or RegionMode.Intersect)
                for (; i <= TileArea - Vector<byte>.Count; i += Vector<byte>.Count)
                {
                    Vector<byte> va = new(a.AsSpan(i)), vb = new(b.AsSpan(i));
                    (mode == RegionMode.Add ? Vector.Max(va, vb) : Vector.Min(va, vb)).CopyTo(into.AsSpan(i));
                }
            for (; i < TileArea; i++) into[i] = CombineBytes(a[i], b[i], mode);
            return into;
        }
        var va2 = new float[TileArea];
        var vb2 = new float[TileArea];
        ToValues(Depth, a, va2);
        ToValues(Depth, b, vb2);
        for (int i = 0; i < TileArea; i++) va2[i] = Combine(va2[i], vb2[i], mode);
        FromValues(Depth, va2, into);
        return into;
    }

    private SKRectI MeasureBounds()
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        int size = SampleBytes;
        for (int row = 0; row < Rows; row++)
            for (int column = 0; column < Columns; column++)
            {
                var tile = _tiles[row * Columns + column];
                if (UniformOf(tile) == 0) continue;
                var area = TileRect(column, row);
                if (UniformOf(tile) is not null)
                {
                    left = Math.Min(left, area.Left);
                    top = Math.Min(top, area.Top);
                    right = Math.Max(right, area.Right - 1);
                    bottom = Math.Max(bottom, area.Bottom - 1);
                    continue;
                }
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    // Any sample not wholly zero is covered — 16 bits and float alike, their bytes all zero where clear.
                    var line = tile!.AsSpan((((y & TileMask) << TileShift) | (area.Left & TileMask)) * size, area.Width * size);
                    int first = line.IndexOfAnyExcept((byte)0);
                    if (first < 0) continue;
                    left = Math.Min(left, area.Left + first / size);
                    right = Math.Max(right, area.Left + line.LastIndexOfAnyExcept((byte)0) / size);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        return right < 0 ? SKRectI.Empty : new SKRectI(left, top, right + 1, bottom + 1);
    }
}

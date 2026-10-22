using System.Buffers.Binary;
using System.Runtime.Intrinsics;

namespace Nexaflow.Media.Jpeg2000.Internal;

/// <summary>
/// Lays a decoded image out as a PDF image's data is laid out: the components interleaved sample by sample, each
/// sample the low bits of its value at its component's precision, most significant bit first, and every row starting
/// on a byte.
/// </summary>
/// <remarks>
/// The buffer is sized exactly before anything is written, and a row is written by the widest step its precisions
/// allow: a byte or two a sample when every component is 8 or 16 bits, otherwise through a bit accumulator.
/// </remarks>
internal static class SamplePacker
{
    public static byte[] Pack(JPXImage image) => Pack(image, image.comps, null);

    /// <summary>
    /// <paramref name="comps"/> interleaved in the order given, each at its precision, every row starting on a byte. With
    /// <paramref name="premultipliedBy"/>, each sample is first divided by that opacity where it stands.
    /// </summary>
    public static byte[] Pack(JPXImage image, ImageComp[] comps, ImageComp? premultipliedBy)
    {
        var width = checked((int)(image.x1 - image.x0));
        var height = checked((int)(image.y1 - image.y0));
        if (width == 0 || height == 0 || comps.Length == 0) return [];

        var rows = new ComponentRows[comps.Length];
        var precisions = new int[comps.Length];
        var bitsPerPixel = 0;
        for (var c = 0; c < comps.Length; c++)
        {
            rows[c] = ComponentRows.Of(comps[c], Array.IndexOf(image.comps, comps[c]), width, height);
            precisions[c] = (int)comps[c].prec;
            bitsPerPixel += precisions[c];
        }

        // The opacity a premultiplied image's samples are divided by, row by row, into rows of their own: a component's
        // rows can be its own data, which is not to be changed.
        ComponentRows? opacity = null;
        int[][]? divided = null;
        var opacityPrecision = 0;
        if (premultipliedBy is not null)
        {
            opacity = ComponentRows.Of(premultipliedBy, Array.IndexOf(image.comps, premultipliedBy), width, height);
            opacityPrecision = (int)premultipliedBy.prec;
            divided = new int[comps.Length][];
            for (var c = 0; c < comps.Length; c++) divided[c] = new int[width];
        }

        var rowBytes = checked((int)(((long)width * bitsPerPixel + 7) / 8));
        var packed = new byte[checked(rowBytes * height)];
        var uniform = Array.TrueForAll(precisions, p => p == precisions[0]) ? precisions[0] : 0;

        var starts = new int[comps.Length];
        var sources = new int[comps.Length][];
        for (var y = 0; y < height; y++)
        {
            for (var c = 0; c < rows.Length; c++) sources[c] = rows[c].Row(y, out starts[c]);
            if (opacity is { } over)
            {
                var levels = over.Row(y, out var from).AsSpan(from, width);
                for (var c = 0; c < sources.Length; c++)
                {
                    Unmultiplied(sources[c].AsSpan(starts[c], width), levels, precisions[c], opacityPrecision, divided![c]);
                    sources[c] = divided[c];
                    starts[c] = 0;
                }
            }

            var target = packed.AsSpan(y * rowBytes, rowBytes);

            switch (uniform)
            {
                case 8 when sources.Length == 1:
                    Narrow(sources[0].AsSpan(starts[0], width), target);
                    break;
                case 8:
                    for (var c = 0; c < sources.Length; c++) Bytes(sources[c].AsSpan(starts[c], width), target, c, sources.Length);
                    break;
                case 16:
                    for (var c = 0; c < sources.Length; c++) Words(sources[c].AsSpan(starts[c], width), target, c, sources.Length);
                    break;
                default:
                    Bits(sources, starts, precisions, width, target);
                    break;
            }
        }

        return packed;
    }

    /// <summary>
    /// One component at the image's size, a byte a sample, row by row, scaled from its precision so its largest value is 255;
    /// a signed one is first offset to start at 0.
    /// </summary>
    public static byte[] Levels(JPXImage image, ImageComp comp)
    {
        var width = checked((int)(image.x1 - image.x0));
        var height = checked((int)(image.y1 - image.y0));
        var levels = new byte[checked(width * height)];
        if (levels.Length == 0) return levels;

        var rows = ComponentRows.Of(comp, Array.IndexOf(image.comps, comp), width, height);
        var precision = (int)comp.prec;
        var largest = (1L << precision) - 1;
        var offset = comp.Signed ? 1L << (precision - 1) : 0;
        for (var y = 0; y < height; y++)
        {
            var row = rows.Row(y, out var start).AsSpan(start, width);
            var target = levels.AsSpan(y * width, width);
            if (precision == 8 && offset == 0)
            {
                Narrow(row, target);
                continue;
            }

            for (var x = 0; x < width; x++)
                target[x] = (byte)((Math.Clamp(row[x] + offset, 0, largest) * 255 + largest / 2) / largest);
        }

        return levels;
    }

    /// <summary>
    /// Samples divided by the opacity they were multiplied by, each in its own precision — c × the opacity's largest value ÷ a,
    /// rounded, and held to the sample's range — and 0 where the opacity is.
    /// </summary>
    private static void Unmultiplied(ReadOnlySpan<int> samples, ReadOnlySpan<int> opacity, int precision, int opacityPrecision, Span<int> target)
    {
        var largest = (1L << precision) - 1;
        var opaque = (1L << opacityPrecision) - 1;
        for (var x = 0; x < samples.Length; x++)
        {
            long a = opacity[x];
            target[x] = a <= 0 ? 0 : (int)Math.Min(largest, (samples[x] * opaque + a / 2) / a);
        }
    }

    /// <summary>One component's samples, as the low byte of each.</summary>
    private static void Narrow(ReadOnlySpan<int> samples, Span<byte> target)
    {
        var x = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            // Narrowing truncates, as the byte cast does: 16 samples to one vector of bytes.
            for (; x + 16 <= samples.Length; x += 16)
            {
                var low = Vector128.Narrow(Vector128.Create(samples.Slice(x, 4)).AsUInt32(), Vector128.Create(samples.Slice(x + 4, 4)).AsUInt32());
                var high = Vector128.Narrow(Vector128.Create(samples.Slice(x + 8, 4)).AsUInt32(), Vector128.Create(samples.Slice(x + 12, 4)).AsUInt32());
                Vector128.Narrow(low, high).CopyTo(target.Slice(x, 16));
            }
        }

        for (; x < samples.Length; x++) target[x] = (byte)samples[x];
    }

    /// <summary>One component's samples, a byte each, into every <paramref name="stride"/>th byte from <paramref name="at"/>.</summary>
    private static void Bytes(ReadOnlySpan<int> samples, Span<byte> target, int at, int stride)
    {
        for (var x = 0; x < samples.Length; x++, at += stride) target[at] = (byte)samples[x];
    }

    /// <summary>One component's samples, two bytes each, high first, into every <paramref name="stride"/>th pair.</summary>
    private static void Words(ReadOnlySpan<int> samples, Span<byte> target, int at, int stride)
    {
        at *= 2;
        stride *= 2;
        for (var x = 0; x < samples.Length; x++, at += stride)
            BinaryPrimitives.WriteUInt16BigEndian(target.Slice(at, 2), (ushort)samples[x]);
    }

    /// <summary>
    /// A row at any precisions: each sample's low bits go into an accumulator, and whole bytes leave it from the top.
    /// A precision is at most 31 bits and fewer than 8 are ever left over, so the accumulator never holds more than 38.
    /// </summary>
    private static void Bits(int[][] sources, int[] starts, int[] precisions, int width, Span<byte> target)
    {
        ulong held = 0;
        var count = 0;
        var at = 0;
        for (var x = 0; x < width; x++)
        {
            for (var c = 0; c < sources.Length; c++)
            {
                var precision = precisions[c];
                held = (held << precision) | ((uint)sources[c][starts[c] + x] & ((1u << precision) - 1));
                for (count += precision; count >= 8; )
                {
                    count -= 8;
                    target[at++] = (byte)(held >> count);
                }
            }
        }

        // The row's last bits, padded to the byte with zeros.
        if (count > 0) target[at] = (byte)(held << (8 - count));
    }

    /// <summary>
    /// Where each row of a component lies once stretched to the image. A component the image's size is read as it
    /// is; a smaller one by nearest sample — the Bresenham mapping, which steps each axis by the whole part of the
    /// ratio and carries the remainder, the same for every row — through a table of row starts and one of column
    /// offsets, gathered into one reused row.
    /// </summary>
    private readonly struct ComponentRows
    {
        private readonly int[] _data;
        private readonly int _width;
        private readonly int[]? _rowStarts;
        private readonly int[]? _columns;
        private readonly int[]? _row;

        private ComponentRows(int[] data, int width, int[]? rowStarts, int[]? columns)
        {
            _data = data;
            _width = width;
            _rowStarts = rowStarts;
            _columns = columns;
            _row = columns is null ? null : new int[columns.Length];
        }

        public static ComponentRows Of(ImageComp comp, int index, int width, int height)
        {
            int w = (int)comp.w, h = (int)comp.h;
            if (comp.data is null || comp.data.Length < (long)w * h)
                throw new Jpeg2000Exception($"Component {index} holds fewer samples than its size.");

            return new ComponentRows(comp.data, w,
                h == height && w == width ? null : Steps(h, height, w),
                w == width ? null : Steps(w, width, 1));
        }

        /// <summary>
        /// The source offset of each of <paramref name="to"/> places stretched from <paramref name="from"/>, a step
        /// of <paramref name="unit"/> apart.
        /// </summary>
        private static int[] Steps(int from, int to, int unit)
        {
            var offsets = new int[to];
            int whole = from / to * unit, part = from % to;
            for (int i = 0, offset = 0, carried = 0; i < to; i++)
            {
                offsets[i] = offset;
                offset += whole;
                carried += part;
                if (carried >= to)
                {
                    carried -= to;
                    offset += unit;
                }
            }

            return offsets;
        }

        /// <summary>Row <paramref name="y"/>'s samples, in the returned array from <paramref name="start"/>.</summary>
        public int[] Row(int y, out int start)
        {
            start = _rowStarts is null ? y * _width : _rowStarts[y];
            if (_columns is null) return _data;

            for (var x = 0; x < _columns.Length; x++) _row![x] = _data[start + _columns[x]];
            start = 0;
            return _row!;
        }
    }
}

using System.Buffers.Binary;
using System.IO.Compression;

namespace Nexaflow.Media.Photoshop;

/// <summary>Fills <paramref name="into"/> with row <paramref name="row"/> of a channel's samples, in the machine's order.</summary>
public delegate void PhotoshopRowSource(int row, Span<byte> into);

/// <summary>Fills <paramref name="into"/> with row <paramref name="row"/> of channel <paramref name="channel"/>'s samples, in the
/// machine's order.</summary>
public delegate void PhotoshopChannelRows(int channel, int row, Span<byte> into);

/// <summary>
/// A Photoshop channel's samples, compressed as the file holds them and back: raw; RLE, each row PackBits on its own with
/// the rows' byte counts before them; ZIP, the channel one zlib stream; and ZIP with prediction, each row's samples stored as
/// the step from the one before — 32-bit rows first split into their floats' byte planes. Samples go in and come out a row at
/// a time in the machine's order — a byte, a little-endian 16-bit value or a little-endian float — whatever the file's.
/// </summary>
public static class PhotoshopChannels
{
    // ── Reading ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The channel of <paramref name="width"/> × <paramref name="height"/> samples of <paramref name="sampleBytes"/> each
    /// starting at <paramref name="offset"/> of <paramref name="stream"/> — its compression first — handed to
    /// <paramref name="row"/> a row at a time, top first.
    /// </summary>
    /// <exception cref="InvalidDataException">The data is not what its compression says.</exception>
    public static void Decode(Stream stream, long offset, long length, int width, int height, int sampleBytes, bool isLarge, Action<int, ReadOnlySpan<byte>> row)
    {
        int y = 0;
        foreach (var samples in Rows(stream, offset, length, width, height, sampleBytes, isLarge)) row(y++, samples);
    }

    /// <summary>
    /// The channel at <paramref name="offset"/> of <paramref name="stream"/> as its rows, top first, each in the machine's order,
    /// in one buffer reused for every row. Each channel reads through a window of its own onto the stream, so the channels of a
    /// layer are read in step — a row of each in turn — through one stream, none of them held whole.
    /// </summary>
    /// <exception cref="InvalidDataException">The data is not what its compression says.</exception>
    public static IEnumerable<byte[]> Rows(Stream stream, long offset, long length, int width, int height, int sampleBytes, bool isLarge)
    {
        if (width <= 0 || height <= 0) yield break;
        var window = new WindowStream(stream, offset, length);
        var compression = (PhotoshopCompression)ReadUInt16(window);
        var line = new byte[width * sampleBytes];
        switch (compression)
        {
            case PhotoshopCompression.Raw:
                for (int y = 0; y < height; y++)
                {
                    window.ReadExactly(line);
                    ToMachine(line, sampleBytes);
                    yield return line;
                }
                break;
            case PhotoshopCompression.Rle:
            {
                var counts = new int[height];
                for (int y = 0; y < height; y++) counts[y] = (int)(isLarge ? ReadUInt32(window) : ReadUInt16(window));
                var packed = new byte[Math.Max(1, counts.Max())];
                for (int y = 0; y < height; y++)
                {
                    window.ReadExactly(packed, 0, counts[y]);
                    UnpackBits(packed.AsSpan(0, counts[y]), line);
                    ToMachine(line, sampleBytes);
                    yield return line;
                }
                break;
            }
            case PhotoshopCompression.Zip or PhotoshopCompression.ZipPredicted:
            {
                using var zlib = new ZLibStream(window, CompressionMode.Decompress);
                for (int y = 0; y < height; y++)
                {
                    zlib.ReadExactly(line);
                    if (compression == PhotoshopCompression.ZipPredicted) Unpredicted(line, width, sampleBytes);
                    else ToMachine(line, sampleBytes);
                    yield return line;
                }
                break;
            }
            default:
                throw new InvalidDataException($"No channel is compressed as {(ushort)compression}.");
        }
    }

    /// <summary>
    /// The merged image at <paramref name="offset"/>: its compression, then <paramref name="channels"/> channels one after the
    /// other — an RLE image's byte counts for every channel's rows first — handed to <paramref name="row"/> a channel's row at a time.
    /// </summary>
    public static void DecodeMerged(Stream stream, long offset, int channels, int width, int height, int sampleBytes, bool isLarge,
                                    Action<int, int, ReadOnlySpan<byte>> row)
    {
        stream.Position = offset;
        var compression = (PhotoshopCompression)ReadUInt16(stream);
        DecodeRows(stream, compression, stream.Length - stream.Position, channels, width, height, sampleBytes, isLarge, row);
    }

    private static void DecodeRows(Stream stream, PhotoshopCompression compression, long length, int channels, int width, int height, int sampleBytes,
                                   bool isLarge, Action<int, int, ReadOnlySpan<byte>> row)
    {
        int rowBytes = width * sampleBytes;
        var line = new byte[rowBytes];
        switch (compression)
        {
            case PhotoshopCompression.Raw:
                for (int c = 0; c < channels; c++)
                    for (int y = 0; y < height; y++)
                    {
                        stream.ReadExactly(line);
                        ToMachine(line, sampleBytes);
                        row(c, y, line);
                    }
                break;
            case PhotoshopCompression.Rle:
            {
                var counts = new long[channels * height];
                for (int i = 0; i < counts.Length; i++) counts[i] = isLarge ? ReadUInt32(stream) : ReadUInt16(stream);
                var packed = new byte[Math.Max(1, (int)counts.Max())];
                for (int c = 0; c < channels; c++)
                    for (int y = 0; y < height; y++)
                    {
                        int count = (int)counts[c * height + y];
                        stream.ReadExactly(packed, 0, count);
                        UnpackBits(packed.AsSpan(0, count), line);
                        ToMachine(line, sampleBytes);
                        row(c, y, line);
                    }
                break;
            }
            case PhotoshopCompression.Zip or PhotoshopCompression.ZipPredicted:
            {
                using var bounded = new BoundedStream(stream, length);
                using var zlib = new ZLibStream(bounded, CompressionMode.Decompress, leaveOpen: true);
                for (int c = 0; c < channels; c++)
                    for (int y = 0; y < height; y++)
                    {
                        zlib.ReadExactly(line);
                        if (compression == PhotoshopCompression.ZipPredicted) Unpredicted(line, width, sampleBytes);
                        else ToMachine(line, sampleBytes);
                        row(c, y, line);
                    }
                break;
            }
            default:
                throw new InvalidDataException($"No channel is compressed as {(ushort)compression}.");
        }
    }

    /// <summary>A row of the file's big-endian samples in the machine's order, in place.</summary>
    private static void ToMachine(Span<byte> line, int sampleBytes)
    {
        if (sampleBytes == 1 || !BitConverter.IsLittleEndian) return;
        for (int i = 0; i < line.Length; i += sampleBytes) line.Slice(i, sampleBytes).Reverse();
    }

    /// <summary>A predicted row — each sample the step from the one before; a float row split into byte planes and stepped
    /// byte by byte — as its samples, in the machine's order, in place.</summary>
    private static void Unpredicted(Span<byte> line, int width, int sampleBytes)
    {
        switch (sampleBytes)
        {
            case 1:
                for (int i = 1; i < line.Length; i++) line[i] += line[i - 1];
                break;
            case 2:
            {
                ushort previous = 0;
                for (int i = 0; i < width; i++)
                {
                    previous = (ushort)(previous + BinaryPrimitives.ReadUInt16BigEndian(line[(i * 2)..]));
                    BinaryPrimitives.WriteUInt16LittleEndian(line[(i * 2)..], previous);
                }
                break;
            }
            default:
            {
                for (int i = 1; i < line.Length; i++) line[i] += line[i - 1];
                var copy = line.ToArray();
                for (int i = 0; i < width; i++)
                {
                    uint bits = (uint)(copy[i] << 24 | copy[width + i] << 16 | copy[2 * width + i] << 8 | copy[3 * width + i]);
                    BinaryPrimitives.WriteUInt32LittleEndian(line[(i * 4)..], bits);
                }
                break;
            }
        }
    }

    /// <summary>PackBits undone into <paramref name="into"/>, filled exactly.</summary>
    public static void UnpackBits(ReadOnlySpan<byte> packed, Span<byte> into)
    {
        int at = 0, written = 0;
        while (written < into.Length)
        {
            if (at >= packed.Length) throw new InvalidDataException("A row of run-length data ends before its pixels do.");
            sbyte header = (sbyte)packed[at++];
            if (header >= 0)
            {
                int count = header + 1;
                if (at + count > packed.Length || written + count > into.Length) throw new InvalidDataException("A literal run runs past its row.");
                packed.Slice(at, count).CopyTo(into[written..]);
                at += count;
                written += count;
            }
            else if (header != -128)
            {
                int count = 1 - header;
                if (at >= packed.Length || written + count > into.Length) throw new InvalidDataException("A repeated run runs past its row.");
                into.Slice(written, count).Fill(packed[at++]);
                written += count;
            }
        }
    }

    // ── Writing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A channel's samples — <paramref name="height"/> rows of <paramref name="width"/>, in the machine's order, row by row —
    /// compressed as a layer's channel is written, its compression first.
    /// </summary>
    public static byte[] Encode(byte[] samples, int width, int height, int sampleBytes, bool isLarge)
    {
        int rowBytes = width * sampleBytes;
        return Encode(width, height, sampleBytes, isLarge, (y, into) => samples.AsSpan(y * rowBytes, rowBytes).CopyTo(into));
    }

    /// <summary>
    /// A channel of <paramref name="height"/> rows of <paramref name="width"/> samples, each row asked of
    /// <paramref name="rows"/> once, top first, compressed as a layer's channel is written, its compression first: 8-bit as RLE,
    /// which every reader reads; 16 and 32 bits as ZIP with prediction, as Photoshop writes them. Only the compressed channel
    /// is held.
    /// </summary>
    public static byte[] Encode(int width, int height, int sampleBytes, bool isLarge, PhotoshopRowSource rows)
    {
        using var output = new MemoryStream();
        if (sampleBytes == 1)
        {
            WriteUInt16(output, (ushort)PhotoshopCompression.Rle);
            WriteRle(output, 1, width, height, 1, isLarge, (_, y, into) => rows(y, into));
        }
        else
        {
            WriteUInt16(output, (ushort)PhotoshopCompression.ZipPredicted);
            using var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true);
            var line = new byte[width * sampleBytes];
            for (int y = 0; y < height; y++)
            {
                rows(y, line);
                Predicted(line, width, sampleBytes);
                zlib.Write(line);
            }
        }
        return output.ToArray();
    }

    /// <summary>
    /// The merged image's data — its compression, then each of its <paramref name="channels"/> channels in turn, every row
    /// asked of <paramref name="rows"/> — 8-bit as RLE, deeper raw, as every reader of a merged image reads them.
    /// </summary>
    public static void WriteMerged(Stream output, int channels, int width, int height, int sampleBytes, bool isLarge, PhotoshopChannelRows rows)
    {
        if (sampleBytes == 1)
        {
            WriteUInt16(output, (ushort)PhotoshopCompression.Rle);
            WriteRle(output, channels, width, height, 1, isLarge, rows);
            return;
        }
        WriteUInt16(output, (ushort)PhotoshopCompression.Raw);
        var line = new byte[width * sampleBytes];
        for (int c = 0; c < channels; c++)
            for (int y = 0; y < height; y++)
            {
                rows(c, y, line);
                ToMachine(line, sampleBytes);   // its own inverse: the machine's order to the file's
                output.Write(line);
            }
    }

    /// <summary>Channels' rows PackBits-compressed: every row's byte count first, then the rows.</summary>
    private static void WriteRle(Stream output, int channels, int width, int height, int sampleBytes, bool isLarge, PhotoshopChannelRows rows)
    {
        var line = new byte[width * sampleBytes];
        var packed = new List<byte[]>(channels * height);
        for (int c = 0; c < channels; c++)
            for (int y = 0; y < height; y++)
            {
                rows(c, y, line);
                packed.Add(PackBits(line));
            }
        foreach (var row in packed)
        {
            if (isLarge) WriteUInt32(output, (uint)row.Length);
            else WriteUInt16(output, checked((ushort)row.Length));
        }
        foreach (var row in packed) output.Write(row);
    }

    /// <summary>A row as PackBits: runs of three or more alike repeated, the rest literal, at most 128 a run.</summary>
    public static byte[] PackBits(ReadOnlySpan<byte> row)
    {
        var packed = new List<byte>(row.Length + row.Length / 128 + 1);
        int i = 0;
        while (i < row.Length)
        {
            int run = 1;
            while (i + run < row.Length && run < 128 && row[i + run] == row[i]) run++;
            if (run >= 3)
            {
                packed.Add((byte)(sbyte)(1 - run));
                packed.Add(row[i]);
                i += run;
                continue;
            }
            int start = i, literal = 0;
            while (i < row.Length && literal < 128)
            {
                if (i + 2 < row.Length && row[i] == row[i + 1] && row[i] == row[i + 2]) break;
                i++;
                literal++;
            }
            packed.Add((byte)(literal - 1));
            for (int k = start; k < start + literal; k++) packed.Add(row[k]);
        }
        return [.. packed];
    }

    /// <summary>A row of samples in the machine's order as ZIP prediction stores it, in place: big-endian, each the step from
    /// the one before — a float row split into its byte planes and stepped byte by byte.</summary>
    private static void Predicted(Span<byte> line, int width, int sampleBytes)
    {
        switch (sampleBytes)
        {
            case 1:
                for (int i = line.Length - 1; i > 0; i--) line[i] -= line[i - 1];
                break;
            case 2:
            {
                ushort previous = 0;
                for (int i = 0; i < width; i++)
                {
                    ushort value = BinaryPrimitives.ReadUInt16LittleEndian(line[(i * 2)..]);
                    BinaryPrimitives.WriteUInt16BigEndian(line[(i * 2)..], (ushort)(value - previous));
                    previous = value;
                }
                break;
            }
            default:
            {
                var copy = line.ToArray();
                for (int i = 0; i < width; i++)
                {
                    uint bits = BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(i * 4));
                    line[i] = (byte)(bits >> 24);
                    line[width + i] = (byte)(bits >> 16);
                    line[2 * width + i] = (byte)(bits >> 8);
                    line[3 * width + i] = (byte)bits;
                }
                for (int i = line.Length - 1; i > 0; i--) line[i] -= line[i - 1];
                break;
            }
        }
    }

    // ── Bytes ───────────────────────────────────────────────────────────────

    internal static ushort ReadUInt16(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[2];
        stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt16BigEndian(bytes);
    }

    internal static uint ReadUInt32(Stream stream)
    {
        Span<byte> bytes = stackalloc byte[4];
        stream.ReadExactly(bytes);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    internal static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        stream.Write(bytes);
    }

    internal static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    /// <summary>A window on a stream: reads stop at its length, so a zlib stream reading ahead never reads past its channel.</summary>
    private sealed class BoundedStream(Stream inner, long length) : Stream
    {
        private long _left = Math.Max(0, length);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_left <= 0) return 0;
            int read = inner.Read(buffer[..(int)Math.Min(buffer.Length, _left)]);
            _left -= read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// A window on part of a stream, with a place of its own: each read goes to where this window last left off, whatever
    /// another window read in between — so several channels are read in step through the one stream.
    /// </summary>
    private sealed class WindowStream(Stream inner, long offset, long length) : Stream
    {
        private long _at;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => _at; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            long left = length - _at;
            if (left <= 0) return 0;
            inner.Position = offset + _at;
            int read = inner.Read(buffer[..(int)Math.Min(buffer.Length, left)]);
            _at += read;
            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

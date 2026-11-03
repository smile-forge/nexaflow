using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

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
        try
        {
            stream.Position = offset;
            var compression = (PhotoshopCompression)ReadUInt16(stream);
            DecodeRows(stream, compression, stream.Length - stream.Position, channels, width, height, sampleBytes, isLarge, row);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("The Photoshop file ends before its merged image does.", ex);
        }
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
        if (sampleBytes == 2)
        {
            var samples = MemoryMarshal.Cast<byte, ushort>(line);
            BinaryPrimitives.ReverseEndianness(samples, samples);
            return;
        }
        var wide = MemoryMarshal.Cast<byte, uint>(line);
        BinaryPrimitives.ReverseEndianness(wide, wide);
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
        using var encoder = new PhotoshopChannelEncoder(width, height, sampleBytes, isLarge);
        var line = new byte[width * sampleBytes];
        for (int y = 0; y < height; y++)
        {
            rows(y, line);
            encoder.Add(line);
        }
        return encoder.Finish();
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
        var packed = new PackedRows(channels * height);
        for (int c = 0; c < channels; c++)
            for (int y = 0; y < height; y++)
            {
                rows(c, y, line);
                packed.Add(line);
            }
        packed.WriteLengths(output, isLarge);
        packed.WritePacked(output);
    }

    /// <summary>A row as PackBits: runs of three or more alike repeated, the rest literal, at most 128 a run.</summary>
    public static byte[] PackBits(ReadOnlySpan<byte> row)
    {
        var packed = new byte[PackedBound(row.Length)];
        return packed.AsSpan(0, PackBits(row, packed)).ToArray();
    }

    /// <summary>The most bytes a row of <paramref name="length"/> bytes packs to: at worst every 128 cost one more, a literal
    /// run's header.</summary>
    public static int PackedBound(int length) => length + (length + 127) / 128;

    /// <summary>
    /// A row as PackBits into <paramref name="into"/>, which holds at least <see cref="PackedBound"/> bytes; the bytes written.
    /// Runs of three or more alike are repeated, the rest literal, at most 128 a run — both found many bytes at a time.
    /// </summary>
    public static int PackBits(ReadOnlySpan<byte> row, Span<byte> into)
    {
        int i = 0, o = 0;
        while (i < row.Length)
        {
            byte value = row[i];
            int most = Math.Min(128, row.Length - i);
            int run = row.Slice(i, most).IndexOfAnyExcept(value);
            if (run < 0) run = most;
            if (run >= 3)
            {
                into[o++] = (byte)(sbyte)(1 - run);
                into[o++] = value;
                i += run;
                continue;
            }
            // A literal runs to the next three alike, or 128 bytes; none starts here, so it holds at least one.
            int literal = NextTriple(row, i, Math.Min(row.Length, i + 128)) - i;
            into[o++] = (byte)(literal - 1);
            row.Slice(i, literal).CopyTo(into[o..]);
            o += literal;
            i += literal;
        }
        return o;
    }

    /// <summary>The first place from <paramref name="from"/>, before <paramref name="end"/>, where three bytes alike begin —
    /// sixteen places compared at a time — or <paramref name="end"/>.</summary>
    private static int NextTriple(ReadOnlySpan<byte> row, int from, int end)
    {
        int j = from;
        for (; j < end && j + 2 + Vector128<byte>.Count <= row.Length; j += Vector128<byte>.Count)
        {
            var first = Vector128.Create(row.Slice(j, Vector128<byte>.Count));
            var second = Vector128.Create(row.Slice(j + 1, Vector128<byte>.Count));
            var third = Vector128.Create(row.Slice(j + 2, Vector128<byte>.Count));
            uint alike = (Vector128.Equals(first, second) & Vector128.Equals(second, third)).ExtractMostSignificantBits();
            if (alike != 0) return Math.Min(end, j + BitOperations.TrailingZeroCount(alike));
        }
        for (; j < end; j++)
            if (j + 2 < row.Length && row[j] == row[j + 1] && row[j] == row[j + 2]) return j;
        return end;
    }

    /// <summary>A row of samples in the machine's order as ZIP prediction stores it, in place: big-endian, each the step from
    /// the one before — a float row split into its byte planes and stepped byte by byte.</summary>
    internal static void Predict(Span<byte> line, int width, int sampleBytes)
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

/// <summary>Rows packed as PackBits one after another in one buffer, and each one's length — as an RLE channel holds
/// them: every length, then every row.</summary>
internal sealed class PackedRows(int rows)
{
    private readonly int[] _lengths = new int[rows];
    private byte[] _bytes = [];
    private int _count, _used;

    public int Count => _count;

    public void Add(ReadOnlySpan<byte> row)
    {
        int needed = _used + PhotoshopChannels.PackedBound(row.Length);
        if (needed > _bytes.Length) Array.Resize(ref _bytes, Math.Max(needed, (int)Math.Min(Array.MaxLength, 2L * _bytes.Length)));
        int length = PhotoshopChannels.PackBits(row, _bytes.AsSpan(_used));
        _lengths[_count++] = length;
        _used += length;
    }

    public void WriteLengths(Stream output, bool isLarge)
    {
        for (int i = 0; i < _count; i++)
        {
            if (isLarge) PhotoshopChannels.WriteUInt32(output, (uint)_lengths[i]);
            else PhotoshopChannels.WriteUInt16(output, checked((ushort)_lengths[i]));
        }
    }

    public void WritePacked(Stream output) => output.Write(_bytes, 0, _used);
}

/// <summary>
/// A layer's channel compressed as its rows are handed in, top first, in the machine's order: 8-bit as RLE, which every
/// reader reads; 16 and 32 bits as ZIP with prediction, as Photoshop writes them. Only what is compressed is held, so a
/// layer's channels can be compressed side by side from one read of its pixels.
/// </summary>
public sealed class PhotoshopChannelEncoder : IDisposable
{
    private readonly int _width, _height, _sampleBytes;
    private readonly bool _large;
    private readonly PackedRows? _packed;
    private readonly MemoryStream? _zipped;
    private readonly System.IO.Compression.ZLibStream? _zlib;
    private readonly byte[] _line;
    private int _rows;

    public PhotoshopChannelEncoder(int width, int height, int sampleBytes, bool isLarge)
    {
        (_width, _height, _sampleBytes, _large) = (width, height, sampleBytes, isLarge);
        _line = new byte[width * sampleBytes];
        if (sampleBytes == 1)
        {
            _packed = new PackedRows(height);
            return;
        }
        _zipped = new MemoryStream();
        PhotoshopChannels.WriteUInt16(_zipped, (ushort)PhotoshopCompression.ZipPredicted);
        _zlib = new System.IO.Compression.ZLibStream(_zipped, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true);
    }

    /// <summary>The next row's samples, <c>width</c> of them.</summary>
    public void Add(ReadOnlySpan<byte> row)
    {
        if (_rows++ >= _height) throw new InvalidOperationException("The channel has all its rows.");
        if (_packed is not null)
        {
            _packed.Add(row[.._line.Length]);
            return;
        }
        row[.._line.Length].CopyTo(_line);
        PhotoshopChannels.Predict(_line, _width, _sampleBytes);
        _zlib!.Write(_line);
    }

    /// <summary>The channel compressed, its compression first; every row must have been handed in.</summary>
    public byte[] Finish()
    {
        if (_rows != _height) throw new InvalidOperationException($"The channel has {_rows} of its {_height} rows.");
        if (_packed is null)
        {
            _zlib!.Dispose();
            return _zipped!.ToArray();
        }
        using var output = new MemoryStream();
        PhotoshopChannels.WriteUInt16(output, (ushort)PhotoshopCompression.Rle);
        _packed.WriteLengths(output, _large);
        _packed.WritePacked(output);
        return output.ToArray();
    }

    public void Dispose()
    {
        _zlib?.Dispose();
        _zipped?.Dispose();
    }
}

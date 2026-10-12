using System.Buffers.Binary;

namespace Nexaflow.Features.Images.Editing.Metadata;

/// <summary>
/// One TIFF directory entry: a tag, its field type, and its value bytes exactly as stored — in the byte order of the
/// file it came from, which is the order it is written back in.
/// </summary>
internal sealed record TiffEntry(ushort Tag, ushort Type, uint Count, byte[] Value)
{
    public const ushort Byte = 1, Ascii = 2, Short = 3, Long = 4, Rational = 5, Undefined = 7;

    /// <summary>Bytes per value of a field type; 0 for a type this reader does not know.</summary>
    public static int SizeOf(ushort type) => type switch
    {
        1 or 2 or 6 or 7 => 1,
        3 or 8           => 2,
        4 or 9 or 11 or 13 => 4,
        5 or 10 or 12    => 8,
        _                => 0,
    };
}

/// <summary>
/// A TIFF directory as data: its entries, the directories its pointer tags lead to, and — for an image directory —
/// the strips its offsets name. Built by <see cref="TiffReader"/> or by hand, and laid out by <see cref="TiffWriter"/>.
/// </summary>
internal sealed class TiffDirectory
{
    public List<TiffEntry> Entries { get; } = [];

    /// <summary>Sub-directories by the pointer tag that leads to them (EXIF, GPS, interoperability).</summary>
    public SortedDictionary<ushort, TiffDirectory> Children { get; } = [];

    /// <summary>Pixel strips, for an image directory; the writer fills in their offsets and byte counts.</summary>
    public List<byte[]> Strips { get; } = [];

    public TiffEntry? Find(ushort tag) => Entries.FirstOrDefault(e => e.Tag == tag);

    public void Set(TiffEntry entry)
    {
        Entries.RemoveAll(e => e.Tag == entry.Tag);
        Entries.Add(entry);
    }
}

/// <summary>Reads TIFF structure — a TIFF file, or the TIFF-shaped body of an EXIF block.</summary>
internal sealed class TiffReader
{
    public const ushort ExifPointer = 0x8769, GpsPointer = 0x8825, InteropPointer = 0xA005;
    private const int MaxEntries = 4096;

    private readonly byte[] _data;

    private TiffReader(byte[] data, bool bigEndian)
    {
        _data     = data;
        BigEndian = bigEndian;
    }

    public bool BigEndian { get; }

    /// <summary>A reader over <paramref name="data"/>, or null when it does not start with a TIFF header.</summary>
    public static TiffReader? Open(byte[] data)
    {
        if (data.Length < 8) return null;
        bool big = data[0] == 'M' && data[1] == 'M';
        if (!big && !(data[0] == 'I' && data[1] == 'I')) return null;
        var reader = new TiffReader(data, big);
        return reader.U16(2) == 42 ? reader : null;
    }

    public uint FirstDirectoryOffset => U32(4);

    /// <summary>
    /// The directory at <paramref name="offset"/> with the sub-directories its <paramref name="pointers"/> lead to,
    /// and the offset of the next directory in the chain (0 at the end). Null when the structure is damaged.
    /// </summary>
    public TiffDirectory? ReadDirectory(uint offset, out uint next, params ushort[] pointers)
    {
        next = 0;
        if (offset < 8 || offset + 2 > _data.Length) return null;
        int count = U16((int)offset);
        if (count > MaxEntries || offset + 2 + 12L * count + 4 > _data.Length) return null;

        var directory = new TiffDirectory();
        for (int i = 0; i < count; i++)
        {
            int at = (int)offset + 2 + 12 * i;
            ushort tag = U16(at), type = U16(at + 2);
            uint n = U32(at + 4);
            int size = TiffEntry.SizeOf(type);
            if (size == 0) continue;
            long length = (long)size * n;

            if (Array.IndexOf(pointers, tag) >= 0 && length == 4)
            {
                if (ReadDirectory(U32(at + 8), out _, ChildPointers(tag)) is { } child) directory.Children[tag] = child;
                continue;
            }

            long valueAt = length <= 4 ? at + 8 : U32(at + 8);
            if (valueAt < 0 || valueAt + length > _data.Length) continue;
            directory.Entries.Add(new TiffEntry(tag, type, n, _data.AsSpan((int)valueAt, (int)length).ToArray()));
        }
        next = U32((int)offset + 2 + 12 * count);
        return directory;
    }

    /// <summary>Each entry of the directory at <paramref name="offset"/>, with where its value field sits — for patching in place.</summary>
    public IEnumerable<(ushort Tag, ushort Type, uint Count, int FieldAt)> Fields(uint offset)
    {
        if (offset < 8 || offset + 2 > _data.Length) yield break;
        int count = U16((int)offset);
        if (count > MaxEntries || offset + 2 + 12L * count + 4 > _data.Length) yield break;
        for (int i = 0; i < count; i++)
        {
            int at = (int)offset + 2 + 12 * i;
            yield return (U16(at), U16(at + 2), U32(at + 4), at + 8);
        }
    }

    /// <summary>Where the next-directory link of the directory at <paramref name="offset"/> is stored.</summary>
    public int NextLinkAt(uint offset) => (int)offset + 2 + 12 * U16((int)offset);

    public ushort U16(int at) => BigEndian
        ? BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(at))
        : BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(at));

    public uint U32(int at) => BigEndian
        ? BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(at))
        : BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(at));

    private static ushort[] ChildPointers(ushort tag) => tag == ExifPointer ? [InteropPointer] : [];
}

/// <summary>
/// Lays out TIFF directories into bytes: the header, each directory followed by its out-of-line values, its
/// sub-directories after it, and any strips at the end — every value on a word boundary, entries in tag order.
/// </summary>
internal sealed class TiffWriter(bool bigEndian)
{
    public const ushort StripOffsets = 273, StripByteCounts = 279;

    public bool BigEndian { get; } = bigEndian;

    public byte[] Write(TiffDirectory root)
    {
        var order = new List<TiffDirectory>();
        Collect(root, order);

        // First pass: where everything goes. Pointer and strip-offset values are fixed-size, so the layout does not
        // depend on them.
        var directoryAt = new Dictionary<TiffDirectory, uint>();
        var valueAt = new Dictionary<(TiffDirectory, ushort), uint>();
        uint at = 8;
        foreach (var directory in order)
        {
            PrepareStripEntries(directory);
            directoryAt[directory] = at;
            at += 2 + 12u * (uint)(directory.Entries.Count + directory.Children.Count) + 4;
            foreach (var entry in directory.Entries.Where(e => e.Value.Length > 4))
            {
                at = Align(at);
                valueAt[(directory, entry.Tag)] = at;
                at += (uint)entry.Value.Length;
            }
        }
        var stripAt = new Dictionary<byte[], uint>(ReferenceEqualityComparer.Instance);
        foreach (var strip in order.SelectMany(d => d.Strips))
        {
            at = Align(at);
            stripAt[strip] = at;
            at += (uint)strip.Length;
        }

        // Second pass: the bytes.
        var output = new byte[at];
        output[0] = output[1] = (byte)(BigEndian ? 'M' : 'I');
        U16(output, 2, 42);
        U32(output, 4, directoryAt[root]);

        foreach (var directory in order)
        {
            if (directory.Strips.Count > 0)
            {
                var offsets = directory.Strips.Select(s => stripAt[s]).ToArray();
                directory.Set(Longs(StripOffsets, offsets));
            }

            var entries = directory.Entries
                .Select(e => (e.Tag, Entry: (TiffEntry?)e, Child: (TiffDirectory?)null))
                .Concat(directory.Children.Select(c => (c.Key, Entry: (TiffEntry?)null, Child: (TiffDirectory?)c.Value)))
                .OrderBy(e => e.Item1)
                .ToList();

            int p = (int)directoryAt[directory];
            U16(output, p, (ushort)entries.Count);
            p += 2;
            foreach (var (tag, entry, child) in entries)
            {
                U16(output, p, tag);
                if (child is not null)
                {
                    U16(output, p + 2, TiffEntry.Long);
                    U32(output, p + 4, 1);
                    U32(output, p + 8, directoryAt[child]);
                }
                else
                {
                    U16(output, p + 2, entry!.Type);
                    U32(output, p + 4, entry.Count);
                    if (entry.Value.Length <= 4) entry.Value.CopyTo(output, p + 8);
                    else
                    {
                        uint valueOffset = valueAt[(directory, tag)];
                        U32(output, p + 8, valueOffset);
                        entry.Value.CopyTo(output, (int)valueOffset);
                    }
                }
                p += 12;
            }
            U32(output, p, 0);   // one image directory, no chain: thumbnails and extra pages are not carried
        }

        foreach (var (strip, offset) in stripAt) strip.CopyTo(output, (int)offset);
        return output;
    }

    /// <summary>A LONG-array entry in this writer's byte order.</summary>
    public TiffEntry Longs(ushort tag, params uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (int i = 0; i < values.Length; i++) U32(bytes, i * 4, values[i]);
        return new TiffEntry(tag, TiffEntry.Long, (uint)values.Length, bytes);
    }

    /// <summary>A SHORT-array entry in this writer's byte order.</summary>
    public TiffEntry Shorts(ushort tag, params ushort[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (int i = 0; i < values.Length; i++) U16(bytes, i * 2, values[i]);
        return new TiffEntry(tag, TiffEntry.Short, (uint)values.Length, bytes);
    }

    /// <summary>A RATIONAL entry in this writer's byte order.</summary>
    public TiffEntry Rational(ushort tag, uint numerator, uint denominator)
    {
        var bytes = new byte[8];
        U32(bytes, 0, numerator);
        U32(bytes, 4, denominator);
        return new TiffEntry(tag, TiffEntry.Rational, 1, bytes);
    }

    private void PrepareStripEntries(TiffDirectory directory)
    {
        if (directory.Strips.Count == 0) return;
        // Placeholders of the final size, so the layout is right before the offsets are known.
        directory.Set(Longs(StripOffsets, new uint[directory.Strips.Count]));
        directory.Set(Longs(StripByteCounts, [.. directory.Strips.Select(s => (uint)s.Length)]));
    }

    private static void Collect(TiffDirectory directory, List<TiffDirectory> order)
    {
        order.Add(directory);
        foreach (var child in directory.Children.Values) Collect(child, order);
    }

    private static uint Align(uint at) => (at + 1) & ~1u;

    private void U16(byte[] buffer, int at, ushort value)
    {
        if (BigEndian) BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(at), value);
        else BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(at), value);
    }

    private void U32(byte[] buffer, int at, uint value)
    {
        if (BigEndian) BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(at), value);
        else BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(at), value);
    }
}

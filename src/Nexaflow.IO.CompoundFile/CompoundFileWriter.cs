using System.Buffers.Binary;
using System.Text;

namespace Nexaflow.IO.CompoundFile;

/// <summary>
/// A storage to be written into a compound file: its streams and the storages inside it, each known by a name unique
/// within it, case aside.
/// </summary>
public sealed class CompoundFolder
{
    private readonly List<CompoundFolder> _folders = [];
    private readonly List<KeyValuePair<string, byte[]>> _streams = [];

    public CompoundFolder(string name)
    {
        CompoundFileWriter.Named(name);
        Name = name;
    }

    /// <summary>What it is called. The root's name is the format's own, whatever this says.</summary>
    public string Name { get; }

    /// <summary>The class the storage says it is an object of.</summary>
    public Guid Class { get; init; }

    public IReadOnlyList<CompoundFolder> Folders => _folders;

    public IReadOnlyList<KeyValuePair<string, byte[]>> Streams => _streams;

    /// <summary>Adds a stream, or puts <paramref name="bytes"/> in place of the stream already called <paramref name="name"/>.</summary>
    public void Add(string name, byte[] bytes)
    {
        CompoundFileWriter.Named(name);
        ArgumentNullException.ThrowIfNull(bytes);

        var at = _streams.FindIndex(stream => string.Equals(stream.Key, name, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) _streams[at] = new(name, bytes);
        else
        {
            Unique(name);
            _streams.Add(new(name, bytes));
        }
    }

    /// <summary>Adds a storage.</summary>
    public void Add(CompoundFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        Unique(folder.Name);
        _folders.Add(folder);
    }

    private void Unique(string name)
    {
        if (_folders.Any(folder => string.Equals(folder.Name, name, StringComparison.OrdinalIgnoreCase)) ||
            _streams.Any(stream => string.Equals(stream.Key, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"'{Name}' already holds something called '{name}'.", nameof(name));
    }
}

/// <summary>
/// Writes a compound file (MS-CFB) from a tree of storages and streams: version 3, 512-byte sectors, every stream under
/// 4096 bytes kept in the mini stream as the format asks — the shape every reader of the format reads.
///
/// <para>
/// Each storage's entries are kept as the red-black tree the format describes, ordered as it orders names — shorter
/// first, then by their capitals — so a reader that searches the tree finds what one that walks it does.
/// </para>
/// </summary>
public static class CompoundFileWriter
{
    private const int SectorSize = 512;
    private const int MiniSectorSize = 64;
    private const int Cutoff = 4096;
    private const int PerSector = SectorSize / 4;
    private const int EntriesPerSector = SectorSize / CompoundFile.EntrySize;

    /// <summary>The file <paramref name="root"/> is, as bytes.</summary>
    public static byte[] Write(CompoundFolder root)
    {
        using var bytes = new MemoryStream();
        Write(root, bytes);
        return bytes.ToArray();
    }

    /// <summary>Writes the file <paramref name="root"/> is to <paramref name="into"/>, from where it stands.</summary>
    public static void Write(CompoundFolder root, Stream into)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(into);

        var entries = Flattened(root);

        // The small streams, one after another in the mini stream, each from a mini sector of its own.
        var miniFat = new List<uint>();
        using var mini = new MemoryStream();

        foreach (var entry in entries.Where(entry => entry.Bytes is { Length: > 0 and < Cutoff }))
        {
            var count = Count(entry.Bytes!.Length, MiniSectorSize);
            entry.Start = (uint)miniFat.Count;

            for (var at = 0; at < count; at++) miniFat.Add(at == count - 1 ? CompoundFile.EndOfChain : (uint)(miniFat.Count + 1));

            mini.Write(entry.Bytes);
            mini.Write(new byte[(count * MiniSectorSize) - entry.Bytes.Length]);
        }

        var large = entries.Where(entry => entry.Bytes is { Length: >= Cutoff }).ToList();

        var directorySectors = Count(entries.Count, EntriesPerSector);
        var miniFatSectors = Count(miniFat.Count, PerSector);
        var miniSectors = Count((int)mini.Length, SectorSize);
        var largeSectors = large.Sum(entry => Count(entry.Bytes!.Length, SectorSize));
        var content = directorySectors + miniFatSectors + miniSectors + largeSectors;

        // The allocation table maps itself and the DIFAT sectors that name it, so its size is found by going round
        // until it stops growing.
        var (fatSectors, difatSectors) = (1, 0);
        while (true)
        {
            var difat = fatSectors > CompoundFile.HeaderDifat ? Count(fatSectors - CompoundFile.HeaderDifat, PerSector - 1) : 0;
            var needed = Count(content + fatSectors + difat, PerSector);
            if (needed <= fatSectors && difat == difatSectors) break;
            (fatSectors, difatSectors) = (Math.Max(needed, fatSectors), difat);
        }

        var fat = new uint[fatSectors * PerSector];
        Array.Fill(fat, CompoundFile.FreeSector);

        var next = 0u;
        for (var at = 0; at < fatSectors; at++) fat[next++] = CompoundFile.FatSector;
        var firstDifat = difatSectors > 0 ? next : CompoundFile.EndOfChain;
        for (var at = 0; at < difatSectors; at++) fat[next++] = CompoundFile.DifatSector;

        var firstDirectory = Chained(fat, ref next, directorySectors);
        var firstMiniFat = Chained(fat, ref next, miniFatSectors);
        var firstMini = Chained(fat, ref next, miniSectors);

        foreach (var entry in large) entry.Start = Chained(fat, ref next, Count(entry.Bytes!.Length, SectorSize));

        entries[0].Start = firstMini;
        entries[0].Size = mini.Length;

        // ── Writing it out ──

        var header = Header(fatSectors, firstDirectory, firstMiniFat, miniFatSectors, firstDifat, difatSectors);
        into.Write(header);

        var sector = new byte[SectorSize];

        for (var at = 0; at < fatSectors; at++)
        {
            for (var word = 0; word < PerSector; word++)
                BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(word * 4), fat[(at * PerSector) + word]);
            into.Write(sector);
        }

        for (var at = 0; at < difatSectors; at++)
        {
            Array.Clear(sector);
            for (var word = 0; word < PerSector - 1; word++)
            {
                var named = CompoundFile.HeaderDifat + (at * (PerSector - 1)) + word;
                BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(word * 4), named < fatSectors ? (uint)named : CompoundFile.FreeSector);
            }

            var following = at == difatSectors - 1 ? CompoundFile.EndOfChain : (uint)(fatSectors + at + 1);
            BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan((PerSector - 1) * 4), following);
            into.Write(sector);
        }

        Padded(into, Directory(entries), directorySectors);
        // What the mini allocation table does not use is free, not a link to mini sector nought.
        while (miniFat.Count % PerSector != 0) miniFat.Add(CompoundFile.FreeSector);
        Padded(into, Words(miniFat), miniFatSectors);
        Padded(into, mini.ToArray(), miniSectors);

        foreach (var entry in large) Padded(into, entry.Bytes!, Count(entry.Bytes!.Length, SectorSize));
    }

    // ── Names ───────────────────────────────────────────────────────────────

    /// <summary>A name the format can hold: one to 31 characters, none of them a slash, a backslash, a colon or a bang.</summary>
    internal static void Named(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (name.Length > 31 || name.IndexOfAny(['/', '\\', ':', '!']) >= 0)
            throw new ArgumentException($"'{name}' is not a name a compound file can hold.", nameof(name));
    }

    /// <summary>How the format orders two names in a storage: the shorter first, then by their capitals.</summary>
    private static int Ordered(string left, string right)
    {
        if (left.Length != right.Length) return left.Length.CompareTo(right.Length);

        for (var at = 0; at < left.Length; at++)
        {
            var compared = char.ToUpperInvariant(left[at]).CompareTo(char.ToUpperInvariant(right[at]));
            if (compared != 0) return compared;
        }

        return 0;
    }

    // ── The directory ───────────────────────────────────────────────────────

    /// <summary>One entry of the directory, as it is laid out.</summary>
    private sealed class Laid(string name, CompoundEntryType type, Guid @class, byte[]? bytes)
    {
        public string Name { get; } = name;
        public CompoundEntryType Type { get; } = type;
        public Guid Class { get; } = @class;
        public byte[]? Bytes { get; } = bytes;
        public uint Left { get; set; } = CompoundFile.NoStream;
        public uint Right { get; set; } = CompoundFile.NoStream;
        public uint Child { get; set; } = CompoundFile.NoStream;
        public bool Red { get; set; }
        public uint Start { get; set; } = CompoundFile.EndOfChain;
        public long Size { get; set; }
    }

    /// <summary>Every storage and stream, the root first, each storage's entries made into its tree.</summary>
    private static List<Laid> Flattened(CompoundFolder root)
    {
        var entries = new List<Laid> { new("Root Entry", CompoundEntryType.Root, root.Class, null) };
        var waiting = new Queue<(CompoundFolder Folder, int Id)>();
        waiting.Enqueue((root, 0));

        while (waiting.Count > 0)
        {
            var (folder, id) = waiting.Dequeue();
            var children = new List<int>();

            foreach (var inner in folder.Folders)
            {
                children.Add(entries.Count);
                waiting.Enqueue((inner, entries.Count));
                entries.Add(new Laid(inner.Name, CompoundEntryType.Storage, inner.Class, null));
            }

            foreach (var (name, bytes) in folder.Streams)
            {
                children.Add(entries.Count);
                entries.Add(new Laid(name, CompoundEntryType.Stream, Guid.Empty, bytes) { Size = bytes.Length });
            }

            children.Sort((left, right) => Ordered(entries[left].Name, entries[right].Name));
            entries[id].Child = Treed(entries, children, 0, children.Count - 1, 0, Deepest(children.Count), Full(children.Count));
        }

        return entries;
    }

    /// <summary>
    /// A balanced tree of <paramref name="sorted"/>, its root the middle one. Black throughout but for the deepest row
    /// where that row is not full, which is red — so every way down passes as many black entries, which is what makes it
    /// the red-black tree the format says a storage is.
    /// </summary>
    private static uint Treed(List<Laid> entries, List<int> sorted, int from, int to, int depth, int deepest, bool full)
    {
        if (from > to) return CompoundFile.NoStream;

        var middle = from + ((to - from) / 2);
        var entry = entries[sorted[middle]];

        entry.Left = Treed(entries, sorted, from, middle - 1, depth + 1, deepest, full);
        entry.Right = Treed(entries, sorted, middle + 1, to, depth + 1, deepest, full);
        entry.Red = !full && depth == deepest;

        return (uint)sorted[middle];
    }

    private static int Deepest(int count) => count <= 1 ? 0 : (int)Math.Floor(Math.Log2(count));

    private static bool Full(int count) => count > 0 && ((count + 1) & count) == 0;

    private static byte[] Directory(List<Laid> entries)
    {
        var bytes = new byte[Count(entries.Count, EntriesPerSector) * SectorSize];

        for (var at = 0; at < bytes.Length / CompoundFile.EntrySize; at++)
        {
            var raw = bytes.AsSpan(at * CompoundFile.EntrySize, CompoundFile.EntrySize);

            if (at >= entries.Count)
            {
                // An unused entry names nothing.
                BinaryPrimitives.WriteUInt32LittleEndian(raw[68..], CompoundFile.NoStream);
                BinaryPrimitives.WriteUInt32LittleEndian(raw[72..], CompoundFile.NoStream);
                BinaryPrimitives.WriteUInt32LittleEndian(raw[76..], CompoundFile.NoStream);
                continue;
            }

            var entry = entries[at];
            Encoding.Unicode.GetBytes(entry.Name, raw);
            BinaryPrimitives.WriteUInt16LittleEndian(raw[64..], (ushort)((entry.Name.Length + 1) * 2));
            raw[66] = (byte)entry.Type;
            raw[67] = entry.Red ? (byte)0 : (byte)1;
            BinaryPrimitives.WriteUInt32LittleEndian(raw[68..], entry.Left);
            BinaryPrimitives.WriteUInt32LittleEndian(raw[72..], entry.Right);
            BinaryPrimitives.WriteUInt32LittleEndian(raw[76..], entry.Child);
            entry.Class.TryWriteBytes(raw.Slice(80, 16));
            BinaryPrimitives.WriteUInt32LittleEndian(raw[116..], entry.Size == 0 && entry.Type == CompoundEntryType.Stream ? CompoundFile.EndOfChain : entry.Start);
            BinaryPrimitives.WriteUInt64LittleEndian(raw[120..], (ulong)entry.Size);
        }

        return bytes;
    }

    // ── Sectors ─────────────────────────────────────────────────────────────

    private static byte[] Header(int fatSectors, uint firstDirectory, uint firstMiniFat, int miniFatSectors, uint firstDifat, int difatSectors)
    {
        var header = new byte[CompoundFile.HeaderSize];

        CompoundFile.Signature.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(24), 0x3E);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(26), 3);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(28), 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(30), 9);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(32), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), (uint)fatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(48), firstDirectory);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(56), Cutoff);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(60), miniFatSectors > 0 ? firstMiniFat : CompoundFile.EndOfChain);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(64), (uint)miniFatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(68), firstDifat);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(72), (uint)difatSectors);

        for (var at = 0; at < CompoundFile.HeaderDifat; at++)
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(76 + (at * 4)), at < fatSectors ? (uint)at : CompoundFile.FreeSector);

        return header;
    }

    /// <summary><paramref name="count"/> sectors chained one after another from <paramref name="next"/>, or none.</summary>
    private static uint Chained(uint[] fat, ref uint next, int count)
    {
        if (count == 0) return CompoundFile.EndOfChain;

        var first = next;
        for (var at = 0; at < count; at++, next++) fat[next] = at == count - 1 ? CompoundFile.EndOfChain : next + 1;

        return first;
    }

    private static void Padded(Stream into, byte[] bytes, int sectors)
    {
        into.Write(bytes);
        var pad = (sectors * SectorSize) - bytes.Length;
        if (pad > 0) into.Write(new byte[pad]);
    }

    private static byte[] Words(List<uint> words)
    {
        var bytes = new byte[words.Count * 4];
        for (var at = 0; at < words.Count; at++) BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at * 4), words[at]);
        return bytes;
    }

    private static int Count(int length, int per) => (length + per - 1) / per;
}

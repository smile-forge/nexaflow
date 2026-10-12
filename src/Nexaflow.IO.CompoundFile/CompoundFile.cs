using System.Buffers.Binary;
using System.Text;

namespace Nexaflow.IO.CompoundFile;

/// <summary>
/// A compound file (MS-CFB, "OLE2" / "structured storage") opened for reading: a little file system inside one file, of
/// storages that hold storages and streams. Outlook's <c>.msg</c> is one, and so are the Office formats before 2007,
/// Windows Installer packages and <c>Thumbs.db</c>.
///
/// <para>
/// <strong>Read from any seekable stream, which it leaves open.</strong> The allocation tables and the directory are read
/// when it is opened; a stream's bytes only when somebody asks for them (<see cref="CompoundStream.Read"/>).
/// </para>
/// <para>
/// <strong>A damaged file is refused, never followed.</strong> Every chain of sectors is walked with a guard against
/// running in a circle or out of the file, every directory entry is checked against the directory, and a stream claiming
/// more than the file could hold is not believed — so a corrupt or hostile file throws one
/// <see cref="InvalidDataException"/> rather than hanging or allocating what it says it is.
/// </para>
/// </summary>
public sealed class CompoundFile
{
    /// <summary>What every compound file starts with.</summary>
    internal static ReadOnlySpan<byte> Signature => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    internal const uint MaxRegular = 0xFFFFFFFA;
    internal const uint DifatSector = 0xFFFFFFFC;
    internal const uint FatSector = 0xFFFFFFFD;
    internal const uint EndOfChain = 0xFFFFFFFE;
    internal const uint FreeSector = 0xFFFFFFFF;
    internal const uint NoStream = 0xFFFFFFFF;

    internal const int HeaderSize = 512;
    internal const int EntrySize = 128;
    internal const int HeaderDifat = 109;

    private readonly Stream _file;
    private readonly int _sectorSize;
    private readonly int _miniSectorSize;
    private readonly uint _cutoff;
    private readonly uint[] _fat;
    private readonly uint[] _miniFat;
    private readonly long _sectors;
    private readonly List<Entry> _entries;
    private byte[]? _miniStream;

    private CompoundFile(Stream file, int sectorSize, int miniSectorSize, uint cutoff, uint[] fat, uint[] miniFat,
                         long sectors, List<Entry> entries)
    {
        _file = file;
        _sectorSize = sectorSize;
        _miniSectorSize = miniSectorSize;
        _cutoff = cutoff;
        _fat = fat;
        _miniFat = miniFat;
        _sectors = sectors;
        _entries = entries;
        Root = new CompoundStorage(this, 0);
    }

    /// <summary>The root storage, holding everything else.</summary>
    public CompoundStorage Root { get; }

    /// <summary>Whether <paramref name="header"/> starts the way a compound file does.</summary>
    public static bool IsCompoundFile(ReadOnlySpan<byte> header) => header.StartsWith(Signature);

    /// <summary>Opens the compound file <paramref name="stream"/> holds, from its start. It must be seekable.</summary>
    /// <exception cref="InvalidDataException">It is not a compound file, or one too damaged to read.</exception>
    public static CompoundFile Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek || !stream.CanRead) throw new ArgumentException("A compound file is read from a seekable stream.", nameof(stream));

        if (stream.Length < HeaderSize) throw Damaged("it is shorter than a compound file's header");

        var header = new byte[HeaderSize];
        stream.Position = 0;
        stream.ReadExactly(header);

        if (!IsCompoundFile(header)) throw Damaged("it does not start with the compound file signature");

        var major = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26));
        var shift = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30));
        var miniShift = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32));

        if (!(major == 3 && shift == 9) && !(major == 4 && shift == 12)) throw Damaged($"version {major} with {1 << Math.Min((int)shift, 30)}-byte sectors is not one the format has");
        if (miniShift != 6) throw Damaged("its mini sectors are not 64 bytes");

        var sectorSize = 1 << shift;
        var fatSectors = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(44));
        var firstDirectory = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(48));
        var cutoff = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(56));
        var firstMiniFat = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(60));
        var miniFatSectors = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(64));
        var firstDifat = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(68));
        var difatSectors = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(72));

        // How many whole sectors follow the header — everything a chain can name.
        var sectors = Math.Max((stream.Length - sectorSize) / sectorSize, 0);

        if (fatSectors > sectors || difatSectors > sectors || miniFatSectors > sectors)
            throw Damaged("its header counts more sectors than the file holds");

        var reader = new SectorReader(stream, sectorSize, sectors);
        var fat = Fat(reader, header, fatSectors, firstDifat, difatSectors);

        var directory = reader.Chain(fat, firstDirectory, "the directory");
        var entries = Entries(directory);

        var miniFat = firstMiniFat == EndOfChain || miniFatSectors == 0
            ? []
            : Words(reader.Chain(fat, firstMiniFat, "the mini allocation table"));

        var file = new CompoundFile(stream, sectorSize, 64, cutoff == 0 ? 4096 : cutoff, fat, miniFat, sectors, entries);
        file.Check();

        return file;
    }

    // ── Opening ─────────────────────────────────────────────────────────────

    /// <summary>The allocation table: its sectors named in the header and the DIFAT chain after it, read in order.</summary>
    private static uint[] Fat(SectorReader reader, byte[] header, uint fatSectors, uint firstDifat, uint difatSectors)
    {
        var named = new List<uint>((int)fatSectors);

        for (var at = 0; at < HeaderDifat && named.Count < fatSectors; at++)
        {
            var sector = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(76 + (at * 4)));
            if (sector > MaxRegular) break;
            named.Add(sector);
        }

        var perDifat = (reader.SectorSize / 4) - 1;
        var next = firstDifat;
        var walked = 0u;

        while (named.Count < fatSectors && next <= MaxRegular)
        {
            if (++walked > difatSectors + 1) throw Damaged("its DIFAT chain runs on past the sectors it counts");

            var difat = reader.Sector(next, "the DIFAT");

            for (var at = 0; at < perDifat && named.Count < fatSectors; at++)
            {
                var sector = BinaryPrimitives.ReadUInt32LittleEndian(difat.AsSpan(at * 4));
                if (sector > MaxRegular) break;
                named.Add(sector);
            }

            next = BinaryPrimitives.ReadUInt32LittleEndian(difat.AsSpan(perDifat * 4));
        }

        if (named.Count < fatSectors) throw Damaged("its allocation table names fewer sectors than it counts");

        var fat = new uint[named.Count * (reader.SectorSize / 4)];
        var bytes = new byte[reader.SectorSize];

        for (var at = 0; at < named.Count; at++)
        {
            reader.Sector(named[at], "the allocation table", bytes);
            for (var word = 0; word < reader.SectorSize / 4; word++)
                fat[(at * (reader.SectorSize / 4)) + word] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(word * 4));
        }

        return fat;
    }

    /// <summary>Every directory entry, in the order the directory holds them.</summary>
    private static List<Entry> Entries(byte[] directory)
    {
        var count = directory.Length / EntrySize;
        var entries = new List<Entry>(count);

        for (var at = 0; at < count; at++)
        {
            var raw = directory.AsSpan(at * EntrySize, EntrySize);
            var nameBytes = BinaryPrimitives.ReadUInt16LittleEndian(raw[64..]);
            var nameLength = Math.Clamp((nameBytes / 2) - 1, 0, 31);

            entries.Add(new Entry(
                Encoding.Unicode.GetString(raw[..(nameLength * 2)]),
                (CompoundEntryType)raw[66],
                BinaryPrimitives.ReadUInt32LittleEndian(raw[68..]),
                BinaryPrimitives.ReadUInt32LittleEndian(raw[72..]),
                BinaryPrimitives.ReadUInt32LittleEndian(raw[76..]),
                new Guid(raw.Slice(80, 16)),
                BinaryPrimitives.ReadUInt32LittleEndian(raw[116..]),
                (long)BinaryPrimitives.ReadUInt64LittleEndian(raw[120..])));
        }

        if (entries.Count == 0 || entries[0].Type != CompoundEntryType.Root) throw Damaged("its directory does not begin with a root");

        return entries;
    }

    /// <summary>
    /// That every entry reachable from the root names entries that exist, is reached once, and claims no more bytes than the
    /// file could hold — checked once, so nothing that reads the tree later has to.
    /// </summary>
    private void Check()
    {
        var seen = new bool[_entries.Count];
        var waiting = new Stack<uint>();
        waiting.Push(0);
        var largest = (_sectors + 1) * _sectorSize;

        while (waiting.Count > 0)
        {
            var id = waiting.Pop();
            if (id == NoStream) continue;
            if (id >= _entries.Count) throw Damaged($"its directory names entry {id} of {_entries.Count}");
            if (seen[id]) throw Damaged($"its directory reaches entry {id} twice");

            seen[id] = true;
            var entry = _entries[(int)id];

            if (entry.Size < 0 || entry.Size > largest) throw Damaged($"'{entry.Name}' claims {entry.Size} bytes");

            if (id != 0)
            {
                waiting.Push(entry.Left);
                waiting.Push(entry.Right);
            }

            if (entry.Type is CompoundEntryType.Storage or CompoundEntryType.Root) waiting.Push(entry.Child);
        }
    }

    // ── Reading ─────────────────────────────────────────────────────────────

    /// <summary>A directory entry, as the directory holds it.</summary>
    internal sealed record Entry(string Name, CompoundEntryType Type, uint Left, uint Right, uint Child, Guid Class, uint Start, long Size);

    internal Entry EntryAt(uint id) => _entries[(int)id];

    /// <summary>The entries a storage holds, in the order their names sort — the order the tree keeps them in.</summary>
    internal IEnumerable<uint> Children(uint storage)
    {
        var walk = new Stack<uint>();
        var at = _entries[(int)storage].Child;

        // In order: left, the entry, right. Check has already made sure every one is reached once.
        while (walk.Count > 0 || at != NoStream)
        {
            while (at != NoStream)
            {
                walk.Push(at);
                at = _entries[(int)at].Left;
            }

            var entry = walk.Pop();
            yield return entry;
            at = _entries[(int)entry].Right;
        }
    }

    /// <summary>The bytes of the stream entry <paramref name="id"/> is.</summary>
    internal byte[] Read(uint id)
    {
        var entry = _entries[(int)id];
        if (entry.Size == 0) return [];

        // The format counts a version 3 file's stream sizes in 32 bits, whatever the high bits say.
        var size = _sectorSize == 512 ? entry.Size & 0xFFFFFFFF : entry.Size;

        var bytes = size < _cutoff
            ? Mini(entry.Start, size, entry.Name)
            : new SectorReader(_file, _sectorSize, _sectors).Chain(_fat, entry.Start, $"'{entry.Name}'", size);

        if (bytes.LongLength < size) throw Damaged($"'{entry.Name}' is cut short");

        return bytes.Length == size ? bytes : bytes[..(int)size];
    }

    /// <summary>A small stream's bytes, from the mini stream the root holds.</summary>
    private byte[] Mini(uint start, long size, string name)
    {
        _miniStream ??= _entries[0].Size == 0
            ? []
            : new SectorReader(_file, _sectorSize, _sectors).Chain(_fat, _entries[0].Start, "the mini stream", _entries[0].Size);

        var bytes = new byte[size];
        var written = 0;
        var next = start;
        var visited = new System.Collections.BitArray(_miniFat.Length);

        while (written < size)
        {
            if (next > MaxRegular || next >= _miniFat.Length) throw Damaged($"'{name}' runs out of mini sectors");
            if (visited[(int)next]) throw Damaged($"'{name}' runs in a circle");

            visited[(int)next] = true;

            var from = (long)next * _miniSectorSize;
            if (from + _miniSectorSize > _miniStream.Length) throw Damaged($"'{name}' names a mini sector past the mini stream");

            var take = (int)Math.Min(_miniSectorSize, size - written);
            _miniStream.AsSpan((int)from, take).CopyTo(bytes.AsSpan(written));
            written += take;
            next = _miniFat[next];
        }

        return bytes;
    }

    private static uint[] Words(byte[] bytes)
    {
        var words = new uint[bytes.Length / 4];
        for (var at = 0; at < words.Length; at++) words[at] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at * 4));
        return words;
    }

    internal static InvalidDataException Damaged(string why) => new($"Not a readable compound file: {why}.");

    /// <summary>Reads sectors of the file, and chains of them, never past its end and never round in a circle.</summary>
    private readonly struct SectorReader(Stream file, int sectorSize, long sectors)
    {
        public int SectorSize => sectorSize;

        public byte[] Sector(uint sector, string what)
        {
            var bytes = new byte[sectorSize];
            Sector(sector, what, bytes);
            return bytes;
        }

        public void Sector(uint sector, string what, byte[] into)
        {
            if (sector >= sectors) throw Damaged($"{what} names sector {sector} of {sectors}");

            file.Position = (sector + 1L) * sectorSize;
            var read = file.ReadAtLeast(into, sectorSize, throwOnEndOfStream: false);
            if (read < sectorSize) Array.Clear(into, read, sectorSize - read);
        }

        /// <summary>A chain of sectors from <paramref name="start"/>, read whole — or as far as <paramref name="size"/> needs.</summary>
        public byte[] Chain(uint[] fat, uint start, string what, long size = -1)
        {
            using var bytes = new MemoryStream(size > 0 ? (int)Math.Min(size + sectorSize, int.MaxValue) : sectorSize);
            var sector = new byte[sectorSize];
            var next = start;
            var visited = new System.Collections.BitArray((int)Math.Min(fat.Length, int.MaxValue));

            while (next != EndOfChain && (size < 0 || bytes.Length < size))
            {
                if (next > MaxRegular || next >= fat.Length) throw Damaged($"{what} names sector {next}, which is not in the file");
                if (visited[(int)next]) throw Damaged($"{what} runs in a circle");

                visited[(int)next] = true;

                Sector(next, what, sector);
                bytes.Write(sector);
                next = fat[next];
            }

            return bytes.ToArray();
        }
    }
}

/// <summary>What a directory entry is.</summary>
public enum CompoundEntryType : byte
{
    Unused = 0,
    Storage = 1,
    Stream = 2,
    Root = 5,
}

namespace Nexaflow.IO.CompoundFile;

/// <summary>
/// A storage in a compound file: a folder of streams and of storages, each known by a name that is unique within it, case
/// aside.
/// </summary>
public sealed class CompoundStorage
{
    private readonly CompoundFile _file;
    private readonly uint _id;
    private IReadOnlyList<CompoundStorage>? _storages;
    private IReadOnlyList<CompoundStream>? _streams;

    internal CompoundStorage(CompoundFile file, uint id)
    {
        _file = file;
        _id = id;

        var entry = file.EntryAt(id);
        Name = entry.Name;
        Class = entry.Class;
    }

    /// <summary>What it is called — <c>Root Entry</c> for the root.</summary>
    public string Name { get; }

    /// <summary>The class the storage says it is an object of, where it says.</summary>
    public Guid Class { get; }

    /// <summary>The storages it holds, in the order their names sort.</summary>
    public IReadOnlyList<CompoundStorage> Storages => _storages ??= Held().OfType<CompoundStorage>().ToList();

    /// <summary>The streams it holds, in the order their names sort.</summary>
    public IReadOnlyList<CompoundStream> Streams => _streams ??= Held().OfType<CompoundStream>().ToList();

    /// <summary>The stream it holds called <paramref name="name"/>, case aside, or null.</summary>
    public CompoundStream? Stream(string name) =>
        Streams.FirstOrDefault(stream => string.Equals(stream.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The storage it holds called <paramref name="name"/>, case aside, or null.</summary>
    public CompoundStorage? Storage(string name) =>
        Storages.FirstOrDefault(storage => string.Equals(storage.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// This storage and everything under it as a folder a <see cref="CompoundFileWriter"/> writes — every stream read, so
    /// what is written is whole however the file it came from is later treated.
    /// </summary>
    public CompoundFolder Copied()
    {
        var folder = new CompoundFolder(Name) { Class = Class };

        foreach (var stream in Streams) folder.Add(stream.Name, stream.Read());
        foreach (var storage in Storages) folder.Add(storage.Copied());

        return folder;
    }

    private IEnumerable<object> Held()
    {
        foreach (var child in _file.Children(_id))
        {
            switch (_file.EntryAt(child).Type)
            {
                case CompoundEntryType.Storage: yield return new CompoundStorage(_file, child); break;
                case CompoundEntryType.Stream: yield return new CompoundStream(_file, child); break;
            }
        }
    }

    public override string ToString() => Name;
}

/// <summary>A stream in a compound file: a name, and bytes read when they are asked for.</summary>
public sealed class CompoundStream
{
    private readonly CompoundFile _file;
    private readonly uint _id;

    internal CompoundStream(CompoundFile file, uint id)
    {
        _file = file;
        _id = id;

        var entry = file.EntryAt(id);
        Name = entry.Name;
        Length = entry.Size;
    }

    /// <summary>What it is called.</summary>
    public string Name { get; }

    /// <summary>How many bytes it holds, as the directory says.</summary>
    public long Length { get; }

    /// <summary>Its bytes, read whole.</summary>
    /// <exception cref="InvalidDataException">The file is damaged where the stream is.</exception>
    public byte[] Read() => _file.Read(_id);

    public override string ToString() => Name;
}

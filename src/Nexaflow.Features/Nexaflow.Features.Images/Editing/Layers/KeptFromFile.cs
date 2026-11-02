using System.Collections.Immutable;

namespace Nexaflow.Features.Images.Editing.Layers;

/// <summary>What a format kept of a layer that Nexaflow does not draw — effects, a vector mask — named, so the look a file
/// opens on can say what drawing the layers leaves out.</summary>
public interface IUndrawnKept
{
    ImmutableArray<string> Undrawn { get; }
}

/// <summary>
/// What the file a picture or a layer was read from held that Nexaflow does not understand, kept to be written back — each
/// layered format's own record of it, by its type, so OpenRaster keeps its attributes and files and Photoshop its tagged
/// blocks side by side. A format reads and writes only its own; a picture saved as another format leaves the others behind.
/// </summary>
public sealed class KeptFromFile : IEquatable<KeptFromFile>
{
    private readonly ImmutableDictionary<Type, object> _records;

    private KeptFromFile(ImmutableDictionary<Type, object> records) => _records = records;

    /// <summary>Nothing kept.</summary>
    public static KeptFromFile None { get; } = new(ImmutableDictionary<Type, object>.Empty);

    /// <summary>Only <paramref name="record"/> kept.</summary>
    public static KeptFromFile Of<T>(T record) where T : class => None.With(record);

    public bool IsEmpty => _records.IsEmpty;

    /// <summary>What the kept records say Nexaflow does not draw of their layer.</summary>
    public IEnumerable<string> Undrawn => _records.Values.OfType<IUndrawnKept>().SelectMany(r => r.Undrawn);

    /// <summary>The record the format that keeps <typeparamref name="T"/> kept; null where it kept none.</summary>
    public T? Get<T>() where T : class => _records.TryGetValue(typeof(T), out var record) ? (T)record : null;

    /// <summary>This with <paramref name="record"/> as what its format keeps — or, null, with nothing of that format kept.</summary>
    public KeptFromFile With<T>(T? record) where T : class
        => new(record is null ? _records.Remove(typeof(T)) : _records.SetItem(typeof(T), record));

    public bool Equals(KeptFromFile? other)
    {
        if (other is null || other._records.Count != _records.Count) return false;
        foreach (var (type, record) in _records)
            if (!other._records.TryGetValue(type, out var theirs) || !record.Equals(theirs)) return false;
        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as KeptFromFile);

    public override int GetHashCode() => _records.Count;

    public override string ToString() => IsEmpty ? "nothing kept" : string.Join(", ", _records.Values);
}

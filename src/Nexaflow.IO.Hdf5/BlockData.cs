using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Nexaflow.IO.Hdf5;

/// <summary>The decoded elements behind an <see cref="Hdf5Block"/>, one shape per family of types.</summary>
internal interface IBlockData
{
    string Format(long element, int field);
    void CopyTo(Span<byte> destination);
}

internal sealed class EmptyBlockData : IBlockData
{
    public static EmptyBlockData Instance { get; } = new();
    public string Format(long element, int field) => throw new ArgumentOutOfRangeException(nameof(element));
    public void CopyTo(Span<byte> destination) { }
}

/// <summary>Integers and floats of a size the runtime has a type for.</summary>
internal sealed class NumericBlockData<T>(T[] values) : IBlockData where T : unmanaged, INumberBase<T>
{
    public string Format(long element, int field) => values[element].ToString(null, CultureInfo.InvariantCulture);
    public void CopyTo(Span<byte> destination) => MemoryMarshal.AsBytes(values.AsSpan()).CopyTo(destination);
}

/// <summary>An enumeration: the stored integers, shown by member name.</summary>
internal sealed class EnumBlockData<T>(T[] values, Hdf5Type type) : IBlockData where T : unmanaged, IBinaryInteger<T>
{
    private readonly Dictionary<long, string> _names = type.Members.GroupBy(m => m.Value).ToDictionary(g => g.Key, g => g.First().Name);

    public string Format(long element, int field)
    {
        long v = long.CreateTruncating(values[element]);
        return _names.TryGetValue(v, out var name) ? name : v.ToString(CultureInfo.InvariantCulture);
    }

    public void CopyTo(Span<byte> destination) => MemoryMarshal.AsBytes(values.AsSpan()).CopyTo(destination);
}

/// <summary>Elements kept as their stored bytes: fixed strings, opaque, bitfields, arrays, odd-sized numbers.</summary>
internal sealed class RawBlockData(byte[] bytes, Hdf5Type type) : IBlockData
{
    public string Format(long element, int field) =>
        Hdf5ValueCodec.FormatRaw(bytes.AsSpan(checked((int)(element * type.Size)), type.Size), type);

    public void CopyTo(Span<byte> destination) => bytes.AsSpan(0, destination.Length).CopyTo(destination);
}

/// <summary>Variable-length strings.</summary>
internal sealed class TextBlockData(string?[] values) : IBlockData
{
    public string Format(long element, int field) => values[element] ?? string.Empty;
    public void CopyTo(Span<byte> destination) => throw new InvalidOperationException("Strings have no fixed-size layout.");
}

/// <summary>Variable-length sequences: each element is an array of the base type, possibly empty.</summary>
internal sealed class SequenceBlockData(Array?[] values, Hdf5Type type) : IBlockData
{
    public string Format(long element, int field) => Hdf5ValueCodec.Format(values[element] ?? Array.Empty<object>(), type);
    public void CopyTo(Span<byte> destination) => throw new InvalidOperationException("Sequences have no fixed-size layout.");
}

/// <summary>Compound records, one dictionary of member values per element.</summary>
internal sealed class CompoundBlockData(Dictionary<string, object>[] records, Hdf5Type type) : IBlockData
{
    public string Format(long element, int field)
    {
        var record = records[element];
        if (field < 0) return Hdf5ValueCodec.Format(record, type);
        var f = type.Fields[field];
        return Hdf5ValueCodec.Format(record.GetValueOrDefault(f.Name), f.Type);
    }

    public void CopyTo(Span<byte> destination)
    {
        for (int i = 0; i < records.Length; i++)
            Hdf5ValueCodec.Encode(records[i], type, destination.Slice(i * type.Size, type.Size));
    }
}

/// <summary>Object references, each shown by the name of the object it points at.</summary>
internal sealed class ReferenceBlockData(string[] targets) : IBlockData
{
    public string Format(long element, int field) => targets[element];
    public void CopyTo(Span<byte> destination) => throw new InvalidOperationException("References have no fixed-size layout.");
}

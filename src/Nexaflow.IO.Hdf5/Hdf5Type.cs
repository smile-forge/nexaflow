namespace Nexaflow.IO.Hdf5;

/// <summary>The HDF5 datatype classes, independent of whichever library read the file.</summary>
public enum Hdf5TypeClass { Integer, Float, Time, String, BitField, Opaque, Compound, Reference, Enum, VariableLength, Array }

/// <summary>One member of a compound type, at its byte offset inside the record.</summary>
public sealed record Hdf5Field(string Name, int Offset, Hdf5Type Type);

/// <summary>One named value of an enumerated type.</summary>
public sealed record Hdf5EnumMember(string Name, long Value);

/// <summary>
/// An HDF5 datatype as the viewer needs it: its class and element size, plus what that class carries —
/// signedness, compound fields, enum members, the base type of an enum / array / variable-length type, an
/// opaque tag. Byte order is not here: the reading library does not report it.
/// </summary>
public sealed record Hdf5Type(Hdf5TypeClass Class, int Size)
{
    public bool IsSigned { get; init; }
    public IReadOnlyList<Hdf5Field> Fields { get; init; } = [];
    public IReadOnlyList<Hdf5EnumMember> Members { get; init; } = [];
    public Hdf5Type? Base { get; init; }
    public string? Tag { get; init; }

    /// <summary>
    /// A variable-length sequence of single-byte characters. The file format flags strings apart from byte
    /// sequences, but the reading library does not report that flag, so a one-byte base is read as text — the
    /// case HDF5 tools meet in practice (h5py, NetCDF-4 and HDF-EOS all write strings this way).
    /// </summary>
    public bool IsVariableLengthString =>
        Class == Hdf5TypeClass.VariableLength && Base is { Class: Hdf5TypeClass.Integer or Hdf5TypeClass.String, Size: 1 };

    /// <summary>Every element occupies <see cref="Size"/> bytes with no indirection — what a flat binary copy can hold.</summary>
    public bool IsFixedSize => Class switch
    {
        Hdf5TypeClass.VariableLength or Hdf5TypeClass.Reference or Hdf5TypeClass.Time => false,
        Hdf5TypeClass.Compound => Fields.All(f => f.Type.IsFixedSize),
        Hdf5TypeClass.Enum or Hdf5TypeClass.Array => Base?.IsFixedSize ?? false,
        _ => true,
    };

    /// <summary>
    /// Language-neutral notation: <c>float64</c>, <c>uint16</c>, <c>string[8]</c>, <c>string</c> (variable
    /// length), <c>enum&lt;int8&gt;{OFF=0, ON=1}</c>, <c>{t: float64, ch: int16}</c>, <c>float32[3]</c>.
    /// </summary>
    public string DisplayName => Class switch
    {
        Hdf5TypeClass.Integer        => $"{(IsSigned ? "int" : "uint")}{Size * 8}",
        Hdf5TypeClass.Float          => $"float{Size * 8}",
        Hdf5TypeClass.String         => $"string[{Size}]",
        Hdf5TypeClass.BitField       => $"bitfield{Size * 8}",
        Hdf5TypeClass.Time           => $"time{Size * 8}",
        Hdf5TypeClass.Reference      => "reference",
        Hdf5TypeClass.Opaque         => string.IsNullOrEmpty(Tag) ? $"opaque[{Size}]" : $"opaque[{Size}] \"{Tag}\"",
        Hdf5TypeClass.Enum           => $"enum<{Base?.DisplayName}>{{{string.Join(", ", Members.Select(m => $"{m.Name}={m.Value}"))}}}",
        Hdf5TypeClass.Compound       => $"{{{string.Join(", ", Fields.Select(f => $"{f.Name}: {f.Type.DisplayName}"))}}}",
        Hdf5TypeClass.Array          => Base is { Size: > 0 } b ? $"{b.DisplayName}[{Size / b.Size}]" : $"array[{Size}]",
        Hdf5TypeClass.VariableLength => IsVariableLengthString ? "string" : $"vlen<{Base?.DisplayName}>",
        _                            => Class.ToString(),
    };
}

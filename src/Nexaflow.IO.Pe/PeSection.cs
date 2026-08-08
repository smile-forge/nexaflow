namespace Nexaflow.IO.Pe;

[Flags]
public enum PeSectionCharacteristics : uint
{
    None = 0,
    Code = 0x0000_0020, InitializedData = 0x0000_0040, UninitializedData = 0x0000_0080,
    Info = 0x0000_0200, Remove = 0x0000_0800, Comdat = 0x0000_1000,
    GlobalPointerRelative = 0x0000_8000,
    Discardable = 0x0200_0000, NotCached = 0x0400_0000, NotPaged = 0x0800_0000,
    Shared = 0x1000_0000, Execute = 0x2000_0000, Read = 0x4000_0000, Write = 0x8000_0000,
}

/// <summary>
/// One section-table entry, plus the two derived values an inspector actually shows: the section's
/// Shannon <see cref="Entropy"/> and its <see cref="Md5"/>. A section whose entropy is high *and*
/// which is executable is the classic packed/encrypted-code signature.
/// </summary>
public sealed record PeSection(
    string                   Name,
    uint                     VirtualSize,
    uint                     VirtualAddress,
    uint                     RawSize,
    uint                     RawPointer,
    uint                     PointerToRelocations,
    uint                     NumberOfRelocations,
    PeSectionCharacteristics Characteristics)
{
    /// <summary>Shannon entropy of the section's raw bytes, 0–8 bits per byte. Null when not computed.</summary>
    public double? Entropy { get; init; }

    /// <summary>Lower-case MD5 of the section's raw bytes. Null when not computed.</summary>
    public string? Md5 { get; init; }

    public bool IsCode       => Characteristics.HasFlag(PeSectionCharacteristics.Code);
    public bool IsExecutable => Characteristics.HasFlag(PeSectionCharacteristics.Execute);
    public bool IsReadable   => Characteristics.HasFlag(PeSectionCharacteristics.Read);
    public bool IsWritable   => Characteristics.HasFlag(PeSectionCharacteristics.Write);
    public bool IsDiscardable=> Characteristics.HasFlag(PeSectionCharacteristics.Discardable);

    /// <summary>Writable *and* executable — legitimate code almost never needs both.</summary>
    public bool IsWritableExecutable => IsWritable && IsExecutable;

    /// <summary>Compact "RWX" style permission string for display.</summary>
    public string Permissions =>
        $"{(IsReadable ? 'R' : '-')}{(IsWritable ? 'W' : '-')}{(IsExecutable ? 'X' : '-')}";

    /// <summary>True when <paramref name="rva"/> falls inside this section's virtual extent.
    /// Uses the larger of virtual and raw size, because a section may be padded on disk.</summary>
    public bool ContainsRva(uint rva)
    {
        uint size = Math.Max(VirtualSize, RawSize);
        return size != 0 && rva >= VirtualAddress && rva < VirtualAddress + size;
    }

    public override string ToString() => $"{Name} @0x{VirtualAddress:X8} ({Permissions})";
}

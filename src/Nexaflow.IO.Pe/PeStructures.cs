namespace Nexaflow.IO.Pe;

// ── Version info (RT_VERSION) ─────────────────────────────────────────────────

/// <summary>
/// The VS_VERSIONINFO resource — the version, company and description Explorer shows on the
/// Details tab. <see cref="Strings"/> holds the per-language StringFileInfo block, which is where
/// everything a human reads actually lives; the fixed part carries the machine-readable versions.
/// </summary>
public sealed record PeVersionInfo(
    string                        FileVersion,
    string                        ProductVersion,
    uint                          FileFlags,
    uint                          FileOs,
    uint                          FileType,
    uint                          FileSubtype,
    IReadOnlyList<PeVersionStrings> Strings)
{
    public static readonly PeVersionInfo Empty = new("", "", 0, 0, 0, 0, []);

    public bool IsEmpty => FileVersion.Length == 0 && Strings.Count == 0;

    /// <summary>A named value from the first string table that has one — the common case, since
    /// almost every binary ships exactly one language block.</summary>
    public string? Value(string name)
        => Strings.Select(t => t.Values.GetValueOrDefault(name)).FirstOrDefault(v => v is { Length: > 0 });

    public string? CompanyName      => Value("CompanyName");
    public string? FileDescription  => Value("FileDescription");
    public string? OriginalFilename => Value("OriginalFilename");
    public string? InternalName     => Value("InternalName");
    public string? LegalCopyright   => Value("LegalCopyright");
    public string? ProductName      => Value("ProductName");

    /// <summary>Set when the build was marked a debug build (VS_FF_DEBUG).</summary>
    public bool IsDebugBuild => (FileFlags & 0x01) != 0;

    /// <summary>Set when the build was marked pre-release (VS_FF_PRERELEASE).</summary>
    public bool IsPrerelease => (FileFlags & 0x02) != 0;
}

/// <summary>One StringFileInfo language block.</summary>
/// <param name="LanguageId">The Windows LANGID, e.g. 0x0409 for en-US.</param>
/// <param name="CodePage">The code page the strings were authored in.</param>
public sealed record PeVersionStrings(
    ushort LanguageId, ushort CodePage, IReadOnlyDictionary<string, string> Values)
{
    /// <summary>The block key as it appears in the resource, e.g. "040904B0".</summary>
    public string Key => $"{LanguageId:X4}{CodePage:X4}";
}

// ── Base relocations (directory 5) ────────────────────────────────────────────

public enum PeRelocationType
{
    Absolute = 0, High = 1, Low = 2, HighLow = 3, HighAdj = 4,
    MipsJmpAddr = 5, ArmMov32 = 5, RiscVHigh20 = 5,
    ThumbMov32 = 7, RiscVLow12I = 7,
    RiscVLow12S = 8, LoongArch32MarkLa = 8,
    MipsJmpAddr16 = 9, Dir64 = 10,
}

/// <param name="Type">The fixup width/kind. <see cref="PeRelocationType.Absolute"/> is padding, not a fixup.</param>
/// <param name="Offset">Offset within the block's 4 KB page.</param>
public sealed record PeRelocationEntry(PeRelocationType Type, ushort Offset)
{
    /// <summary>Padding used to keep a block 4-byte aligned; it fixes nothing.</summary>
    public bool IsPadding => Type == PeRelocationType.Absolute;
}

/// <summary>One 4 KB page of fixups.</summary>
public sealed record PeRelocationBlock(uint PageRva, uint BlockSize, IReadOnlyList<PeRelocationEntry> Entries)
{
    public int FixupCount => Entries.Count(e => !e.IsPadding);
}

public sealed record PeRelocations(IReadOnlyList<PeRelocationBlock> Blocks)
{
    public static readonly PeRelocations Empty = new([]);

    public bool IsEmpty      => Blocks.Count == 0;
    public int  TotalFixups  => Blocks.Sum(b => b.FixupCount);
    public long TotalBytes   => Blocks.Sum(b => (long)b.BlockSize);

    /// <summary>How many fixups of each kind, for the summary breakdown.</summary>
    public IReadOnlyDictionary<PeRelocationType, int> CountsByType
        => Blocks.SelectMany(b => b.Entries)
                 .Where(e => !e.IsPadding)
                 .GroupBy(e => e.Type)
                 .ToDictionary(g => g.Key, g => g.Count());
}

// ── TLS callbacks (directory 9) ───────────────────────────────────────────────

/// <summary>
/// A thread-local-storage callback. These run <em>before</em> the entry point on every thread
/// attach, which is exactly why packers and anti-debug code use them — so a callback on a binary
/// with no other reason to have one is worth surfacing.
/// </summary>
public sealed record PeTlsCallback(ulong VirtualAddress, uint Rva, long? FileOffset);

public sealed record PeTls(
    ulong                        StartAddressOfRawData,
    ulong                        EndAddressOfRawData,
    ulong                        AddressOfIndex,
    ulong                        AddressOfCallBacks,
    uint                         SizeOfZeroFill,
    uint                         Characteristics,
    IReadOnlyList<PeTlsCallback> Callbacks)
{
    public static readonly PeTls Empty = new(0, 0, 0, 0, 0, 0, []);

    public bool IsPresent   => AddressOfCallBacks != 0 || StartAddressOfRawData != 0;
    public bool HasCallbacks => Callbacks.Count > 0;
}

// ── Debug directory (directory 6) ─────────────────────────────────────────────

public enum PeDebugType
{
    Unknown = 0, Coff = 1, CodeView = 2, Fpo = 3, Misc = 4, Exception = 5, Fixup = 6,
    OmapToSrc = 7, OmapFromSrc = 8, Borland = 9, Reserved10 = 10, Clsid = 11,
    VcFeature = 12, Pogo = 13, Iltcg = 14, Mpx = 15,
    /// <summary>The build is reproducible: the COFF TimeDateStamp is a content hash, not a time.</summary>
    Reproducible = 16,
    EmbeddedPortablePdb = 17, PdbChecksum = 19, ExDllCharacteristics = 20,
}

public sealed record PeDebugEntry(
    PeDebugType Type, uint TimeDateStamp, ushort MajorVersion, ushort MinorVersion,
    uint SizeOfData, uint AddressOfRawData, uint PointerToRawData);

/// <summary>
/// The debug directory. The interesting part for an inspector is the CodeView record: it names the
/// PDB the binary was built against, and that path is frequently an absolute one from the build
/// machine — a real information leak worth showing plainly.
/// </summary>
public sealed record PeDebug(IReadOnlyList<PeDebugEntry> Entries)
{
    public static readonly PeDebug Empty = new([]);

    public bool IsEmpty => Entries.Count == 0;

    /// <summary>The PDB path from the CodeView record, verbatim.</summary>
    public string? PdbPath { get; init; }

    /// <summary>The PDB signature GUID; matched against the PDB to confirm it belongs to this build.</summary>
    public Guid? PdbGuid { get; init; }

    public uint? PdbAge { get; init; }

    /// <summary>A reproducible build — so <see cref="PeCoffHeader.TimeDateStamp"/> is a content hash
    /// rather than a build time and must not be shown as a date.</summary>
    public bool IsDeterministic => Entries.Any(e => e.Type == PeDebugType.Reproducible);

    public bool HasEmbeddedPdb => Entries.Any(e => e.Type == PeDebugType.EmbeddedPortablePdb);

    /// <summary>True when the PDB path looks like a build-machine absolute path rather than a bare
    /// file name — i.e. the binary leaks where and by whom it was built.</summary>
    public bool LeaksBuildPath
        => PdbPath is { Length: > 0 } p && (p.Contains(":\\") || p.StartsWith('/'));
}

// ── CLR header (directory 14) ─────────────────────────────────────────────────

[Flags]
public enum PeClrFlags : uint
{
    None = 0, IlOnly = 0x0001, Requires32Bit = 0x0002, IlLibrary = 0x0004,
    StrongNameSigned = 0x0008, NativeEntryPoint = 0x0010, TrackDebugData = 0x0001_0000,
    Prefers32Bit = 0x0002_0000,
}

/// <summary>A referenced assembly from the AssemblyRef table.</summary>
public sealed record PeAssemblyReference(string Name, string Version, string? Culture, string? PublicKeyToken)
{
    public override string ToString() => $"{Name}, Version={Version}";
}

/// <summary>
/// The managed half of the image. Everything below <see cref="IsManaged"/> comes from the metadata
/// tables via <c>System.Reflection.Metadata</c> and is best-effort: a native image, or one whose
/// metadata root is corrupt, still reports the COR20 header on its own.
/// </summary>
public sealed record PeClr
{
    public static readonly PeClr NotManaged = new();

    public bool       IsManaged        { get; init; }
    public string?    RuntimeVersion   { get; init; }
    public PeClrFlags Flags            { get; init; }
    public uint       EntryPointToken  { get; init; }
    public uint       MetadataRva      { get; init; }
    public uint       MetadataSize     { get; init; }

    /// <summary>The metadata version string, e.g. "v4.0.30319".</summary>
    public string? MetadataVersion { get; init; }

    public string?  AssemblyName    { get; init; }
    public string?  AssemblyVersion { get; init; }
    public string?  AssemblyCulture { get; init; }
    public string?  PublicKeyToken  { get; init; }

    /// <summary>The <c>TargetFrameworkAttribute</c> value, e.g. ".NETCoreApp,Version=v10.0".</summary>
    public string? TargetFramework { get; init; }

    public IReadOnlyList<PeAssemblyReference> AssemblyReferences { get; init; } = [];

    /// <summary>A Windows Runtime metadata file (<c>.winmd</c>) rather than a normal assembly.</summary>
    public bool IsWindowsRuntime { get; init; }

    public bool IsIlOnly           => Flags.HasFlag(PeClrFlags.IlOnly);
    public bool IsStrongNameSigned => Flags.HasFlag(PeClrFlags.StrongNameSigned);

    /// <summary>How the assembly will actually be loaded, resolving the 32-bit flag pair.</summary>
    public string Bitness => Flags.HasFlag(PeClrFlags.Requires32Bit)
        ? Flags.HasFlag(PeClrFlags.Prefers32Bit) ? "32-bit preferred" : "32-bit required"
        : "AnyCPU";
}

// ── Embedded strings ──────────────────────────────────────────────────────────

public enum PeStringEncoding { Ascii, Utf16 }

/// <param name="Offset">File offset the run starts at — right-clickable straight into the hex view.</param>
public sealed record PeString(long Offset, PeStringEncoding Encoding, string Value)
{
    /// <summary>The section the run falls in, filled in by the caller when it wants one.</summary>
    public string? Section { get; init; }
}

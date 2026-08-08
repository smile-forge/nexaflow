namespace Nexaflow.IO.Pe;

/// <summary>
/// A parsed Portable Executable. Produced by <see cref="PeReader"/>, which never throws — check
/// <see cref="IsPe"/> and <see cref="Diagnostics"/> rather than catching.
/// <para>
/// The image keeps its <see cref="PeBuffer"/> mapped, so <see cref="ReadRva"/> and
/// <see cref="ReadResource"/> stay cheap after the parse and a viewer can pull resource bytes on
/// demand instead of holding them all. <b>Dispose it when the page closes.</b>
/// </para>
/// </summary>
public sealed class PeImage : IDisposable
{
    private readonly PeBuffer          _buffer;
    private readonly List<PeDiagnostic> _diagnostics;
    private          bool               _disposed;

    internal PeImage(string? path, PeBuffer buffer, List<PeDiagnostic> diagnostics)
    {
        Path         = path;
        _buffer      = buffer;
        _diagnostics = diagnostics;
    }

    /// <summary>The file this was read from, or null for stream/memory input.</summary>
    public string? Path { get; }

    public long Length => _buffer.Length;

    /// <summary>False when the DOS or NT signature was missing — everything else will be null.</summary>
    public bool IsPe { get; internal set; }

    public PeDosHeader?      DosHeader      { get; internal set; }
    public PeCoffHeader?     CoffHeader     { get; internal set; }
    public PeOptionalHeader? OptionalHeader { get; internal set; }

    /// <summary>The data directories actually present in the optional header (may be fewer than 16).</summary>
    public IReadOnlyList<PeDataDirectory> DataDirectories { get; internal set; } = [];

    public IReadOnlyList<PeSection>       Sections     { get; internal set; } = [];
    public IReadOnlyList<PeImportModule>  Imports      { get; internal set; } = [];
    public IReadOnlyList<PeImportModule>  DelayImports { get; internal set; } = [];
    public IReadOnlyList<PeImportModule>  BoundImports { get; internal set; } = [];
    public PeExports                      Exports      { get; internal set; } = PeExports.Empty;
    public PeResources                    Resources    { get; internal set; } = PeResources.Empty;
    public PeEntropy                      Entropy      { get; internal set; } = PeEntropy.Empty;
    public PeSecurity                     Security     { get; internal set; } = PeSecurity.NotChecked;

    public PeVersionInfo Version     { get; internal set; } = PeVersionInfo.Empty;
    public PeRelocations Relocations { get; internal set; } = PeRelocations.Empty;
    public PeTls         Tls         { get; internal set; } = PeTls.Empty;
    public PeDebug       Debug       { get; internal set; } = PeDebug.Empty;
    public PeClr         Clr         { get; internal set; } = PeClr.NotManaged;

    /// <summary>The application manifest, embedded or from a sidecar. Empty when there is none.</summary>
    public AppManifest Manifest { get; internal set; } = AppManifest.Empty;

    /// <summary>The standard import hash — lower-case <c>module.function</c> pairs joined by commas, MD5'd.
    /// Null when the image has no imports.</summary>
    public string? ImpHash { get; internal set; }

    public string? Sha256 { get; internal set; }
    public string? Md5    { get; internal set; }

    public IReadOnlyList<PeDiagnostic> Diagnostics => _diagnostics;

    /// <summary>
    /// The link time, or null when there isn't one. A reproducible build stores a content hash in
    /// the COFF TimeDateStamp rather than a time, so that value must not be rendered as a date —
    /// the debug directory's Reproducible entry is what distinguishes the two.
    /// </summary>
    public DateTimeOffset? BuildTimestamp
        => Debug.IsDeterministic ? null : CoffHeader?.Timestamp;

    public bool Is64Bit  => OptionalHeader?.Is64Bit ?? false;
    public bool IsDll    => CoffHeader?.Characteristics.HasFlag(PeFileCharacteristics.Dll) ?? false;
    public bool IsSystem => CoffHeader?.Characteristics.HasFlag(PeFileCharacteristics.System) ?? false;

    /// <summary>A driver: the native subsystem, which is what distinguishes a <c>.sys</c> from a DLL.</summary>
    public bool IsDriver => OptionalHeader?.Subsystem is PeSubsystem.Native or PeSubsystem.NativeWindows;

    public PeMachine Machine => CoffHeader?.Machine ?? PeMachine.Unknown;

    /// <summary>Every import module regardless of how it was declared.</summary>
    public IEnumerable<PeImportModule> AllImports => Imports.Concat(DelayImports).Concat(BoundImports);

    internal PeBuffer Buffer => _buffer;

    /// <summary>
    /// Ceiling on recorded diagnostics. A deliberately corrupted image can fail thousands of times;
    /// past the first few hundred the list stops informing anyone and just becomes something the UI
    /// has to render, so it is capped with one final note saying so.
    /// </summary>
    private const int MaxDiagnostics = 250;

    internal void Add(PeDiagnostic diagnostic)
    {
        if (_diagnostics.Count > MaxDiagnostics) return;

        if (_diagnostics.Count == MaxDiagnostics)
        {
            _diagnostics.Add(new PeDiagnostic(PeSeverity.Warning, "Diagnostics",
                $"More than {MaxDiagnostics} problems were found; further ones were not recorded. " +
                "This image is badly malformed."));
            return;
        }
        _diagnostics.Add(diagnostic);
    }

    // ── Address translation ───────────────────────────────────────────────────

    /// <summary>The directory entry for <paramref name="kind"/>, or null when the optional header
    /// declared fewer entries than that (which is legal and common for small images).</summary>
    public PeDataDirectory? Directory(PeDirectory kind)
        => DataDirectories.FirstOrDefault(d => d.Kind == kind);

    public PeSection? SectionContaining(uint rva)
        => Sections.FirstOrDefault(s => s.ContainsRva(rva));

    /// <summary>
    /// Maps a relative virtual address to a file offset — the primitive behind every "view this in
    /// hex" jump. Returns null when the RVA falls in no section and outside the header block, or
    /// when the section is uninitialised (present in memory, absent on disk).
    /// </summary>
    public long? RvaToFileOffset(uint rva)
    {
        if (SectionContaining(rva) is { } section)
        {
            if (section.RawSize == 0) return null;                 // .bss and friends: no bytes on disk
            uint delta = rva - section.VirtualAddress;
            if (delta >= section.RawSize) return null;             // inside virtual padding only
            long offset = section.RawPointer + delta;
            return _buffer.InRange(offset, 1) ? offset : null;
        }

        // Before the first section the RVA is the file offset — that is where the headers live.
        if (OptionalHeader is { } oh && rva < oh.SizeOfHeaders && _buffer.InRange(rva, 1)) return rva;
        return null;
    }

    /// <summary>Absolute virtual address → file offset, for tables (TLS callbacks, load config) that
    /// store VAs rather than RVAs.</summary>
    public long? VaToFileOffset(ulong va)
    {
        ulong imageBase = OptionalHeader?.ImageBase ?? 0;
        if (va < imageBase) return null;
        ulong rva = va - imageBase;
        return rva > uint.MaxValue ? null : RvaToFileOffset((uint)rva);
    }

    /// <summary>Bytes at an RVA, or empty when it does not resolve.</summary>
    public byte[] ReadRva(uint rva, int count)
        => RvaToFileOffset(rva) is { } offset ? _buffer.ToArray(offset, count) : [];

    /// <summary>The bytes of a resource leaf, or empty for an interior node or an unresolvable RVA.</summary>
    public byte[] ReadResource(PeResourceNode node)
    {
        if (!node.IsLeaf) return [];
        long? offset = node.DataOffset ?? RvaToFileOffset(node.DataRva);
        return offset is { } o ? _buffer.ToArray(o, node.DataSize) : [];
    }

    /// <summary>Raw bytes anywhere in the file — used by the hex jump and the strings scanner.</summary>
    public byte[] ReadAt(long offset, long count) => _buffer.ToArray(offset, count);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _buffer.Dispose();
    }
}

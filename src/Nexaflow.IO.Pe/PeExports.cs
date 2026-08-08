namespace Nexaflow.IO.Pe;

/// <summary>
/// One exported symbol. An export is either a real address (<see cref="Rva"/>) or a
/// <see cref="ForwarderTo"/> string like <c>NTDLL.RtlAllocateHeap</c> — the loader chases the
/// forwarder instead, which is how the API-set DLLs are built and why an export table can look
/// enormous while containing almost no code.
/// </summary>
public sealed record PeExportEntry(uint Ordinal, string? Name, uint Rva, string? ForwarderTo)
{
    public bool IsForwarder => ForwarderTo is not null;

    /// <summary>Exported by ordinal only — no name-table entry points at it.</summary>
    public bool IsByOrdinal => Name is null;

    public string Display => Name ?? $"#{Ordinal}";
}

public sealed record PeExports(
    string?                       DllName,
    uint                          OrdinalBase,
    uint                          TimeDateStamp,
    IReadOnlyList<PeExportEntry>  Entries)
{
    public static readonly PeExports Empty = new(null, 0, 0, []);

    /// <summary>
    /// The DLL exports the classic in-process COM server entry points, so it can be registered with
    /// <c>regsvr32</c>. Presence of <c>DllRegisterServer</c> is the definitive marker.
    /// </summary>
    public bool IsComSelfRegistering =>
        Has("DllRegisterServer") || (Has("DllGetClassObject") && Has("DllCanUnloadNow"));

    public bool Has(string exportName)
        => Entries.Any(e => string.Equals(e.Name, exportName, StringComparison.Ordinal));

    /// <summary>The COM entry points this DLL actually exports, for display.</summary>
    public IReadOnlyList<string> ComEntryPoints =>
    [
        .. new[] { "DllRegisterServer", "DllUnregisterServer", "DllGetClassObject",
                   "DllCanUnloadNow", "DllInstall" }.Where(Has)
    ];
}

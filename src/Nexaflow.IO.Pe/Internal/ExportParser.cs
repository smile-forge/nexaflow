namespace Nexaflow.IO.Pe.Internal;

/// <summary>
/// The export directory. Three parallel tables have to be cross-indexed: the address table is keyed
/// by ordinal, while names live in a separate table joined through an ordinal-index array — so an
/// export can have an address and no name (ordinal-only), and a slot can be empty.
/// </summary>
internal static class ExportParser
{
    public static PeExports Parse(PeImage image, PeReadOptions options)
    {
        if (image.Directory(PeDirectory.Export) is not { IsPresent: true } dir) return PeExports.Empty;
        if (image.RvaToFileOffset(dir.VirtualAddress) is not { } start)
        {
            image.Add(new PeDiagnostic(PeSeverity.Error, "Exports",
                $"The export directory RVA 0x{dir.VirtualAddress:X} does not map into any section."));
            return PeExports.Empty;
        }

        var buf = image.Buffer;
        if (!buf.InRange(start, 40))
        {
            image.Add(new PeDiagnostic(PeSeverity.Error, "Exports", "The export directory is truncated.", start));
            return PeExports.Empty;
        }

        buf.TryU32(start + 4,  out uint timestamp);
        buf.TryU32(start + 12, out uint nameRva);
        buf.TryU32(start + 16, out uint ordinalBase);
        buf.TryU32(start + 20, out uint functionCount);
        buf.TryU32(start + 24, out uint nameCount);
        buf.TryU32(start + 28, out uint addressTableRva);
        buf.TryU32(start + 32, out uint nameTableRva);
        buf.TryU32(start + 36, out uint ordinalTableRva);

        string? dllName = image.RvaToFileOffset(nameRva) is { } n ? buf.AsciiZ(n, 512) : null;

        if (functionCount > options.MaxTableEntries)
        {
            image.Add(new PeDiagnostic(PeSeverity.Warning, "Exports",
                $"NumberOfFunctions is {functionCount}; capped at {options.MaxTableEntries}.", start + 20));
            functionCount = (uint)options.MaxTableEntries;
        }
        if (nameCount > functionCount + options.MaxTableEntries) nameCount = 0;   // nonsense; ignore names

        // ordinal index → exported name, joined through AddressOfNameOrdinals.
        var namesByIndex = ReadNameMap(image, nameTableRva, ordinalTableRva, nameCount);

        long? addressTable = image.RvaToFileOffset(addressTableRva);
        if (addressTable is null)
        {
            image.Add(new PeDiagnostic(PeSeverity.Error, "Exports",
                $"The export address table RVA 0x{addressTableRva:X} does not map into any section."));
            return new PeExports(dllName, ordinalBase, timestamp, []);
        }

        uint dirStart = dir.VirtualAddress;
        uint dirEnd   = dir.VirtualAddress + dir.Size;

        var entries = new List<PeExportEntry>((int)Math.Min(functionCount, 4096));
        for (uint i = 0; i < functionCount; i++)
        {
            if (!buf.TryU32(addressTable.Value + i * 4L, out uint functionRva))
            {
                image.Add(new PeDiagnostic(PeSeverity.Warning, "Exports",
                    $"The export address table is truncated after {i} of {functionCount} entries."));
                break;
            }
            if (functionRva == 0) continue;   // an unused ordinal slot

            namesByIndex.TryGetValue(i, out string? name);

            // An address pointing back inside the export directory is not code — it is a
            // "TARGETDLL.Function" string the loader chases instead. This is how the API-set
            // forwarder DLLs are built.
            string? forwarder = null;
            if (functionRva >= dirStart && functionRva < dirEnd &&
                image.RvaToFileOffset(functionRva) is { } fwdOffset)
                forwarder = buf.AsciiZ(fwdOffset, 512);

            entries.Add(new PeExportEntry(ordinalBase + i, name, functionRva, forwarder));
        }

        return new PeExports(dllName, ordinalBase, timestamp, entries);
    }

    private static Dictionary<uint, string> ReadNameMap(
        PeImage image, uint nameTableRva, uint ordinalTableRva, uint nameCount)
    {
        var map = new Dictionary<uint, string>();
        if (nameCount == 0) return map;

        long? nameTable    = image.RvaToFileOffset(nameTableRva);
        long? ordinalTable = image.RvaToFileOffset(ordinalTableRva);
        if (nameTable is null || ordinalTable is null)
        {
            image.Add(new PeDiagnostic(PeSeverity.Warning, "Exports",
                "The export name or ordinal table does not map into any section; exports will show as ordinal-only."));
            return map;
        }

        var buf = image.Buffer;
        for (uint i = 0; i < nameCount; i++)
        {
            if (!buf.TryU32(nameTable.Value + i * 4L, out uint nameRva)) break;
            if (!buf.TryU16(ordinalTable.Value + i * 2L, out ushort index)) break;
            if (image.RvaToFileOffset(nameRva) is not { } nameOffset) continue;
            if (buf.AsciiZ(nameOffset, 512) is { Length: > 0 } name) map[index] = name;
        }
        return map;
    }
}

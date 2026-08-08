namespace Nexaflow.IO.Pe.Internal;

/// <summary>
/// The three small header-driven directories: base relocations, TLS callbacks and the debug
/// directory. Grouped because each is a short fixed-shape walk over a directory the loader reads.
/// </summary>
internal static class StructureParser
{
    // ── Base relocations (directory 5) ────────────────────────────────────────

    public static PeRelocations ParseRelocations(PeImage image, PeReadOptions options)
    {
        if (image.Directory(PeDirectory.BaseRelocation) is not { IsPresent: true } dir) return PeRelocations.Empty;
        if (image.RvaToFileOffset(dir.VirtualAddress) is not { } start)
        {
            image.Add(new PeDiagnostic(PeSeverity.Warning, "Relocations",
                $"The relocation directory RVA 0x{dir.VirtualAddress:X} does not map into any section."));
            return PeRelocations.Empty;
        }

        var  buf    = image.Buffer;
        var  blocks = new List<PeRelocationBlock>();
        long cursor = start;
        long end    = start + dir.Size;

        while (cursor < end && blocks.Count < options.MaxTableEntries)
        {
            if (!buf.InRange(cursor, 8)) break;

            buf.TryU32(cursor,     out uint pageRva);
            buf.TryU32(cursor + 4, out uint blockSize);

            // A block must at least contain its own header; anything less would not advance and
            // would spin here forever.
            if (blockSize < 8 || !buf.InRange(cursor, blockSize))
            {
                if (blockSize != 0)
                    image.Add(new PeDiagnostic(PeSeverity.Warning, "Relocations",
                        $"A relocation block declares an invalid size of {blockSize} bytes.", cursor));
                break;
            }

            int count   = (int)((blockSize - 8) / 2);
            var entries = new List<PeRelocationEntry>(count);
            for (int i = 0; i < count; i++)
            {
                if (!buf.TryU16(cursor + 8 + i * 2L, out ushort raw)) break;
                entries.Add(new PeRelocationEntry((PeRelocationType)(raw >> 12), (ushort)(raw & 0x0FFF)));
            }

            blocks.Add(new PeRelocationBlock(pageRva, blockSize, entries));
            cursor += blockSize;
        }

        return blocks.Count == 0 ? PeRelocations.Empty : new PeRelocations(blocks);
    }

    // ── TLS (directory 9) ─────────────────────────────────────────────────────

    public static PeTls ParseTls(PeImage image, PeReadOptions options)
    {
        if (image.Directory(PeDirectory.Tls) is not { IsPresent: true } dir) return PeTls.Empty;
        if (image.RvaToFileOffset(dir.VirtualAddress) is not { } start) return PeTls.Empty;

        var  buf  = image.Buffer;
        bool is64 = image.Is64Bit;
        int  size = is64 ? 40 : 24;

        if (!buf.InRange(start, size))
        {
            image.Add(new PeDiagnostic(PeSeverity.Warning, "Tls", "The TLS directory is truncated.", start));
            return PeTls.Empty;
        }

        ulong Addr(long offset)
        {
            if (is64) { buf.TryU64(offset, out ulong v); return v; }
            buf.TryU32(offset, out uint v32); return v32;
        }

        int  stride = is64 ? 8 : 4;
        ulong rawStart = Addr(start);
        ulong rawEnd   = Addr(start + stride);
        ulong index    = Addr(start + stride * 2);
        ulong callbacks = Addr(start + stride * 3);
        buf.TryU32(start + stride * 4,     out uint zeroFill);
        buf.TryU32(start + stride * 4 + 4, out uint characteristics);

        var list = ReadCallbacks(image, callbacks, is64, options);
        return new PeTls(rawStart, rawEnd, index, callbacks, zeroFill, characteristics, list);
    }

    /// <summary>
    /// Walks the NUL-terminated callback array. The array is addressed by absolute VA, not RVA —
    /// treating it as an RVA is the standard way to read garbage here.
    /// </summary>
    private static List<PeTlsCallback> ReadCallbacks(
        PeImage image, ulong arrayVa, bool is64, PeReadOptions options)
    {
        var result = new List<PeTlsCallback>();
        if (arrayVa == 0) return result;
        if (image.VaToFileOffset(arrayVa) is not { } start) return result;

        var   buf       = image.Buffer;
        int   stride    = is64 ? 8 : 4;
        ulong imageBase = image.OptionalHeader?.ImageBase ?? 0;

        for (int i = 0; i < options.MaxTableEntries; i++)
        {
            long slot = start + (long)i * stride;
            ulong va;
            if (is64) { if (!buf.TryU64(slot, out va)) break; }
            else      { if (!buf.TryU32(slot, out uint v32)) break; va = v32; }

            if (va == 0) break;   // terminator

            uint rva = va >= imageBase && va - imageBase <= uint.MaxValue ? (uint)(va - imageBase) : 0;
            result.Add(new PeTlsCallback(va, rva, image.VaToFileOffset(va)));
        }
        return result;
    }

    // ── Debug directory (directory 6) ─────────────────────────────────────────

    private const int DebugEntrySize = 28;
    private const uint CodeViewRsds  = 0x5344_5352;  // "RSDS"
    private const uint CodeViewNb10  = 0x3031_424E;  // "NB10"

    public static PeDebug ParseDebug(PeImage image, PeReadOptions options)
    {
        if (image.Directory(PeDirectory.Debug) is not { IsPresent: true } dir) return PeDebug.Empty;
        if (image.RvaToFileOffset(dir.VirtualAddress) is not { } start) return PeDebug.Empty;

        var buf     = image.Buffer;
        int count   = (int)Math.Min(dir.Size / DebugEntrySize, (uint)options.MaxTableEntries);
        var entries = new List<PeDebugEntry>(count);

        string? pdbPath = null;
        Guid?   pdbGuid = null;
        uint?   pdbAge  = null;

        for (int i = 0; i < count; i++)
        {
            long entry = start + i * (long)DebugEntrySize;
            if (!buf.InRange(entry, DebugEntrySize)) break;

            buf.TryU32(entry + 4,  out uint   timestamp);
            buf.TryU16(entry + 8,  out ushort major);
            buf.TryU16(entry + 10, out ushort minor);
            buf.TryU32(entry + 12, out uint   type);
            buf.TryU32(entry + 16, out uint   sizeOfData);
            buf.TryU32(entry + 20, out uint   addressOfRawData);
            buf.TryU32(entry + 24, out uint   pointerToRawData);

            entries.Add(new PeDebugEntry((PeDebugType)type, timestamp, major, minor,
                                         sizeOfData, addressOfRawData, pointerToRawData));

            if ((PeDebugType)type == PeDebugType.CodeView && pdbPath is null)
                (pdbPath, pdbGuid, pdbAge) = ReadCodeView(image, pointerToRawData, sizeOfData);
        }

        return new PeDebug(entries) { PdbPath = pdbPath, PdbGuid = pdbGuid, PdbAge = pdbAge };
    }

    /// <summary>
    /// The CodeView record names the PDB. RSDS (a GUID plus an age) is what every modern toolchain
    /// emits; NB10 is the pre-2002 form and is still occasionally seen in old drivers.
    /// </summary>
    private static (string?, Guid?, uint?) ReadCodeView(PeImage image, uint offset, uint size)
    {
        var buf = image.Buffer;
        if (size < 8 || !buf.InRange(offset, Math.Min(size, 24))) return (null, null, null);

        buf.TryU32(offset, out uint signature);

        if (signature == CodeViewRsds)
        {
            if (!buf.InRange(offset, 24)) return (null, null, null);
            var guidBytes = buf.Slice(offset + 4, 16);
            var guid      = new Guid(guidBytes);
            buf.TryU32(offset + 20, out uint age);
            string? path = buf.AsciiZ(offset + 24, (int)Math.Min(size, 1024));
            return (string.IsNullOrEmpty(path) ? null : path, guid, age);
        }

        if (signature == CodeViewNb10)
        {
            if (!buf.InRange(offset, 16)) return (null, null, null);
            buf.TryU32(offset + 12, out uint age);
            string? path = buf.AsciiZ(offset + 16, (int)Math.Min(size, 1024));
            return (string.IsNullOrEmpty(path) ? null : path, null, age);
        }

        return (null, null, null);
    }
}

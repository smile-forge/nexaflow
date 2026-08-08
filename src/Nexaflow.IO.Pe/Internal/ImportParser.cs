using System.Security.Cryptography;
using System.Text;

namespace Nexaflow.IO.Pe.Internal;

/// <summary>
/// The three import directories. All of them are null-terminated descriptor arrays, and all of them
/// are routinely mangled by packers, so every walk is bounded by
/// <see cref="PeReadOptions.MaxTableEntries"/> as well as by the terminator.
/// </summary>
internal static class ImportParser
{
    private const int DescriptorSize      = 20;
    private const int DelayDescriptorSize = 32;

    public static void Parse(PeImage image, PeReadOptions options)
    {
        image.Imports      = ParseStandard(image, options);
        image.DelayImports = ParseDelayLoad(image, options);
        image.BoundImports = ParseBound(image, options);
        image.ImpHash      = ComputeImpHash(image.Imports);
    }

    // ── Standard imports ──────────────────────────────────────────────────────

    private static List<PeImportModule> ParseStandard(PeImage image, PeReadOptions options)
    {
        var result = new List<PeImportModule>();
        if (image.Directory(PeDirectory.Import) is not { IsPresent: true } dir) return result;
        if (image.RvaToFileOffset(dir.VirtualAddress) is not { } start)
        {
            image.Add(new PeDiagnostic(PeSeverity.Error, "Imports",
                $"The import directory RVA 0x{dir.VirtualAddress:X} does not map into any section."));
            return result;
        }

        var buf = image.Buffer;
        for (int i = 0; i < options.MaxTableEntries; i++)
        {
            long entry = start + (long)i * DescriptorSize;
            if (!buf.InRange(entry, DescriptorSize))
            {
                image.Add(new PeDiagnostic(PeSeverity.Warning, "Imports",
                    "The import descriptor table is not terminated before the end of the file.", entry));
                break;
            }

            buf.TryU32(entry,      out uint originalFirstThunk);
            buf.TryU32(entry + 4,  out uint timestamp);
            buf.TryU32(entry + 12, out uint nameRva);
            buf.TryU32(entry + 16, out uint firstThunk);

            if (originalFirstThunk == 0 && nameRva == 0 && firstThunk == 0) break;  // terminator

            string name = ReadName(image, nameRva, "Imports") ?? $"(unnamed #{i})";

            // The ILT is the authority on names; a bound image overwrites the IAT with addresses,
            // so fall back to the IAT only when there is no ILT at all.
            uint thunkRva  = originalFirstThunk != 0 ? originalFirstThunk : firstThunk;
            var  functions = ReadThunks(image, thunkRva, firstThunk, options, name);

            result.Add(new PeImportModule(name, PeImportKind.Standard,
                                          originalFirstThunk, firstThunk, timestamp, functions));
        }
        return result;
    }

    // ── Delay-load imports ────────────────────────────────────────────────────

    private static List<PeImportModule> ParseDelayLoad(PeImage image, PeReadOptions options)
    {
        var result = new List<PeImportModule>();
        if (image.Directory(PeDirectory.DelayImport) is not { IsPresent: true } dir) return result;
        if (image.RvaToFileOffset(dir.VirtualAddress) is not { } start) return result;

        var   buf       = image.Buffer;
        ulong imageBase = image.OptionalHeader?.ImageBase ?? 0;

        for (int i = 0; i < options.MaxTableEntries; i++)
        {
            long entry = start + (long)i * DelayDescriptorSize;
            if (!buf.InRange(entry, DelayDescriptorSize)) break;

            buf.TryU32(entry,      out uint attributes);
            buf.TryU32(entry + 4,  out uint nameField);
            buf.TryU32(entry + 12, out uint iatField);
            buf.TryU32(entry + 16, out uint intField);
            buf.TryU32(entry + 28, out uint timestamp);

            if (nameField == 0 && iatField == 0 && intField == 0) break;

            // Attributes bit 0 set means the fields are RVAs. Pre-VS2005 linkers wrote absolute VAs.
            bool rvaBased = (attributes & 1) != 0;
            uint nameRva  = rvaBased ? nameField : ToRva(nameField, imageBase);
            uint intRva   = rvaBased ? intField  : ToRva(intField,  imageBase);
            uint iatRva   = rvaBased ? iatField  : ToRva(iatField,  imageBase);

            string name      = ReadName(image, nameRva, "DelayImports") ?? $"(unnamed #{i})";
            var    functions = ReadThunks(image, intRva != 0 ? intRva : iatRva, iatRva, options, name);

            result.Add(new PeImportModule(name, PeImportKind.DelayLoad, intRva, iatRva, timestamp, functions));
        }
        return result;
    }

    private static uint ToRva(uint va, ulong imageBase)
        => va == 0 || va < imageBase ? va : (uint)(va - imageBase);

    // ── Bound imports ─────────────────────────────────────────────────────────

    /// <summary>
    /// Bound imports record a previous bind's timestamps, not functions — the useful signal is
    /// which modules were bound and when, so each descriptor becomes a module with no function list.
    /// Module names are byte offsets from the start of the directory, not RVAs.
    /// </summary>
    private static List<PeImportModule> ParseBound(PeImage image, PeReadOptions options)
    {
        var result = new List<PeImportModule>();
        if (image.Directory(PeDirectory.BoundImport) is not { IsPresent: true } dir) return result;
        if (image.RvaToFileOffset(dir.VirtualAddress) is not { } start) return result;

        var buf    = image.Buffer;
        long cursor = start;

        for (int i = 0; i < options.MaxTableEntries; i++)
        {
            if (!buf.InRange(cursor, 8)) break;

            buf.TryU32(cursor,     out uint   timestamp);
            buf.TryU16(cursor + 4, out ushort nameOffset);
            buf.TryU16(cursor + 6, out ushort forwarderCount);

            if (timestamp == 0 && nameOffset == 0 && forwarderCount == 0) break;

            string name = buf.AsciiZ(start + nameOffset) ?? $"(unnamed #{i})";
            result.Add(new PeImportModule(name, PeImportKind.Bound, 0, 0, timestamp, []));

            // Skip this descriptor plus its forwarder refs, which share the same 8-byte shape.
            cursor += 8L + forwarderCount * 8L;
        }
        return result;
    }

    // ── Shared ────────────────────────────────────────────────────────────────

    private static string? ReadName(PeImage image, uint rva, string area)
    {
        if (rva == 0) return null;
        if (image.RvaToFileOffset(rva) is not { } offset)
        {
            image.Add(new PeDiagnostic(PeSeverity.Warning, area,
                $"A module-name RVA (0x{rva:X}) does not map into any section."));
            return null;
        }
        return image.Buffer.AsciiZ(offset, 512);
    }

    /// <summary>
    /// Walks a thunk array. <paramref name="nameThunkRva"/> supplies the names, while
    /// <paramref name="iatRva"/> is reported as each function's IAT slot so a caller can point at the
    /// live pointer. Entries with the high bit set are ordinal imports and carry no name at all.
    /// </summary>
    private static List<PeImportFunction> ReadThunks(
        PeImage image, uint nameThunkRva, uint iatRva, PeReadOptions options, string moduleName)
    {
        var functions = new List<PeImportFunction>();
        if (nameThunkRva == 0) return functions;
        if (image.RvaToFileOffset(nameThunkRva) is not { } start) return functions;

        var   buf         = image.Buffer;
        bool  is64        = image.Is64Bit;
        int   stride      = is64 ? 8 : 4;
        ulong ordinalFlag = is64 ? 0x8000_0000_0000_0000UL : 0x8000_0000UL;

        for (int i = 0; i < options.MaxTableEntries; i++)
        {
            long slot = start + (long)i * stride;
            ulong value;
            if (is64)
            {
                if (!buf.TryU64(slot, out value)) break;
            }
            else
            {
                if (!buf.TryU32(slot, out uint v32)) break;
                value = v32;
            }

            if (value == 0) break;   // terminator

            uint slotRva = iatRva == 0 ? 0 : (uint)(iatRva + i * stride);

            if ((value & ordinalFlag) != 0)
            {
                functions.Add(new PeImportFunction(null, (ushort)(value & 0xFFFF), 0, slotRva, value));
                continue;
            }

            uint hintNameRva = (uint)(value & 0x7FFF_FFFF);
            if (image.RvaToFileOffset(hintNameRva) is not { } hintOffset)
            {
                // A bound image leaves resolved addresses here; that is expected, not corruption.
                functions.Add(new PeImportFunction(null, null, 0, slotRva, value));
                continue;
            }

            buf.TryU16(hintOffset, out ushort hint);
            string? name = buf.AsciiZ(hintOffset + 2, 512);
            functions.Add(new PeImportFunction(string.IsNullOrEmpty(name) ? null : name, null, hint, slotRva, value));
        }

        if (functions.Count == options.MaxTableEntries)
            image.Add(new PeDiagnostic(PeSeverity.Warning, "Imports",
                $"'{moduleName}' has an unterminated thunk array; stopped at {options.MaxTableEntries} entries."));

        return functions;
    }

    // ── Import hash ───────────────────────────────────────────────────────────

    /// <summary>
    /// The standard imphash: lower-cased <c>module.function</c> pairs in table order, comma-joined,
    /// MD5'd. Only the standard import directory contributes, matching the reference implementation.
    /// <para>
    /// One documented deviation: ordinal-only imports are emitted as <c>ord123</c> rather than being
    /// mapped back to a name through the per-DLL ordinal tables that pefile carries for
    /// ws2_32/wsock32/oleaut32. For binaries that import from those three by ordinal the value will
    /// differ from VirusTotal's; for everything else it matches.
    /// </para>
    /// </summary>
    private static string? ComputeImpHash(IReadOnlyList<PeImportModule> imports)
    {
        if (imports.Count == 0) return null;

        var parts = new List<string>();
        foreach (var module in imports)
        {
            string lib = module.Name.ToLowerInvariant();
            int dot = lib.LastIndexOf('.');
            if (dot > 0 && lib[dot..] is ".dll" or ".ocx" or ".sys") lib = lib[..dot];

            foreach (var fn in module.Functions)
            {
                string? name = fn.Name is { Length: > 0 } n ? n.ToLowerInvariant()
                             : fn.Ordinal is { } o          ? $"ord{o}"
                             : null;
                if (name is null) continue;   // a bound IAT slot: neither name nor ordinal
                parts.Add($"{lib}.{name}");
            }
        }

        if (parts.Count == 0) return null;
        return Convert.ToHexStringLower(MD5.HashData(Encoding.ASCII.GetBytes(string.Join(',', parts))));
    }
}

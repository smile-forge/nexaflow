using System.Text;

namespace Nexaflow.IO.Pe.Internal;

/// <summary>
/// DOS stub → NT signature → COFF header → optional header → section table. Each step records a
/// diagnostic and stops rather than throwing, so a file that is merely <em>shaped</em> like a PE
/// still yields whatever prefix parsed.
/// </summary>
internal static class HeaderParser
{
    private const ushort DosSignature = 0x5A4D;      // "MZ"
    private const uint   NtSignature  = 0x0000_4550; // "PE\0\0"

    /// <summary>The stock linker stub text; anything else is worth flagging.</summary>
    private static ReadOnlySpan<byte> StockStubText => "This program cannot be run in DOS mode"u8;

    public static bool Parse(PeBuffer buf, PeImage image, PeReadOptions options)
    {
        if (!TryDos(buf, image, out uint ntOffset)) return false;
        if (!TryNtSignature(buf, image, ntOffset)) return false;

        long coffOffset = ntOffset + 4L;
        if (!TryCoff(buf, image, coffOffset)) return false;

        long optionalOffset = coffOffset + 20;
        TryOptional(buf, image, optionalOffset);

        long sectionOffset = optionalOffset + (image.CoffHeader?.SizeOfOptionalHeader ?? 0);
        TrySections(buf, image, sectionOffset, options);

        image.IsPe = true;
        return true;
    }

    // ── DOS ───────────────────────────────────────────────────────────────────

    private static bool TryDos(PeBuffer buf, PeImage image, out uint ntOffset)
    {
        ntOffset = 0;

        if (!buf.TryU16(0, out ushort magic))
        {
            image.Add(new PeDiagnostic(PeSeverity.Error, "DosHeader", "File is too small to contain a DOS header."));
            return false;
        }
        if (magic != DosSignature)
        {
            image.Add(new PeDiagnostic(PeSeverity.Error, "DosHeader",
                $"Not a PE image: expected the MZ signature, found 0x{magic:X4}.", 0));
            return false;
        }
        if (!buf.TryU32(0x3C, out ntOffset))
        {
            image.Add(new PeDiagnostic(PeSeverity.Error, "DosHeader",
                "e_lfanew is past the end of the file.", 0x3C));
            return false;
        }

        // The stub is everything between the fixed header and the NT header.
        int stubLength = ntOffset > 0x40 ? (int)Math.Min(ntOffset - 0x40, 4096) : 0;
        var stub       = stubLength > 0 ? buf.ToArray(0x40, stubLength) : [];
        bool custom    = stub.Length > 0 && stub.AsSpan().IndexOf(StockStubText) < 0;

        image.DosHeader = new PeDosHeader(magic, ntOffset, stub) { HasCustomStub = custom };
        return true;
    }

    private static bool TryNtSignature(PeBuffer buf, PeImage image, uint ntOffset)
    {
        if (!buf.TryU32(ntOffset, out uint signature))
        {
            image.Add(new PeDiagnostic(PeSeverity.Error, "NtHeader",
                $"e_lfanew (0x{ntOffset:X}) points past the end of the file.", ntOffset));
            return false;
        }
        if (signature != NtSignature)
        {
            image.Add(new PeDiagnostic(PeSeverity.Error, "NtHeader",
                $"Expected the PE signature at 0x{ntOffset:X}, found 0x{signature:X8}.", ntOffset));
            return false;
        }
        return true;
    }

    // ── COFF ──────────────────────────────────────────────────────────────────

    private static bool TryCoff(PeBuffer buf, PeImage image, long offset)
    {
        if (!buf.InRange(offset, 20))
        {
            image.Add(new PeDiagnostic(PeSeverity.Error, "CoffHeader",
                "The COFF header is truncated.", offset));
            return false;
        }

        buf.TryU16(offset,      out ushort machine);
        buf.TryU16(offset + 2,  out ushort sectionCount);
        buf.TryU32(offset + 4,  out uint   timestamp);
        buf.TryU32(offset + 8,  out uint   symbolTable);
        buf.TryU32(offset + 12, out uint   symbolCount);
        buf.TryU16(offset + 16, out ushort optionalSize);
        buf.TryU16(offset + 18, out ushort characteristics);

        image.CoffHeader = new PeCoffHeader(
            (PeMachine)machine, sectionCount, timestamp, symbolTable, symbolCount,
            optionalSize, (PeFileCharacteristics)characteristics);
        return true;
    }

    // ── Optional header ───────────────────────────────────────────────────────

    private static void TryOptional(PeBuffer buf, PeImage image, long offset)
    {
        if (!buf.TryU16(offset, out ushort magic))
        {
            image.Add(new PeDiagnostic(PeSeverity.Error, "OptionalHeader",
                "The optional header is truncated.", offset));
            return;
        }

        bool is64 = magic == PeOptionalHeader.Pe32PlusMagic;
        if (magic != PeOptionalHeader.Pe32Magic && !is64)
        {
            image.Add(new PeDiagnostic(PeSeverity.Error, "OptionalHeader",
                magic == PeOptionalHeader.RomMagic
                    ? "ROM images are not supported."
                    : $"Unrecognised optional-header magic 0x{magic:X4}.", offset));
            return;
        }

        buf.TryU8 (offset + 2,  out byte majorLinker);
        buf.TryU8 (offset + 3,  out byte minorLinker);
        buf.TryU32(offset + 4,  out uint sizeOfCode);
        buf.TryU32(offset + 8,  out uint sizeOfInitData);
        buf.TryU32(offset + 12, out uint sizeOfUninitData);
        buf.TryU32(offset + 16, out uint entryPoint);
        buf.TryU32(offset + 20, out uint baseOfCode);

        uint  baseOfData = 0;
        ulong imageBase;
        long  tail;   // offset of SectionAlignment, which is where the two layouts re-converge

        if (is64)
        {
            buf.TryU64(offset + 24, out imageBase);
            tail = offset + 32;
        }
        else
        {
            buf.TryU32(offset + 24, out baseOfData);
            buf.TryU32(offset + 28, out uint imageBase32);
            imageBase = imageBase32;
            tail = offset + 32;
        }

        buf.TryU32(tail,      out uint   sectionAlignment);
        buf.TryU32(tail + 4,  out uint   fileAlignment);
        buf.TryU16(tail + 8,  out ushort majorOs);
        buf.TryU16(tail + 10, out ushort minorOs);
        buf.TryU16(tail + 12, out ushort majorImage);
        buf.TryU16(tail + 14, out ushort minorImage);
        buf.TryU16(tail + 16, out ushort majorSubsystem);
        buf.TryU16(tail + 18, out ushort minorSubsystem);
        // tail + 20 is Win32VersionValue, reserved and always zero.
        buf.TryU32(tail + 24, out uint   sizeOfImage);
        buf.TryU32(tail + 28, out uint   sizeOfHeaders);
        buf.TryU32(tail + 32, out uint   checkSum);
        buf.TryU16(tail + 36, out ushort subsystem);
        buf.TryU16(tail + 38, out ushort dllCharacteristics);

        ulong stackReserve, stackCommit, heapReserve, heapCommit;
        long  afterSizes;

        if (is64)
        {
            buf.TryU64(tail + 40, out stackReserve);
            buf.TryU64(tail + 48, out stackCommit);
            buf.TryU64(tail + 56, out heapReserve);
            buf.TryU64(tail + 64, out heapCommit);
            afterSizes = tail + 72;
        }
        else
        {
            buf.TryU32(tail + 40, out uint sr);
            buf.TryU32(tail + 44, out uint sc);
            buf.TryU32(tail + 48, out uint hr);
            buf.TryU32(tail + 52, out uint hc);
            (stackReserve, stackCommit, heapReserve, heapCommit) = (sr, sc, hr, hc);
            afterSizes = tail + 56;
        }

        // afterSizes is LoaderFlags (reserved); NumberOfRvaAndSizes follows it.
        buf.TryU32(afterSizes + 4, out uint numberOfRvaAndSizes);

        image.OptionalHeader = new PeOptionalHeader(
            magic, majorLinker, minorLinker, sizeOfCode, sizeOfInitData, sizeOfUninitData,
            entryPoint, baseOfCode, baseOfData, imageBase, sectionAlignment, fileAlignment,
            majorOs, minorOs, majorImage, minorImage, majorSubsystem, minorSubsystem,
            sizeOfImage, sizeOfHeaders, checkSum, (PeSubsystem)subsystem,
            (PeDllCharacteristics)dllCharacteristics,
            stackReserve, stackCommit, heapReserve, heapCommit, numberOfRvaAndSizes);

        ParseDirectories(buf, image, afterSizes + 8, numberOfRvaAndSizes);
    }

    private static void ParseDirectories(PeBuffer buf, PeImage image, long offset, uint declared)
    {
        // 16 is the architectural maximum; a larger count is corruption, a smaller one is legal.
        uint count = Math.Min(declared, 16);
        if (declared > 16)
            image.Add(new PeDiagnostic(PeSeverity.Warning, "OptionalHeader",
                $"NumberOfRvaAndSizes is {declared}; clamped to the architectural maximum of 16.", offset));

        var directories = new List<PeDataDirectory>((int)count);
        for (uint i = 0; i < count; i++)
        {
            long entry = offset + i * 8;
            if (!buf.TryU32(entry, out uint rva) || !buf.TryU32(entry + 4, out uint size))
            {
                image.Add(new PeDiagnostic(PeSeverity.Warning, "OptionalHeader",
                    $"The data directory table is truncated after {i} of {count} entries.", entry));
                break;
            }
            directories.Add(new PeDataDirectory((PeDirectory)i, rva, size));
        }
        image.DataDirectories = directories;
    }

    // ── Section table ─────────────────────────────────────────────────────────

    private static void TrySections(PeBuffer buf, PeImage image, long offset, PeReadOptions options)
    {
        int declared = image.CoffHeader?.NumberOfSections ?? 0;
        if (declared == 0) return;

        // 96 is the architectural ceiling and no real image comes close, so a larger count is a
        // corrupt or hostile header. Clamp to 96 rather than to "whatever fits in the file" —
        // otherwise a 0xFFFF count on a 350 KB image yields thousands of junk sections, each with
        // its own warning, and the caller has to render them.
        const int MaxSections = 96;
        int count = declared;
        if (declared > MaxSections)
        {
            count = MaxSections;
            image.Add(new PeDiagnostic(PeSeverity.Warning, "Sections",
                $"NumberOfSections is {declared}, beyond the architectural maximum of {MaxSections}; " +
                $"only the first {MaxSections} were read.", offset));
        }

        var sections = new List<PeSection>(count);
        for (int i = 0; i < count; i++)
        {
            long entry = offset + i * 40L;
            if (!buf.InRange(entry, 40))
            {
                image.Add(new PeDiagnostic(PeSeverity.Warning, "Sections",
                    $"The section table is truncated after {i} of {declared} entries.", entry));
                break;
            }

            var    nameBytes = buf.Slice(entry, 8);
            int    nul       = nameBytes.IndexOf((byte)0);
            string name      = Encoding.ASCII.GetString(nul >= 0 ? nameBytes[..nul] : nameBytes).TrimEnd();

            buf.TryU32(entry + 8,  out uint   virtualSize);
            buf.TryU32(entry + 12, out uint   virtualAddress);
            buf.TryU32(entry + 16, out uint   rawSize);
            buf.TryU32(entry + 20, out uint   rawPointer);
            buf.TryU32(entry + 24, out uint   relocPointer);
            buf.TryU16(entry + 32, out ushort relocCount);
            buf.TryU32(entry + 36, out uint   characteristics);

            // A raw range that runs past EOF is the commonest corruption; keep the entry but clamp
            // the bytes we will ever read from it.
            if (rawSize > 0 && !buf.InRange(rawPointer, rawSize))
            {
                image.Add(new PeDiagnostic(PeSeverity.Warning, "Sections",
                    $"Section '{name}' declares {rawSize} bytes at 0x{rawPointer:X} which extends past the end of the file.",
                    rawPointer));
                rawSize = (uint)Math.Max(0, Math.Min(rawSize, buf.Length - rawPointer));
            }

            var section = new PeSection(name, virtualSize, virtualAddress, rawSize, rawPointer,
                                        relocPointer, relocCount, (PeSectionCharacteristics)characteristics);

            if (rawSize > 0 && (options.IncludeEntropy || options.IncludeSectionHashes))
            {
                var bytes = buf.ToArray(rawPointer, rawSize);
                section = section with
                {
                    Entropy = options.IncludeEntropy       ? PeEntropy.Shannon(bytes)                : null,
                    Md5     = options.IncludeSectionHashes ? Convert.ToHexStringLower(
                                                                 System.Security.Cryptography.MD5.HashData(bytes))
                                                           : null,
                };
            }

            sections.Add(section);
        }
        image.Sections = sections;
    }
}

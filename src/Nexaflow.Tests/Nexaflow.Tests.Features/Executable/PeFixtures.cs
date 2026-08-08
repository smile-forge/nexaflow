using System;
using System.Buffers.Binary;
using System.IO;

namespace Nexaflow.Tests.Features.Executable;

/// <summary>
/// Real binaries and synthetic broken ones, without adding a single byte to the repository.
/// <para>
/// The good cases come from <c>%SystemRoot%\System32</c>: they are present on every Windows machine,
/// they are the exact shapes the inspector has to get right (a catalog-signed GUI app, an
/// export-heavy DLL, a native driver), and no checked-in fixture could stay as representative.
/// The bad cases are corrupted copies built in memory, so the malformed-input tests exercise
/// realistic damage rather than hand-written toy headers.
/// </para>
/// </summary>
internal static class PeFixtures
{
    public static string System32(string name)
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);

    /// <summary>Native PE32+, ~1700 exports, 200+ forwarders, Authenticode-signed in place.</summary>
    public static string Kernel32 => System32("kernel32.dll");

    /// <summary>A GUI app with an icon, version info, a full manifest — and catalog signing.</summary>
    public static string Notepad => System32("notepad.exe");

    /// <summary>A managed assembly: CLR header, assembly references, target framework.</summary>
    public static string ManagedAssembly => typeof(Nexaflow.IO.Pe.PeReader).Assembly.Location;

    public static bool Exists(string path) => File.Exists(path);

    // ── Synthetic damage ──────────────────────────────────────────────────────

    /// <summary>4 KB of zeroes — no MZ signature at all.</summary>
    public static byte[] NotAPe() => new byte[4096];

    /// <summary>An MZ signature and nothing else; e_lfanew reads past the end.</summary>
    public static byte[] MzOnly()
    {
        var bytes = new byte[100];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
    }

    /// <summary>A real binary cut off partway through its headers.</summary>
    public static byte[] Truncated(int length = 2048)
        => File.ReadAllBytes(Notepad).AsSpan(0, length).ToArray();

    /// <summary>e_lfanew pointing far past the end of the file.</summary>
    public static byte[] BadNtOffset()
    {
        var bytes = File.ReadAllBytes(Notepad);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x3C), 0xFFFF_FF00);
        return bytes;
    }

    /// <summary>NumberOfSections claiming 65535 — far beyond the architectural maximum.</summary>
    public static byte[] InsaneSectionCount()
    {
        var bytes = File.ReadAllBytes(Notepad);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(NtOffset(bytes) + 4 + 2), 0xFFFF);
        return bytes;
    }

    /// <summary>Import, export and resource directories all pointing at an unmapped RVA.</summary>
    public static byte[] DanglingDirectories()
    {
        var bytes = File.ReadAllBytes(Notepad);
        int directories = DirectoryTable(bytes);
        foreach (int index in (int[])[0, 1, 2])
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(directories + index * 8),     0x7F00_0000);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(directories + index * 8 + 4), 0x1000);
        }
        return bytes;
    }

    /// <summary>A section whose raw range runs past the end of the file.</summary>
    public static byte[] SectionPastEof()
    {
        var bytes = File.ReadAllBytes(Notepad);
        int table  = SectionTable(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(table + 16), 0x7FFF_FFFF);   // SizeOfRawData
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(table + 20), 0x7FFF_FF00);   // PointerToRawData
        return bytes;
    }

    /// <summary>A resource directory whose first entry is a subdirectory pointing back at the root.</summary>
    public static byte[] ResourceCycle()
    {
        var bytes = File.ReadAllBytes(Notepad);
        int  directories = DirectoryTable(bytes);
        uint resourceRva = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(directories + 2 * 8));

        int table = SectionTable(bytes);
        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(NtOffset(bytes) + 4 + 2));
        for (int i = 0; i < count; i++)
        {
            int  entry = table + i * 40;
            uint va    = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 12));
            uint size  = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 8));
            uint raw   = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 20));
            if (resourceRva < va || resourceRva >= va + size) continue;

            long root = raw + (resourceRva - va);
            // First entry's OffsetToData: high bit set (a subdirectory) at offset 0 — the root itself.
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan((int)root + 16 + 4), 0x8000_0000);
            break;
        }
        return bytes;
    }

    private static int NtOffset(byte[] bytes)
        => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x3C));

    private static int SectionTable(byte[] bytes)
    {
        int nt = NtOffset(bytes);
        return nt + 24 + BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(nt + 4 + 16));
    }

    private static int DirectoryTable(byte[] bytes)
    {
        int  nt   = NtOffset(bytes);
        bool is64 = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(nt + 24)) == 0x20B;
        return nt + 24 + (is64 ? 112 : 96);
    }
}

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Nexaflow.Features.Font.Decoding;

/// <summary>
/// Decodes a WOFF 1.0 container into raw sfnt bytes. WOFF is a thin wrapper over a normal
/// TrueType/OpenType font: a 44-byte header, a table directory, then each table's data stored either
/// verbatim or zlib-compressed. Reconstruction is mechanical — inflate any compressed table and lay the
/// tables back out behind a standard sfnt offset table + directory (4-byte aligned). Original table
/// checksums are preserved; WPF does not validate <c>head.checkSumAdjustment</c>, so it is left as-is.
/// <para>WOFF2 (Brotli + glyf/loca transforms) is a separate, harder format — a future
/// <c>Woff2Decoder : IFontFormatDecoder</c> slots into <see cref="FontSourceResolver"/> alongside this.</para>
/// </summary>
public sealed class WoffDecoder : IFontFormatDecoder
{
    private const uint WoffSignature = 0x774F4646; // 'wOFF'

    public bool CanDecode(string extension) =>
        string.Equals(extension, ".woff", StringComparison.OrdinalIgnoreCase);

    public byte[] DecodeToSfnt(Stream input)
    {
        using var ms = new MemoryStream();
        input.CopyTo(ms);
        var woff = ms.ToArray();

        if (woff.Length < 44 || ReadU32(woff, 0) != WoffSignature)
            throw new InvalidDataException("Not a WOFF file (bad signature).");

        uint flavor    = ReadU32(woff, 4);
        int  numTables = ReadU16(woff, 12);

        // Read the table directory (each entry: tag, offset, compLength, origLength, origChecksum).
        var entries = new WoffEntry[numTables];
        int dir = 44;
        for (int i = 0; i < numTables; i++)
        {
            int p = dir + i * 20;
            entries[i] = new WoffEntry(
                Tag:        ReadU32(woff, p),
                Offset:     (int)ReadU32(woff, p + 4),
                CompLength: (int)ReadU32(woff, p + 8),
                OrigLength: (int)ReadU32(woff, p + 12),
                Checksum:   ReadU32(woff, p + 16));
        }

        // Inflate each table (or copy when stored uncompressed: compLength == origLength).
        var tables = new byte[numTables][];
        for (int i = 0; i < numTables; i++)
        {
            var e = entries[i];
            if (e.CompLength == e.OrigLength)
            {
                tables[i] = woff.AsSpan(e.Offset, e.OrigLength).ToArray();
            }
            else
            {
                var outBuf = new byte[e.OrigLength];
                using var comp = new MemoryStream(woff, e.Offset, e.CompLength, writable: false);
                using var zlib = new ZLibStream(comp, CompressionMode.Decompress);
                ReadExactly(zlib, outBuf);
                tables[i] = outBuf;
            }
        }

        return BuildSfnt(flavor, entries, tables);
    }

    private static byte[] BuildSfnt(uint flavor, WoffEntry[] entries, byte[][] tables)
    {
        int numTables = entries.Length;
        int headerSize = 12 + numTables * 16;

        int total = headerSize;
        var offsets = new int[numTables];
        for (int i = 0; i < numTables; i++)
        {
            offsets[i] = total;
            total += Align4(tables[i].Length);
        }

        var sfnt = new byte[total];

        // Offset table.
        WriteU32(sfnt, 0, flavor);
        WriteU16(sfnt, 4, (ushort)numTables);
        int maxPow2 = HighestPowerOfTwo(numTables);
        ushort searchRange   = (ushort)(maxPow2 * 16);
        ushort entrySelector = (ushort)Log2(maxPow2);
        ushort rangeShift    = (ushort)(numTables * 16 - searchRange);
        WriteU16(sfnt, 6, searchRange);
        WriteU16(sfnt, 8, entrySelector);
        WriteU16(sfnt, 10, rangeShift);

        // Directory + table data (directory must be sorted by tag; the WOFF directory already is,
        // but sort defensively so a non-conforming producer still yields a valid sfnt).
        var order = new int[numTables];
        for (int i = 0; i < numTables; i++) order[i] = i;
        Array.Sort(order, (a, b) => entries[a].Tag.CompareTo(entries[b].Tag));

        for (int slot = 0; slot < numTables; slot++)
        {
            int i = order[slot];
            int rec = 12 + slot * 16;
            WriteU32(sfnt, rec, entries[i].Tag);
            WriteU32(sfnt, rec + 4, entries[i].Checksum);
            WriteU32(sfnt, rec + 8, (uint)offsets[i]);
            WriteU32(sfnt, rec + 12, (uint)tables[i].Length);
            Buffer.BlockCopy(tables[i], 0, sfnt, offsets[i], tables[i].Length);
        }

        return sfnt;
    }

    private readonly record struct WoffEntry(uint Tag, int Offset, int CompLength, int OrigLength, uint Checksum);

    private static int Align4(int n) => (n + 3) & ~3;

    private static int HighestPowerOfTwo(int n)
    {
        int p = 1;
        while (p * 2 <= n) p *= 2;
        return p;
    }

    private static int Log2(int n)
    {
        int r = 0;
        while (n > 1) { n >>= 1; r++; }
        return r;
    }

    private static void ReadExactly(Stream s, byte[] buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = s.Read(buffer, read, buffer.Length - read);
            if (n == 0) throw new InvalidDataException("WOFF table decompressed short.");
            read += n;
        }
    }

    private static uint ReadU32(byte[] b, int o) => BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o, 4));
    private static ushort ReadU16(byte[] b, int o) => BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(o, 2));
    private static void WriteU32(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o, 4), v);
    private static void WriteU16(byte[] b, int o, ushort v) => BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(o, 2), v);
}

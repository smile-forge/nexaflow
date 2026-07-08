using System.IO.Compression;
using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Builds a tiny but genuinely valid TrueType font (and its WOFF 1.0 wrapping) entirely in code —
/// no external tool, no third-party font bytes, fully deterministic. It carries the ten required
/// OpenType tables (OS/2, cmap, glyf, head, hhea, hmtx, loca, maxp, name, post), correct per-table
/// and whole-file checksums, a family name of "<see cref="FamilyName"/>", and one square outline mapped
/// across A–Z / a–z / 0–9 so it both parses in WPF's <c>GlyphTypeface</c> and renders something visible.
///
/// The WOFF form zlib-compresses each table so it exercises the viewer's decode path end-to-end.
/// </summary>
internal static class MinimalFont
{
    public const string FamilyName = "Nexaflow Test";

    private const int UnitsPerEm = 1024;
    private const int GlyphBytes = 34;      // one square glyph, fixed size (see BuildGlyf)

    // Glyph 0 = .notdef; glyphs 1.. map A–Z, a–z, 0–9 (all the same square).
    private const int NumGlyphs = 1 + 26 + 26 + 10; // 63

    public static byte[] BuildTtf() => Assemble(BuildTables(), patchHeadAdjustment: true);

    public static byte[] BuildWoff() => Woff(BuildTables());

    private sealed record Table(string Tag, byte[] Data);

    /// <summary>The ten tables in ascending-tag order (the order an sfnt directory requires).</summary>
    private static List<Table> BuildTables() =>
    [
        new("OS/2", BuildOs2()),
        new("cmap", BuildCmap()),
        new("glyf", BuildGlyf()),
        new("head", BuildHead()),
        new("hhea", BuildHhea()),
        new("hmtx", BuildHmtx()),
        new("loca", BuildLoca()),
        new("maxp", BuildMaxp()),
        new("name", BuildName()),
        new("post", BuildPost()),
    ];

    // ── sfnt assembly ─────────────────────────────────────────────────────────

    private static byte[] Assemble(List<Table> tables, bool patchHeadAdjustment)
    {
        int numTables = tables.Count;
        int headerSize = 12 + numTables * 16;

        var offsets = new int[numTables];
        int pos = headerSize;
        for (int i = 0; i < numTables; i++)
        {
            offsets[i] = pos;
            pos += Align4(tables[i].Data.Length);
        }

        var buf = new byte[pos];
        int maxPow2 = HighestPowerOfTwo(numTables);
        WriteU32(buf, 0, 0x00010000);
        WriteU16(buf, 4, (ushort)numTables);
        WriteU16(buf, 6, (ushort)(maxPow2 * 16));
        WriteU16(buf, 8, (ushort)Log2(maxPow2));
        WriteU16(buf, 10, (ushort)(numTables * 16 - maxPow2 * 16));

        int headOffset = -1;
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + i * 16;
            WriteTag(buf, rec, tables[i].Tag);
            WriteU32(buf, rec + 4, ChecksumPadded(tables[i].Data));
            WriteU32(buf, rec + 8, (uint)offsets[i]);
            WriteU32(buf, rec + 12, (uint)tables[i].Data.Length);
            Buffer.BlockCopy(tables[i].Data, 0, buf, offsets[i], tables[i].Data.Length);
            if (tables[i].Tag == "head") headOffset = offsets[i];
        }

        if (patchHeadAdjustment && headOffset >= 0)
        {
            uint fileSum = ChecksumPadded(buf);
            WriteU32(buf, headOffset + 8, unchecked(0xB1B0AFBA - fileSum));
        }

        return buf;
    }

    // ── WOFF 1.0 wrapping (each table zlib-compressed when it helps) ───────────

    private static byte[] Woff(List<Table> tables)
    {
        int numTables = tables.Count;
        int sfntSize = 12 + numTables * 16;
        foreach (var t in tables) sfntSize += Align4(t.Data.Length);

        // Compress table data (store verbatim if compression doesn't shrink it).
        var comp = new byte[numTables][];
        for (int i = 0; i < numTables; i++)
        {
            var deflated = Deflate(tables[i].Data);
            comp[i] = deflated.Length < tables[i].Data.Length ? deflated : tables[i].Data;
        }

        int dirSize = 44 + numTables * 20;
        int total = dirSize;
        var offsets = new int[numTables];
        for (int i = 0; i < numTables; i++)
        {
            offsets[i] = total;
            total += Align4(comp[i].Length);
        }

        var buf = new byte[total];
        WriteU32(buf, 0, 0x774F4646);       // 'wOFF'
        WriteU32(buf, 4, 0x00010000);       // flavor (TrueType)
        WriteU32(buf, 8, (uint)total);      // length
        WriteU16(buf, 12, (ushort)numTables);
        WriteU16(buf, 14, 0);               // reserved
        WriteU32(buf, 16, (uint)sfntSize);  // totalSfntSize
        WriteU16(buf, 20, 1);               // majorVersion
        WriteU16(buf, 22, 0);               // minorVersion
        // metaOffset/metaLength/metaOrigLength/privOffset/privLength stay zero.

        for (int i = 0; i < numTables; i++)
        {
            int rec = 44 + i * 20;
            WriteTag(buf, rec, tables[i].Tag);
            WriteU32(buf, rec + 4, (uint)offsets[i]);
            WriteU32(buf, rec + 8, (uint)comp[i].Length);         // compLength
            WriteU32(buf, rec + 12, (uint)tables[i].Data.Length); // origLength
            WriteU32(buf, rec + 16, ChecksumPadded(tables[i].Data));
            Buffer.BlockCopy(comp[i], 0, buf, offsets[i], comp[i].Length);
        }

        return buf;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zlib = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    // ── Individual tables ─────────────────────────────────────────────────────

    private static byte[] BuildHead()
    {
        var w = new Be(54);
        w.U32(0x00010000);   // version
        w.U32(0x00010000);   // fontRevision
        w.U32(0);            // checkSumAdjustment (patched last)
        w.U32(0x5F0F3CF5);   // magicNumber
        w.U16(0);            // flags
        w.U16(UnitsPerEm);
        w.U32(0); w.U32(0);  // created  (LONGDATETIME)
        w.U32(0); w.U32(0);  // modified
        w.I16(100); w.I16(0); w.I16(900); w.I16(700); // xMin,yMin,xMax,yMax
        w.U16(0);            // macStyle
        w.U16(8);            // lowestRecPPEM
        w.I16(2);            // fontDirectionHint
        w.I16(0);            // indexToLocFormat (0 = short)
        w.I16(0);            // glyphDataFormat
        return w.ToArray();
    }

    private static byte[] BuildHhea()
    {
        var w = new Be(36);
        w.U32(0x00010000);
        w.I16(800); w.I16(-200); w.I16(0);  // ascender, descender, lineGap
        w.U16(UnitsPerEm);                  // advanceWidthMax
        w.I16(0); w.I16(0); w.I16(900);     // min LSB, min RSB, xMaxExtent
        w.I16(1); w.I16(0); w.I16(0);       // caret slope rise/run/offset
        w.I16(0); w.I16(0); w.I16(0); w.I16(0); // reserved ×4
        w.I16(0);                            // metricDataFormat
        w.U16(1);                            // numberOfHMetrics
        return w.ToArray();
    }

    private static byte[] BuildMaxp()
    {
        var w = new Be(32);
        w.U32(0x00010000);
        w.U16(NumGlyphs);
        w.U16(4);   // maxPoints
        w.U16(1);   // maxContours
        w.U16(0); w.U16(0); // maxComposite points/contours
        w.U16(2);   // maxZones
        w.U16(0); w.U16(0); w.U16(0); w.U16(0); w.U16(0); w.U16(0); // storage/fn/instr/stack
        w.U16(0); w.U16(0); // maxComponentElements/Depth
        return w.ToArray();
    }

    private static byte[] BuildOs2()
    {
        var w = new Be(96);
        w.U16(4);           // version
        w.I16(500);         // xAvgCharWidth
        w.U16(400);         // usWeightClass (Regular)
        w.U16(5);           // usWidthClass (Medium)
        w.U16(0);           // fsType (installable)
        w.I16(650); w.I16(700); w.I16(0); w.I16(140);   // subscript
        w.I16(650); w.I16(700); w.I16(0); w.I16(480);   // superscript
        w.I16(50); w.I16(300);   // strikeout size/pos
        w.I16(0);           // sFamilyClass
        for (int i = 0; i < 10; i++) w.U8(0);            // panose
        w.U32(1); w.U32(0); w.U32(0); w.U32(0);          // ulUnicodeRange1..4 (bit0 = Basic Latin)
        w.Ascii("NEXA");    // achVendID
        w.U16(0x0040);      // fsSelection (REGULAR)
        w.U16(0x30);        // usFirstCharIndex ('0')
        w.U16(0x7A);        // usLastCharIndex ('z')
        w.I16(800); w.I16(-200); w.I16(0);   // sTypo ascender/descender/lineGap
        w.U16(800); w.U16(200);              // usWinAscent/Descent
        w.U32(1); w.U32(0);                  // ulCodePageRange1/2 (Latin1)
        w.I16(500); w.I16(700);              // sxHeight, sCapHeight
        w.U16(0); w.U16(0x20); w.U16(1);     // default/break char, maxContext
        return w.ToArray();
    }

    private static byte[] BuildPost()
    {
        var w = new Be(32);
        w.U32(0x00030000);  // version 3.0 (no glyph names)
        w.U32(0);           // italicAngle
        w.I16(-100);        // underlinePosition
        w.I16(50);          // underlineThickness
        w.U32(0);           // isFixedPitch
        w.U32(0); w.U32(0); w.U32(0); w.U32(0); // mem type42/type1
        return w.ToArray();
    }

    private static byte[] BuildLoca()
    {
        // Short format: uint16 = offset / 2. All glyphs are GlyphBytes long.
        var w = new Be((NumGlyphs + 1) * 2);
        for (int i = 0; i <= NumGlyphs; i++) w.U16((i * GlyphBytes) / 2);
        return w.ToArray();
    }

    private static byte[] BuildGlyf()
    {
        var w = new Be(NumGlyphs * GlyphBytes);
        for (int i = 0; i < NumGlyphs; i++) WriteSquareGlyph(w);
        return w.ToArray();
    }

    private static void WriteSquareGlyph(Be w)
    {
        w.I16(1);                                   // numberOfContours
        w.I16(100); w.I16(0); w.I16(900); w.I16(700); // bbox
        w.U16(3);                                   // endPtsOfContours[0] (4 points)
        w.U16(0);                                   // instructionLength
        w.U8(0x01); w.U8(0x01); w.U8(0x01); w.U8(0x01); // flags: all on-curve, long coords
        w.I16(100); w.I16(800); w.I16(0); w.I16(-800);  // x deltas → 100,900,900,100
        w.I16(0); w.I16(0); w.I16(700); w.I16(0);       // y deltas → 0,0,700,700
    }

    private static byte[] BuildHmtx()
    {
        // numberOfHMetrics = 1: one longHorMetric, then leftSideBearing for the rest.
        var w = new Be(4 + (NumGlyphs - 1) * 2);
        w.U16(UnitsPerEm); w.I16(100);              // advanceWidth, lsb
        for (int i = 1; i < NumGlyphs; i++) w.I16(100);
        return w.ToArray();
    }

    private static byte[] BuildCmap()
    {
        // Format 4, segments: 0-9, A-Z, a-z, then the mandatory 0xFFFF terminator.
        ushort[] end   = [0x39, 0x5A, 0x7A, 0xFFFF];
        ushort[] start = [0x30, 0x41, 0x61, 0xFFFF];
        short[]  delta = [(short)(53 - 0x30), (short)(1 - 0x41), (short)(27 - 0x61), (short)1];
        int segCount = end.Length;
        int subLen = 16 + segCount * 8; // header(14) + reservedPad(2) + 4 arrays × segCount×2

        var w = new Be(12 + subLen);
        // cmap header
        w.U16(0);           // version
        w.U16(1);           // numTables
        w.U16(3); w.U16(1); // platform 3, encoding 1 (Windows BMP)
        w.U32(12);          // offset to subtable
        // format 4 subtable
        w.U16(4);
        w.U16((ushort)subLen);
        w.U16(0);           // language
        w.U16((ushort)(segCount * 2));            // segCountX2
        int maxPow2 = HighestPowerOfTwo(segCount);
        w.U16((ushort)(maxPow2 * 2));             // searchRange
        w.U16((ushort)Log2(maxPow2));             // entrySelector
        w.U16((ushort)(segCount * 2 - maxPow2 * 2)); // rangeShift
        foreach (var e in end) w.U16(e);
        w.U16(0);           // reservedPad
        foreach (var s in start) w.U16(s);
        foreach (var d in delta) w.I16(d);
        for (int i = 0; i < segCount; i++) w.U16(0); // idRangeOffset
        return w.ToArray();
    }

    private static byte[] BuildName()
    {
        (int id, string text)[] records =
        [
            (1, FamilyName),
            (2, "Regular"),
            (4, FamilyName + " Regular"),
            (6, "NexaflowTest-Regular"),
        ];

        var strings = new List<byte[]>();
        foreach (var (_, text) in records) strings.Add(Encoding.BigEndianUnicode.GetBytes(text));

        int count = records.Length;
        int stringOffset = 6 + count * 12;

        var w = new Be(stringOffset + strings.Sum(s => s.Length));
        w.U16(0);                    // format
        w.U16((ushort)count);
        w.U16((ushort)stringOffset);

        int running = 0;
        for (int i = 0; i < count; i++)
        {
            w.U16(3); w.U16(1); w.U16(0x0409);       // Windows, BMP, en-US
            w.U16((ushort)records[i].id);
            w.U16((ushort)strings[i].Length);
            w.U16((ushort)running);
            running += strings[i].Length;
        }
        foreach (var s in strings) w.Bytes(s);
        return w.ToArray();
    }

    // ── Low-level helpers ─────────────────────────────────────────────────────

    private static int Align4(int n) => (n + 3) & ~3;
    private static int HighestPowerOfTwo(int n) { int p = 1; while (p * 2 <= n) p *= 2; return p; }
    private static int Log2(int n) { int r = 0; while (n > 1) { n >>= 1; r++; } return r; }

    private static uint ChecksumPadded(byte[] data)
    {
        uint sum = 0;
        int full = data.Length & ~3;
        for (int i = 0; i < full; i += 4)
            sum = unchecked(sum + ((uint)data[i] << 24 | (uint)data[i + 1] << 16 | (uint)data[i + 2] << 8 | data[i + 3]));
        if (full < data.Length)
        {
            uint last = 0;
            for (int i = full; i < data.Length; i++) last |= (uint)data[i] << (24 - 8 * (i - full));
            sum = unchecked(sum + last);
        }
        return sum;
    }

    private static void WriteU16(byte[] b, int o, ushort v) { b[o] = (byte)(v >> 8); b[o + 1] = (byte)v; }
    private static void WriteU32(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }
    private static void WriteTag(byte[] b, int o, string tag)
    {
        for (int i = 0; i < 4; i++) b[o + i] = i < tag.Length ? (byte)tag[i] : (byte)' ';
    }

    /// <summary>Minimal big-endian writer over a growable buffer.</summary>
    private sealed class Be(int capacity)
    {
        private readonly List<byte> _b = new(capacity);
        public void U8(int v) => _b.Add((byte)v);
        public void U16(int v) { _b.Add((byte)(v >> 8)); _b.Add((byte)v); }
        public void I16(int v) => U16((ushort)(short)v);
        public void U32(uint v) { _b.Add((byte)(v >> 24)); _b.Add((byte)(v >> 16)); _b.Add((byte)(v >> 8)); _b.Add((byte)v); }
        public void Ascii(string s) { foreach (var c in s) _b.Add((byte)c); }
        public void Bytes(byte[] data) => _b.AddRange(data);
        public byte[] ToArray() => _b.ToArray();
    }
}

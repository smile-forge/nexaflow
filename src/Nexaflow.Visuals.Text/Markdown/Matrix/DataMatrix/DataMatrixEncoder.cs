using System;
using System.Collections.Generic;
using System.Text;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.DataMatrix;

/// <summary>Which of the two families of symbol size the encoder may pick from.</summary>
public enum DataMatrixShape
{
    /// <summary>The smallest symbol that fits, square or rectangular.</summary>
    Any,

    /// <summary>Square symbols only — 10×10 up to 144×144.</summary>
    Square,

    /// <summary>The six rectangular symbols only — 8×18 up to 16×48.</summary>
    Rectangle,
}

/// <summary>The Macro codewords: an industry header and trailer folded into one codeword each.</summary>
public enum DataMatrixMacro
{
    None,

    /// <summary>Macro 05: <c>[)>RS05GS</c> … <c>RSEOT</c>.</summary>
    Macro05,

    /// <summary>Macro 06: <c>[)>RS06GS</c> … <c>RSEOT</c> — what a PPN symbol wraps its fields in.</summary>
    Macro06,
}

/// <summary>What the encoder is asked for beyond the text.</summary>
public sealed record DataMatrixOptions
{
    public DataMatrixShape Shape { get; init; } = DataMatrixShape.Any;

    /// <summary>A symbol size to use regardless of what would fit — rows by columns. Null to pick the smallest.</summary>
    public (int Rows, int Columns)? Size { get; init; }

    /// <summary>Emit FNC1 first, marking the symbol as GS1 — and make an ASCII 0x1D a separator FNC1 rather than a byte.</summary>
    public bool Gs1 { get; init; }

    public DataMatrixMacro Macro { get; init; } = DataMatrixMacro.None;

    public static readonly DataMatrixOptions Default = new();
}

/// <summary>A finished Data Matrix symbol — ECC 200 — and the choices that produced it.</summary>
public sealed class DataMatrixSymbol : IModuleMatrix
{
    private readonly bool[] _modules;

    internal DataMatrixSymbol(DataMatrixSize size, bool[] modules, int dataCodewordsUsed, string encodation)
    {
        Size              = size;
        _modules          = modules;
        DataCodewordsUsed = dataCodewordsUsed;
        Encodation        = encodation;
    }

    public DataMatrixSize Size { get; }

    public int Width  => Size.Columns;
    public int Height => Size.Rows;

    /// <summary>How many of the symbol's data codewords the payload took, before padding.</summary>
    public int DataCodewordsUsed { get; }

    /// <summary>The encodation the payload was written in — <c>ASCII</c> or <c>C40</c> — for the tooltip.</summary>
    public string Encodation { get; }

    public bool this[int x, int y] => _modules[y * Size.Columns + x];
}

/// <summary>
/// One row of the ECC 200 size table: a symbol's overall size, how it is divided into data regions,
/// and how many codewords it holds.
/// </summary>
/// <param name="Rows">Modules down, finder patterns included.</param>
/// <param name="Columns">Modules across.</param>
/// <param name="RegionRows">Data modules down in one region — the region's finder border is not counted.</param>
/// <param name="RegionColumns">Data modules across in one region.</param>
/// <param name="RegionsDown">How many regions are stacked vertically.</param>
/// <param name="RegionsAcross">How many regions sit side by side.</param>
/// <param name="DataCodewords">Codewords of payload the symbol carries.</param>
/// <param name="EccCodewords">Parity codewords across all blocks.</param>
/// <param name="Blocks">How many Reed–Solomon blocks the codewords are split into.</param>
public readonly record struct DataMatrixSize(
    int Rows, int Columns, int RegionRows, int RegionColumns, int RegionsDown, int RegionsAcross,
    int DataCodewords, int EccCodewords, int Blocks)
{
    public bool IsSquare => Rows == Columns;

    /// <summary>All the data modules of the symbol laid edge to edge, as the placement algorithm sees them.</summary>
    public int MappingRows    => RegionRows    * RegionsDown;
    public int MappingColumns => RegionColumns * RegionsAcross;

    public override string ToString() => $"{Rows}×{Columns}";
}

/// <summary>
/// Encodes text as a Data Matrix symbol — ISO/IEC 16022, the ECC 200 kind that everything since 1995
/// has meant by the name.
///
/// <para>
/// The work is in three parts, none of which knows the others: turn the text into codewords in the
/// encodation that makes them fewest, choose the smallest symbol they fit, and lay them into it. The
/// last is the part that looks like magic in the standard — Annex F's "utah" shapes — and is copied
/// here as it is written there, because it is a placement rule with no derivation to reconstruct and
/// every other implementation is a transcription of the same annex.
/// </para>
/// <para>
/// Two encodations are used: ASCII, which carries anything and pairs digits, and C40, which packs three
/// upper-case characters into two codewords and is what lets a Royal Mail Mailmark's ninety characters
/// fit the 32×32 symbol the spec mandates for them. Whichever produces fewer codewords wins; a reader
/// decodes either, so choosing between them is a matter of size and nothing else.
/// </para>
/// <para>
/// Text outside ASCII is written as UTF-8 under ECI 26, which is how a reader is told the encoding.
/// </para>
/// </summary>
public static class DataMatrixEncoder
{
    // ── The ECC 200 size table (ISO/IEC 16022 Table 7) ──────────────────────

    /// <summary>Every size the standard defines, smallest first within each shape.</summary>
    public static readonly DataMatrixSize[] Sizes =
    [
        // rows, cols, region rows, region cols, regions down, regions across, data cw, ecc cw, blocks
        new( 10,  10,  8,  8, 1, 1,    3,   5,  1),
        new( 12,  12, 10, 10, 1, 1,    5,   7,  1),
        new( 14,  14, 12, 12, 1, 1,    8,  10,  1),
        new( 16,  16, 14, 14, 1, 1,   12,  12,  1),
        new( 18,  18, 16, 16, 1, 1,   18,  14,  1),
        new( 20,  20, 18, 18, 1, 1,   22,  18,  1),
        new( 22,  22, 20, 20, 1, 1,   30,  20,  1),
        new( 24,  24, 22, 22, 1, 1,   36,  24,  1),
        new( 26,  26, 24, 24, 1, 1,   44,  28,  1),
        new( 32,  32, 14, 14, 2, 2,   62,  36,  1),
        new( 36,  36, 16, 16, 2, 2,   86,  42,  1),
        new( 40,  40, 18, 18, 2, 2,  114,  48,  1),
        new( 44,  44, 20, 20, 2, 2,  144,  56,  1),
        new( 48,  48, 22, 22, 2, 2,  174,  68,  1),
        new( 52,  52, 24, 24, 2, 2,  204,  84,  2),
        new( 64,  64, 14, 14, 4, 4,  280, 112,  2),
        new( 72,  72, 16, 16, 4, 4,  368, 144,  4),
        new( 80,  80, 18, 18, 4, 4,  456, 192,  4),
        new( 88,  88, 20, 20, 4, 4,  576, 224,  4),
        new( 96,  96, 22, 22, 4, 4,  696, 272,  4),
        new(104, 104, 24, 24, 4, 4,  816, 336,  6),
        new(120, 120, 18, 18, 6, 6, 1050, 408,  6),
        new(132, 132, 20, 20, 6, 6, 1304, 496,  8),
        new(144, 144, 22, 22, 6, 6, 1558, 620, 10),

        new(  8,  18,  6, 16, 1, 1,    5,   7,  1),
        new(  8,  32,  6, 14, 1, 2,   10,  11,  1),
        new( 12,  26, 10, 24, 1, 1,   16,  14,  1),
        new( 12,  36, 10, 16, 1, 2,   22,  18,  1),
        new( 16,  36, 14, 16, 1, 2,   32,  24,  1),
        new( 16,  48, 14, 22, 1, 2,   49,  28,  1),
    ];

    /// <summary>The size named by rows and columns, if the standard defines one.</summary>
    public static bool TryGetSize(int rows, int columns, out DataMatrixSize size)
    {
        foreach (var s in Sizes)
            if (s.Rows == rows && s.Columns == columns) { size = s; return true; }
        size = default;
        return false;
    }

    // ── Codeword values with a meaning of their own ────────────────────────

    private const int Pad        = 129;
    private const int LatchC40   = 230;
    private const int Fnc1       = 232;
    private const int UpperShift = 235;
    private const int Macro05    = 236;
    private const int Macro06    = 237;
    private const int Eci        = 241;
    private const int Unlatch    = 254;

    // ── Encoding ───────────────────────────────────────────────────────────

    public static bool TryEncode(string text, DataMatrixOptions options,
                                 out DataMatrixSymbol? symbol, out string? error)
    {
        symbol = null;
        error  = null;
        text ??= string.Empty;

        if (options.Macro != DataMatrixMacro.None && options.Gs1)
        {
            error = "A symbol cannot be both GS1 and a Macro: each claims the first codeword.";
            return false;
        }

        // The prefix: what says how to read the rest. ECI for anything outside ASCII, since the default
        // is Latin-1 and a UTF-8 payload read as Latin-1 is mojibake the author never sees.
        var prefix = new List<int>();
        bool ascii = IsAscii(text);
        if (!ascii) { prefix.Add(Eci); prefix.Add(26 + 1); }
        if (options.Gs1) prefix.Add(Fnc1);
        if (options.Macro == DataMatrixMacro.Macro05) prefix.Add(Macro05);
        if (options.Macro == DataMatrixMacro.Macro06) prefix.Add(Macro06);

        byte[] bytes = ascii ? Encoding.ASCII.GetBytes(text) : Encoding.UTF8.GetBytes(text);

        // Both encodations, and the shorter wins. C40 is only tried where it can help — it cannot carry
        // a byte above 127 at all, and below that it is only a saving on text that is mostly upper case.
        var asciiWords = EncodeAscii(bytes, options.Gs1);
        var c40        = ascii ? C40Text.From(bytes, options.Gs1) : null;
        var c40Words   = c40?.Estimate();

        bool useC40 = c40Words is not null && c40Words.Count < asciiWords.Count;
        var body    = useC40 ? c40Words! : asciiWords;

        if (!TryChooseSize(prefix.Count + body.Count, options, out var size, out error)) return false;

        // C40's ending depends on how much room the symbol leaves, so it is written once the size is known.
        if (useC40) body = c40!.Finish(size.DataCodewords - prefix.Count);

        var data = new List<int>(prefix.Count + body.Count);
        data.AddRange(prefix);
        data.AddRange(body);
        int used = data.Count;

        PadTo(data, size.DataCodewords);

        var codewords = AddParity(data, size);
        var modules   = Place(codewords, size);

        symbol = new DataMatrixSymbol(size, modules, used, useC40 ? "C40" : "ASCII");
        return true;
    }

    private static bool IsAscii(string text)
    {
        foreach (char c in text) if (c > 127) return false;
        return true;
    }

    // ── ASCII encodation ───────────────────────────────────────────────────

    /// <summary>
    /// Every byte as itself plus one, digit pairs as one codeword, and bytes above 127 behind an upper
    /// shift. The one encodation that can carry anything, so it is the baseline the other is measured
    /// against.
    /// </summary>
    internal static List<int> EncodeAscii(ReadOnlySpan<byte> bytes, bool gs1)
    {
        var words = new List<int>(bytes.Length);

        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];

            if (IsDigit(b) && i + 1 < bytes.Length && IsDigit(bytes[i + 1]))
            {
                words.Add(130 + (b - '0') * 10 + (bytes[i + 1] - '0'));
                i++;
                continue;
            }

            if (gs1 && b == 0x1D) { words.Add(Fnc1); continue; }

            if (b >= 128) { words.Add(UpperShift); words.Add(b - 128 + 1); continue; }

            words.Add(b + 1);
        }

        return words;
    }

    private static bool IsDigit(byte b) => b is >= (byte)'0' and <= (byte)'9';

    // ── C40 encodation ─────────────────────────────────────────────────────

    /// <summary>
    /// A text as C40 values: three values into two codewords, for text that is mostly capitals and
    /// digits. Anything else is reached through a shift — a lower-case letter costs two values, a
    /// punctuation mark two — so the saving only exists on text that rarely needs one, which is exactly
    /// the text the industrial formats that mandate this encodation are made of.
    /// </summary>
    private sealed class C40Text
    {
        private readonly byte[] _bytes;
        private readonly bool _gs1;
        private readonly List<int> _values = [];

        /// <summary>The byte each value came from — a shifted character contributes two values from one byte.</summary>
        private readonly List<int> _byteOf = [];

        private C40Text(byte[] bytes, bool gs1) { _bytes = bytes; _gs1 = gs1; }

        /// <summary>The values for the text, or null when a byte cannot be expressed in C40.</summary>
        public static C40Text? From(byte[] bytes, bool gs1)
        {
            var text = new C40Text(bytes, gs1);

            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                if (b > 127) return null;

                if (gs1 && b == 0x1D)                             { text.Add(i, 1, 27); continue; }   // shift 2, FNC1
                if (b == ' ')                                     { text.Add(i, 3);     continue; }
                if (b is >= (byte)'0' and <= (byte)'9')           { text.Add(i, b - '0' + 4);  continue; }
                if (b is >= (byte)'A' and <= (byte)'Z')           { text.Add(i, b - 'A' + 14); continue; }
                if (b < 32)                                       { text.Add(i, 0, b); continue; }               // shift 1: controls
                if (b is >= (byte)'a' and <= (byte)'z')           { text.Add(i, 2, b - 'a' + 14); continue; }    // shift 3: lower case

                int punct = Shift2.IndexOf((char)b);
                if (punct < 0) return null;
                text.Add(i, 1, punct);                                                                        // shift 2: punctuation
            }

            return text;
        }

        /// <summary>The shift-2 set, in the order the standard numbers it.</summary>
        private const string Shift2 = "!\"#$%&'()*+,-./:;<=>?@[\\]^_";

        private void Add(int byteIndex, params int[] values)
        {
            foreach (int v in values) { _values.Add(v); _byteOf.Add(byteIndex); }
        }

        /// <summary>
        /// A codeword count to compare against ASCII's, with the ending taken at its longest — unlatch and
        /// the leftovers in ASCII — because the exact ending is not known until the symbol is.
        /// </summary>
        public List<int> Estimate()
        {
            var words = new List<int> { LatchC40 };
            int full  = AlignedTriples();

            for (int i = 0; i < full; i += 3) AddTriple(words, i);

            words.Add(Unlatch);
            words.AddRange(EncodeAscii(_bytes.AsSpan(full == 0 ? 0 : _byteOf[full - 1] + 1), _gs1));
            return words;
        }

        /// <summary>
        /// The body with its ending written for a symbol with <paramref name="room"/> data codewords
        /// to spare after the prefix.
        /// <para>
        /// The standard's end-of-data rules exist to save a codeword at the edge of the symbol: two values
        /// left with exactly two codewords of room are packed with a pad and need no unlatch; one value
        /// with one codeword of room is written in ASCII with the unlatch implied. Everything else
        /// unlatches and finishes in ASCII.
        /// </para>
        /// </summary>
        public List<int> Finish(int room)
        {
            var words = new List<int> { LatchC40 };
            int full  = AlignedTriples();

            for (int i = 0; i < full; i += 3) AddTriple(words, i);

            int leftover  = _values.Count - full;
            int remaining = room - words.Count;
            int fromByte  = full == 0 ? 0 : _byteOf[full - 1] + 1;

            if (leftover == 0 && remaining == 0) return words;

            if (leftover == 2 && remaining == 2 && _byteOf[full] != _byteOf[full + 1])
            {
                // Two plain values, one codeword pair, no unlatch.
                AddTriple(words, _values[full], _values[full + 1], 0);
                return words;
            }

            if (leftover == 1 && remaining == 1)
            {
                words.Add(_bytes[fromByte] + 1);
                return words;
            }

            words.Add(Unlatch);
            words.AddRange(EncodeAscii(_bytes.AsSpan(fromByte), _gs1));
            return words;
        }

        /// <summary>
        /// How many values can be written as whole triples without splitting a shifted character across
        /// the boundary — the leftover has to start on a byte, or it cannot be finished in ASCII.
        /// </summary>
        private int AlignedTriples()
        {
            int full = _values.Count / 3 * 3;
            while (full > 0 && full < _values.Count && _byteOf[full] == _byteOf[full - 1]) full -= 3;
            return full;
        }

        private void AddTriple(List<int> words, int at) =>
            AddTriple(words, _values[at], _values[at + 1], _values[at + 2]);

        private static void AddTriple(List<int> words, int a, int b, int c)
        {
            int v = 1600 * a + 40 * b + c + 1;
            words.Add(v / 256);
            words.Add(v % 256);
        }
    }

    // ── Size, padding, parity ──────────────────────────────────────────────

    private static bool TryChooseSize(int codewords, DataMatrixOptions options,
                                      out DataMatrixSize size, out string? error)
    {
        error = null;

        if (options.Size is { } forced)
        {
            if (!TryGetSize(forced.Rows, forced.Columns, out size))
            {
                error = $"{forced.Rows}×{forced.Columns} is not a Data Matrix size.";
                return false;
            }

            if (codewords > size.DataCodewords)
            {
                error = $"Too much data for a {size} symbol: {codewords} codewords where it holds {size.DataCodewords}.";
                return false;
            }

            return true;
        }

        DataMatrixSize? best = null;
        foreach (var candidate in Sizes)
        {
            if (options.Shape == DataMatrixShape.Square    && !candidate.IsSquare) continue;
            if (options.Shape == DataMatrixShape.Rectangle &&  candidate.IsSquare) continue;
            if (candidate.DataCodewords < codewords) continue;

            bool smaller = best is null
                        || candidate.DataCodewords < best.Value.DataCodewords
                        || (candidate.DataCodewords == best.Value.DataCodewords
                            && candidate.Rows * candidate.Columns < best.Value.Rows * best.Value.Columns);
            if (smaller) best = candidate;
        }

        if (best is null)
        {
            size  = default;
            error = options.Shape == DataMatrixShape.Rectangle
                ? $"Too much data for a rectangular Data Matrix: {codewords} codewords where the largest holds 49."
                : $"Too much data for a Data Matrix: {codewords} codewords where the largest symbol holds 1558.";
            return false;
        }

        size = best.Value;
        return true;
    }

    /// <summary>
    /// Fills the data field to the symbol's capacity: one plain pad, then pads run through the
    /// 253-state algorithm so a symbol that is mostly empty is not mostly one repeated value.
    /// </summary>
    private static void PadTo(List<int> data, int capacity)
    {
        if (data.Count >= capacity) return;

        data.Add(Pad);
        while (data.Count < capacity)
        {
            int pseudo = (149 * (data.Count + 1)) % 253 + 1;
            int value  = Pad + pseudo;
            if (value > 254) value -= 254;
            data.Add(value);
        }
    }

    /// <summary>
    /// Appends the parity. A symbol with more than one block interleaves them: block i takes every
    /// i-th data codeword, works out its own parity, and the parities are interleaved back the same way.
    /// </summary>
    private static int[] AddParity(List<int> data, DataMatrixSize size)
    {
        int blocks  = size.Blocks;
        int eccEach = size.EccCodewords / blocks;
        var result  = new int[size.DataCodewords + size.EccCodewords];
        data.CopyTo(result, 0);

        var generator = ReedSolomon.Generator(GaloisField.DataMatrix, eccEach, firstRoot: 1);

        for (int b = 0; b < blocks; b++)
        {
            var block = new List<int>();
            for (int i = b; i < size.DataCodewords; i += blocks) block.Add(data[i]);

            var parity = ReedSolomon.Parity(GaloisField.DataMatrix, block.ToArray(), generator);
            for (int p = 0; p < eccEach; p++)
                result[size.DataCodewords + b + p * blocks] = parity[p];
        }

        return result;
    }

    // ── Placement (ISO/IEC 16022 Annex F) ──────────────────────────────────

    /// <summary>
    /// Lays the codewords into the symbol, region by region, with the finder pattern around each.
    /// <para>
    /// The mapping is worked out over all the data modules as one grid, ignoring the finder borders
    /// between regions, and the finished grid is then cut up and each piece framed. That is how the
    /// standard describes it, and it is what makes a multi-region symbol read as one.
    /// </para>
    /// </summary>
    private static bool[] Place(int[] codewords, DataMatrixSize size)
    {
        int mr = size.MappingRows, mc = size.MappingColumns;
        var mapping = new bool[mr * mc];

        var placement = new Placement(mr, mc);
        placement.Run();

        for (int r = 0; r < mr; r++)
        {
            for (int c = 0; c < mc; c++)
            {
                int slot = placement[r, c];
                if (slot == 0) continue;

                int word = (slot >> 3) - 1;
                int bit  = slot & 7;
                mapping[r * mc + c] = ((codewords[word] >> bit) & 1) == 1;
            }
        }

        // The bottom-right corner the walk never reaches is fixed by the standard: two dark modules
        // on the diagonal, whenever the mapping's corner is left empty.
        if (placement.LeavesCornerUnfilled)
        {
            mapping[(mr - 1) * mc + (mc - 1)] = true;
            mapping[(mr - 2) * mc + (mc - 2)] = true;
        }

        return Frame(mapping, size);
    }

    /// <summary>Cuts the mapping into regions and draws each one's finder pattern around it.</summary>
    private static bool[] Frame(bool[] mapping, DataMatrixSize size)
    {
        var modules = new bool[size.Rows * size.Columns];
        int rr = size.RegionRows, rc = size.RegionColumns, mc = size.MappingColumns;

        for (int ry = 0; ry < size.RegionsDown; ry++)
        {
            for (int rx = 0; rx < size.RegionsAcross; rx++)
            {
                int top  = ry * (rr + 2);
                int left = rx * (rc + 2);

                for (int y = 0; y < rr + 2; y++)
                {
                    for (int x = 0; x < rc + 2; x++)
                    {
                        bool dark;
                        if (x == 0)           dark = true;                  // the solid left edge
                        else if (y == rr + 1) dark = true;                  // the solid bottom edge
                        else if (y == 0)      dark = x % 2 == 0;            // the alternating top edge
                        else if (x == rc + 1) dark = y % 2 == 1;            // the alternating right edge
                        else                  dark = mapping[(ry * rr + y - 1) * mc + rx * rc + x - 1];

                        modules[(top + y) * size.Columns + left + x] = dark;
                    }
                }
            }
        }

        return modules;
    }

    /// <summary>
    /// Annex F's placement walk, as the standard gives it. Each cell ends up holding which codeword and
    /// which of its bits it shows, packed as <c>(codeword × 8) + bit</c> with the codeword one-based —
    /// so zero means "not yet placed".
    /// </summary>
    internal sealed class Placement
    {
        private readonly int[] _cells;
        private readonly int _rows, _cols;

        public Placement(int rows, int cols)
        {
            _rows  = rows;
            _cols  = cols;
            _cells = new int[rows * cols];
        }

        public int this[int r, int c] => _cells[r * _cols + c];

        public bool LeavesCornerUnfilled => _cells[_rows * _cols - 1] == 0;

        public void Run()
        {
            int nr = _rows, nc = _cols;
            int p = 1, r = 4, c = 0;

            do
            {
                if (r == nr && c == 0)                    Corner1(p++);
                if (r == nr - 2 && c == 0 && nc % 4 != 0) Corner2(p++);
                if (r == nr - 2 && c == 0 && nc % 8 == 4) Corner3(p++);
                if (r == nr + 4 && c == 2 && nc % 8 == 0) Corner4(p++);

                do
                {
                    if (r < nr && c >= 0 && this[r, c] == 0) Utah(r, c, p++);
                    r -= 2; c += 2;
                }
                while (r >= 0 && c < nc);
                r += 1; c += 3;

                do
                {
                    if (r >= 0 && c < nc && this[r, c] == 0) Utah(r, c, p++);
                    r += 2; c -= 2;
                }
                while (r < nr && c >= 0);
                r += 3; c += 1;
            }
            while (r < nr || c < nc);
        }

        private void Bit(int r, int c, int p, int b)
        {
            if (r < 0) { r += _rows; c += 4 - ((_rows + 4) % 8); }
            if (c < 0) { c += _cols; r += 4 - ((_cols + 4) % 8); }
            _cells[r * _cols + c] = (p << 3) + b;
        }

        private void Utah(int r, int c, int p)
        {
            Bit(r - 2, c - 2, p, 7);
            Bit(r - 2, c - 1, p, 6);
            Bit(r - 1, c - 2, p, 5);
            Bit(r - 1, c - 1, p, 4);
            Bit(r - 1, c,     p, 3);
            Bit(r,     c - 2, p, 2);
            Bit(r,     c - 1, p, 1);
            Bit(r,     c,     p, 0);
        }

        private void Corner1(int p)
        {
            Bit(_rows - 1, 0,         p, 7);
            Bit(_rows - 1, 1,         p, 6);
            Bit(_rows - 1, 2,         p, 5);
            Bit(0,         _cols - 2, p, 4);
            Bit(0,         _cols - 1, p, 3);
            Bit(1,         _cols - 1, p, 2);
            Bit(2,         _cols - 1, p, 1);
            Bit(3,         _cols - 1, p, 0);
        }

        private void Corner2(int p)
        {
            Bit(_rows - 3, 0,         p, 7);
            Bit(_rows - 2, 0,         p, 6);
            Bit(_rows - 1, 0,         p, 5);
            Bit(0,         _cols - 4, p, 4);
            Bit(0,         _cols - 3, p, 3);
            Bit(0,         _cols - 2, p, 2);
            Bit(0,         _cols - 1, p, 1);
            Bit(1,         _cols - 1, p, 0);
        }

        private void Corner3(int p)
        {
            Bit(_rows - 3, 0,         p, 7);
            Bit(_rows - 2, 0,         p, 6);
            Bit(_rows - 1, 0,         p, 5);
            Bit(0,         _cols - 2, p, 4);
            Bit(0,         _cols - 1, p, 3);
            Bit(1,         _cols - 1, p, 2);
            Bit(2,         _cols - 1, p, 1);
            Bit(3,         _cols - 1, p, 0);
        }

        private void Corner4(int p)
        {
            Bit(_rows - 1, 0,         p, 7);
            Bit(_rows - 1, _cols - 1, p, 6);
            Bit(0,         _cols - 3, p, 5);
            Bit(0,         _cols - 2, p, 4);
            Bit(0,         _cols - 1, p, 3);
            Bit(1,         _cols - 3, p, 2);
            Bit(1,         _cols - 2, p, 1);
            Bit(1,         _cols - 1, p, 0);
        }
    }
}

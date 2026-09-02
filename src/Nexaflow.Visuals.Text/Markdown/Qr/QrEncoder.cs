using System;
using System.Collections.Generic;
using System.Text;
using Nexaflow.Visuals.Text.Markdown.Matrix;

namespace Nexaflow.Visuals.Text.Markdown.Qr;

/// <summary>
/// Encodes a string into a QR symbol (ISO/IEC 18004 model 2) ΓÇö versions 1ΓÇô40, every error-correction
/// level, and the numeric / alphanumeric / byte modes.
///
/// <para>
/// Written here rather than taken from a package because the whole job is arithmetic over a byte
/// array: no IO, no platform, nothing to keep current. It is deliberately free of WPF so a matrix can
/// be produced and asserted on without a UI thread ΓÇö <c>WpfQrRenderer</c> is the only thing that
/// knows how to paint one.
/// </para>
///
/// <para>
/// The pipeline is the standard one: choose a mode for the text, pick the smallest version whose data
/// capacity holds it, pad to that capacity, split into blocks and append ReedΓÇôSolomon codewords,
/// interleave the blocks, lay the bits into the grid around the function patterns, then try all eight
/// masks and keep whichever one the penalty rules like best.
/// </para>
/// </summary>
public static class QrEncoder
{
    public const int MinVersion = 1;
    public const int MaxVersion = 40;

    /// <summary>Characters encodable in alphanumeric mode, in their code order.</summary>
    private const string AlphanumericCharset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    // ΓöÇΓöÇ Public API ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    /// <summary>Encodes <paramref name="text"/>, choosing the smallest version that fits.</summary>
    /// <exception cref="ArgumentException">The text is too long for a version-40 symbol at this level.</exception>
    public static QrMatrix Encode(string text, QrErrorCorrection ecl = QrErrorCorrection.Medium)
        => TryEncode(text, ecl, out var matrix, out var error)
            ? matrix!
            : throw new ArgumentException(error, nameof(text));

    /// <summary>
    /// The non-throwing form. Returns false with a reader-facing <paramref name="error"/> when the
    /// payload cannot be encoded ΓÇö which for a QR code means only one thing: it does not fit.
    /// </summary>
    public static bool TryEncode(string text, QrErrorCorrection ecl, out QrMatrix? matrix, out string? error)
    {
        matrix = null;
        error  = null;
        text ??= string.Empty;

        var mode     = ModeFor(text);
        var dataBits = EncodeData(text, mode);
        int chars    = mode == Mode.Byte ? Encoding.UTF8.GetByteCount(text) : text.Length;

        int version = 0;
        for (int v = MinVersion; v <= MaxVersion; v++)
        {
            if (4 + CharCountBits(mode, v) + dataBits.Count <= NumDataCodewords(v, ecl) * 8)
            {
                version = v;
                break;
            }
        }

        if (version == 0)
        {
            error = $"Too much data for a QR code: {chars} {(mode == Mode.Byte ? "bytes" : "characters")} "
                  + $"exceeds what a version-40 symbol holds at error correction {ecl}. "
                  + "Shorten the content, or lower `ec`.";
            return false;
        }

        // Header, payload, terminator, then pad out to a whole number of codewords.
        int capacity = NumDataCodewords(version, ecl) * 8;
        var bits     = new List<bool>(capacity);
        AppendBits(bits, ModeIndicator(mode), 4);
        AppendBits(bits, chars, CharCountBits(mode, version));
        bits.AddRange(dataBits);

        AppendBits(bits, 0, Math.Min(4, capacity - bits.Count));
        AppendBits(bits, 0, (8 - bits.Count % 8) % 8);
        for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11)
            AppendBits(bits, pad, 8);

        var dataCodewords = new byte[bits.Count / 8];
        for (int i = 0; i < bits.Count; i++)
            if (bits[i])
                dataCodewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

        matrix = Build(version, ecl, AddEccAndInterleave(dataCodewords, version, ecl));
        return true;
    }

    // ΓöÇΓöÇ Mode selection and data encoding ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    private enum Mode { Numeric, Alphanumeric, Byte }

    /// <summary>
    /// The narrowest mode that covers the whole string. Splitting a string into per-mode segments can
    /// occasionally shave a version off, but only for mixed text; every payload this block builds is
    /// uniform enough that the extra machinery would not pay for itself.
    /// </summary>
    private static Mode ModeFor(string text)
    {
        if (text.Length == 0) return Mode.Byte;

        bool numeric = true, alnum = true;
        foreach (char c in text)
        {
            if (c is < '0' or > '9') numeric = false;
            if (AlphanumericCharset.IndexOf(c) < 0) { alnum = false; break; }
        }
        return numeric ? Mode.Numeric : alnum ? Mode.Alphanumeric : Mode.Byte;
    }

    private static int ModeIndicator(Mode mode) => mode switch
    {
        Mode.Numeric      => 0b0001,
        Mode.Alphanumeric => 0b0010,
        _                 => 0b0100,
    };

    /// <summary>Width of the character-count field, which widens with the version.</summary>
    private static int CharCountBits(Mode mode, int version) => mode switch
    {
        Mode.Numeric      => version < 10 ? 10 : version < 27 ? 12 : 14,
        Mode.Alphanumeric => version < 10 ?  9 : version < 27 ? 11 : 13,
        _                 => version < 10 ?  8 : 16,
    };

    private static List<bool> EncodeData(string text, Mode mode)
    {
        var bits = new List<bool>();
        switch (mode)
        {
            case Mode.Numeric:
                // Three digits pack into ten bits; a tail of two takes seven, one takes four.
                for (int i = 0; i < text.Length;)
                {
                    int n = Math.Min(3, text.Length - i);
                    AppendBits(bits, int.Parse(text.Substring(i, n)), n * 3 + 1);
                    i += n;
                }
                break;

            case Mode.Alphanumeric:
                // Pairs pack into eleven bits (base 45); an odd tail character takes six.
                for (int i = 0; i < text.Length;)
                {
                    if (i + 1 < text.Length)
                    {
                        AppendBits(bits, AlphanumericCharset.IndexOf(text[i]) * 45
                                       + AlphanumericCharset.IndexOf(text[i + 1]), 11);
                        i += 2;
                    }
                    else
                    {
                        AppendBits(bits, AlphanumericCharset.IndexOf(text[i]), 6);
                        i++;
                    }
                }
                break;

            default:
                foreach (byte b in Encoding.UTF8.GetBytes(text))
                    AppendBits(bits, b, 8);
                break;
        }
        return bits;
    }

    private static void AppendBits(List<bool> bits, int value, int length)
    {
        for (int i = length - 1; i >= 0; i--)
            bits.Add(((value >>> i) & 1) != 0);
    }

    // ΓöÇΓöÇ Capacity ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    /// <summary>
    /// Data modules available at this version ΓÇö the whole grid less the function patterns, which grow
    /// with it (one more alignment ring every seven versions, and version information from 7 up).
    /// </summary>
    private static int RawDataModules(int version)
    {
        int result = (16 * version + 128) * version + 64;
        if (version >= 2)
        {
            int numAlign = version / 7 + 2;
            result -= (25 * numAlign - 10) * numAlign - 55;
            if (version >= 7) result -= 36;
        }
        return result;
    }

    private static int NumDataCodewords(int version, QrErrorCorrection ecl) =>
        RawDataModules(version) / 8
        - EccCodewordsPerBlock[(int)ecl][version - 1] * NumEccBlocks[(int)ecl][version - 1];

    /// <summary>
    /// How this version and level split their codewords: the total, the number of blocks the data is
    /// spread over, and each block's ECC length.
    /// <para>
    /// Internal for the tests, which read a finished symbol back and need the same de-interleaving that
    /// produced it. The block tables themselves are checked from the outside instead — the published
    /// capacity of a version tells you whether both of them are right.
    /// </para>
    /// </summary>
    internal static (int TotalCodewords, int Blocks, int EccPerBlock) BlockLayout(int version, QrErrorCorrection ecl) =>
    (RawDataModules(version) / 8,
     NumEccBlocks[(int)ecl][version - 1],
     EccCodewordsPerBlock[(int)ecl][version - 1]);

    /// <summary>Data codewords available at this version and level — what a payload is measured against.</summary>
    internal static int DataCodewords(int version, QrErrorCorrection ecl) => NumDataCodewords(version, ecl);

    // ΓöÇΓöÇ ReedΓÇôSolomon over GF(256), primitive polynomial 0x11D ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    /// <summary>
    /// Splits the data into blocks, appends each block's ECC codewords, then interleaves the lot.
    /// Interleaving is what makes a burst of damage land across many blocks instead of destroying one.
    /// </summary>
    private static byte[] AddEccAndInterleave(byte[] data, int version, QrErrorCorrection ecl)
    {
        int numBlocks    = NumEccBlocks[(int)ecl][version - 1];
        int blockEccLen  = EccCodewordsPerBlock[(int)ecl][version - 1];
        int rawCodewords = RawDataModules(version) / 8;

        // The blocks divide as evenly as they can; the leftover codewords go to the later blocks.
        int numShortBlocks = numBlocks - rawCodewords % numBlocks;
        int shortBlockLen  = rawCodewords / numBlocks;

        // QR's field and its generator start at g⁰ — the shared codec's defaults, because QR was its
        // first user. Data Matrix and PDF417 both say otherwise, and say so at their call sites.
        var generator = ReedSolomon.Generator(GaloisField.Qr, blockEccLen);
        var blocks    = new byte[numBlocks][];

        for (int i = 0, k = 0; i < numBlocks; i++)
        {
            int datLen = shortBlockLen - blockEccLen + (i < numShortBlocks ? 0 : 1);
            var dat    = data.AsSpan(k, datLen);
            k += datLen;

            var block = new byte[shortBlockLen + 1];
            dat.CopyTo(block);

            var parity = ReedSolomon.Parity(GaloisField.Qr, Widen(dat), generator);
            for (int p = 0; p < blockEccLen; p++) block[block.Length - blockEccLen + p] = (byte)parity[p];

            blocks[i] = block;
        }

        var result = new byte[rawCodewords];
        for (int i = 0, k = 0; i < blocks[0].Length; i++)
        {
            for (int j = 0; j < numBlocks; j++)
            {
                // A short block has no codeword at the final data index — step over its hole.
                if (i != shortBlockLen - blockEccLen || j >= numShortBlocks)
                    result[k++] = blocks[j][i];
            }
        }
        return result;
    }

    /// <summary>Codewords as the field takes them. QR's are bytes; the codec is written for fields wider than a byte.</summary>
    private static int[] Widen(ReadOnlySpan<byte> bytes)
    {
        var result = new int[bytes.Length];
        for (int i = 0; i < bytes.Length; i++) result[i] = bytes[i];
        return result;
    }

    // ΓöÇΓöÇ Grid construction ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    /// <summary>The grid under construction: the modules, and which of them data may not overwrite.</summary>
    private sealed class Grid
    {
        internal Grid(int size)
        {
            Size       = size;
            Modules    = new bool[size * size];
            IsFunction = new bool[size * size];
        }

        internal int Size { get; }
        internal bool[] Modules { get; }
        internal bool[] IsFunction { get; }

        internal bool Get(int x, int y) => Modules[y * Size + x];
        internal void Set(int x, int y, bool dark) => Modules[y * Size + x] = dark;

        /// <summary>Sets a module data must not touch. Coordinates off the grid are ignored, which is
        /// what lets a finder pattern be drawn as a plain 9├ù9 square straddling the edge.</summary>
        internal void SetFunction(int x, int y, bool dark)
        {
            if (x < 0 || x >= Size || y < 0 || y >= Size) return;
            Modules[y * Size + x]    = dark;
            IsFunction[y * Size + x] = true;
        }
    }

    private static QrMatrix Build(int version, QrErrorCorrection ecl, byte[] codewords)
    {
        var grid = new Grid(version * 4 + 17);

        DrawFunctionPatterns(grid, version, ecl);
        DrawCodewords(grid, codewords);

        // Every mask is tried and the penalty rules pick the one that reads most cleanly. The format
        // bits name the mask, so they are redrawn for each candidate before it is scored.
        int bestMask = 0;
        long bestPenalty = long.MaxValue;
        for (int mask = 0; mask < 8; mask++)
        {
            ApplyMask(grid, mask);
            DrawFormatBits(grid, ecl, mask);

            long penalty = PenaltyScore(grid);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                bestMask    = mask;
            }

            ApplyMask(grid, mask);   // XOR is its own inverse
        }

        ApplyMask(grid, bestMask);
        DrawFormatBits(grid, ecl, bestMask);

        return new QrMatrix(version, ecl, bestMask, grid.Modules);
    }

    private static void DrawFunctionPatterns(Grid grid, int version, QrErrorCorrection ecl)
    {
        int size = grid.Size;

        // Timing patterns: the alternating row and column a reader counts modules against.
        for (int i = 0; i < size; i++)
        {
            grid.SetFunction(6, i, i % 2 == 0);
            grid.SetFunction(i, 6, i % 2 == 0);
        }

        // Finder patterns and their separators, in three corners.
        DrawFinder(grid, 3, 3);
        DrawFinder(grid, size - 4, 3);
        DrawFinder(grid, 3, size - 4);

        // Alignment patterns everywhere the rings cross, bar the three finder corners.
        var positions = AlignmentPatternPositions(version);
        for (int i = 0; i < positions.Count; i++)
        {
            for (int j = 0; j < positions.Count; j++)
            {
                bool corner = (i == 0 && j == 0)
                           || (i == 0 && j == positions.Count - 1)
                           || (i == positions.Count - 1 && j == 0);
                if (!corner) DrawAlignment(grid, positions[i], positions[j]);
            }
        }

        // Reserve the format and version areas; the format bits are drawn for real once a mask is picked.
        DrawFormatBits(grid, ecl, 0);
        DrawVersionBits(grid, version);
    }

    private static void DrawFinder(Grid grid, int cx, int cy)
    {
        for (int dy = -4; dy <= 4; dy++)
        {
            for (int dx = -4; dx <= 4; dx++)
            {
                int dist = Math.Max(Math.Abs(dx), Math.Abs(dy));   // Chebyshev: concentric squares
                grid.SetFunction(cx + dx, cy + dy, dist != 2 && dist != 4);
            }
        }
    }

    private static void DrawAlignment(Grid grid, int cx, int cy)
    {
        for (int dy = -2; dy <= 2; dy++)
            for (int dx = -2; dx <= 2; dx++)
                grid.SetFunction(cx + dx, cy + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
    }

    /// <summary>Row/column centres of the alignment patterns ΓÇö evenly spaced, always spanning 6 to sizeΓêÆ7.</summary>
    private static List<int> AlignmentPatternPositions(int version)
    {
        if (version == 1) return [];

        int numAlign = version / 7 + 2;
        int step = version == 32
            ? 26
            : (version * 4 + numAlign * 2 + 1) / (numAlign * 2 - 2) * 2;

        var result = new List<int>();
        for (int i = 0, pos = version * 4 + 10; i < numAlign - 1; i++, pos -= step)
            result.Insert(0, pos);
        result.Insert(0, 6);
        return result;
    }

    /// <summary>
    /// The 15-bit format information ΓÇö error-correction level and mask ΓÇö protected by a BCH(15,5) code
    /// and masked with 0x5412 so it is never all zeros. Written twice, so losing one corner survives.
    /// </summary>
    private static void DrawFormatBits(Grid grid, QrErrorCorrection ecl, int mask)
    {
        int size = grid.Size;
        int data = (FormatBits[(int)ecl] << 3) | mask;

        int rem = data;
        for (int i = 0; i < 10; i++)
            rem = (rem << 1) ^ ((rem >>> 9) * 0x537);

        int bits = ((data << 10) | rem) ^ 0x5412;

        // First copy: around the top-left finder.
        for (int i = 0; i <= 5; i++) grid.SetFunction(8, i, Bit(bits, i));
        grid.SetFunction(8, 7, Bit(bits, 6));
        grid.SetFunction(8, 8, Bit(bits, 7));
        grid.SetFunction(7, 8, Bit(bits, 8));
        for (int i = 9; i < 15; i++) grid.SetFunction(14 - i, 8, Bit(bits, i));

        // Second copy: split between the other two finders.
        for (int i = 0; i < 8; i++)  grid.SetFunction(size - 1 - i, 8, Bit(bits, i));
        for (int i = 8; i < 15; i++) grid.SetFunction(8, size - 15 + i, Bit(bits, i));

        grid.SetFunction(8, size - 8, true);   // the always-dark module
    }

    /// <summary>The 18-bit version information, BCH(18,6) protected. Only versions 7 and up carry it.</summary>
    private static void DrawVersionBits(Grid grid, int version)
    {
        if (version < 7) return;

        int rem = version;
        for (int i = 0; i < 12; i++)
            rem = (rem << 1) ^ ((rem >>> 11) * 0x1F25);

        int bits = (version << 12) | rem;
        for (int i = 0; i < 18; i++)
        {
            bool bit = Bit(bits, i);
            int a = grid.Size - 11 + i % 3;
            int b = i / 3;
            grid.SetFunction(a, b, bit);
            grid.SetFunction(b, a, bit);
        }
    }

    private static bool Bit(int value, int index) => ((value >>> index) & 1) != 0;

    /// <summary>
    /// Lays the codeword bits into the grid: upward then downward through two-module-wide columns,
    /// right to left, stepping over function modules and the vertical timing column.
    /// </summary>
    private static void DrawCodewords(Grid grid, byte[] codewords)
    {
        int size = grid.Size;
        int i = 0;   // bit index into the codewords

        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;   // the timing column is in no pair

            for (int vert = 0; vert < size; vert++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? size - 1 - vert : vert;

                    if (!grid.IsFunction[y * size + x] && i < codewords.Length * 8)
                    {
                        grid.Set(x, y, Bit(codewords[i >> 3], 7 - (i & 7)));
                        i++;
                    }
                    // Anything left over stays light ΓÇö the spec's remainder bits.
                }
            }
        }
    }

    private static void ApplyMask(Grid grid, int mask)
    {
        int size = grid.Size;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if (grid.IsFunction[y * size + x]) continue;

                bool invert = mask switch
                {
                    0 => (x + y) % 2 == 0,
                    1 => y % 2 == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (x / 3 + y / 2) % 2 == 0,
                    5 => x * y % 2 + x * y % 3 == 0,
                    6 => (x * y % 2 + x * y % 3) % 2 == 0,
                    _ => ((x + y) % 2 + x * y % 3) % 2 == 0,
                };
                if (invert) grid.Set(x, y, !grid.Get(x, y));
            }
        }
    }

    // ΓöÇΓöÇ Mask penalty rules ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    private const int PenaltyRun     = 3;    // a run of five same-coloured modules, +1 for each extra
    private const int PenaltyBlock   = 3;    // a 2├ù2 block of one colour
    private const int PenaltyFinder  = 40;   // a run that could be mistaken for a finder pattern
    private const int PenaltyBalance = 10;   // per 5% the dark/light split strays from even

    private static long PenaltyScore(Grid grid)
    {
        int size = grid.Size;
        long result = 0;

        // Rules 1 and 3, row-wise then column-wise.
        for (int y = 0; y < size; y++)
            result += LinePenalty(grid, y, horizontal: true);
        for (int x = 0; x < size; x++)
            result += LinePenalty(grid, x, horizontal: false);

        // Rule 2: solid 2├ù2 blocks.
        for (int y = 0; y < size - 1; y++)
        {
            for (int x = 0; x < size - 1; x++)
            {
                bool c = grid.Get(x, y);
                if (c == grid.Get(x + 1, y) && c == grid.Get(x, y + 1) && c == grid.Get(x + 1, y + 1))
                    result += PenaltyBlock;
            }
        }

        // Rule 4: how far the proportion of dark modules strays from half.
        int dark = 0;
        foreach (bool m in grid.Modules) if (m) dark++;

        int total = size * size;
        int k = (int)((Math.Abs(dark * 20L - total * 10L) + total - 1) / total) - 1;
        return result + (long)k * PenaltyBalance;
    }

    /// <summary>Rules 1 and 3 down one row or column: long same-colour runs, and finder-like sequences.</summary>
    private static long LinePenalty(Grid grid, int line, bool horizontal)
    {
        int size = grid.Size;
        long result = 0;

        bool runColor = false;
        int runLength = 0;
        var history = new int[7];

        for (int i = 0; i < size; i++)
        {
            bool module = horizontal ? grid.Get(i, line) : grid.Get(line, i);
            if (module == runColor)
            {
                runLength++;
                if (runLength == 5) result += PenaltyRun;
                else if (runLength > 5) result++;
            }
            else
            {
                AddRunToHistory(runLength, history, size);
                if (!runColor) result += CountFinderLikePatterns(history) * PenaltyFinder;
                runColor  = module;
                runLength = 1;
            }
        }

        return result + TerminateAndCount(runColor, runLength, history, size) * PenaltyFinder;
    }

    /// <summary>Pushes a finished run onto the seven-deep history, padding the first run with the light border.</summary>
    private static void AddRunToHistory(int runLength, int[] history, int size)
    {
        if (history[0] == 0) runLength += size;   // the quiet zone counts as light modules
        Array.Copy(history, 0, history, 1, history.Length - 1);
        history[0] = runLength;
    }

    /// <summary>Counts the 1:1:3:1:1 finder-like runs (with their four-wide light margin) ending at this history.</summary>
    private static int CountFinderLikePatterns(int[] history)
    {
        int n = history[1];
        bool core = n > 0
                 && history[2] == n && history[3] == n * 3
                 && history[4] == n && history[5] == n;

        return (core && history[0] >= n * 4 && history[6] >= n ? 1 : 0)
             + (core && history[6] >= n * 4 && history[0] >= n ? 1 : 0);
    }

    private static int TerminateAndCount(bool runColor, int runLength, int[] history, int size)
    {
        if (runColor)
        {
            AddRunToHistory(runLength, history, size);
            runLength = 0;
        }
        runLength += size;   // the light border past the edge
        AddRunToHistory(runLength, history, size);
        return CountFinderLikePatterns(history);
    }

    // ΓöÇΓöÇ Specification tables (ISO/IEC 18004), indexed [level][version ΓêÆ 1] ΓöÇ

    /// <summary>Format-information bits for L / M / Q / H ΓÇö deliberately not the enum's own order.</summary>
    private static readonly int[] FormatBits = [1, 0, 3, 2];

    private static readonly int[][] EccCodewordsPerBlock =
    [
        // L
        [7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28,
         28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        // M
        [10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26,
         26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28],
        // Q
        [13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30,
         28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        // H
        [17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28,
         30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
    ];

    private static readonly int[][] NumEccBlocks =
    [
        // L
        [1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8,
         8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25],
        // M
        [1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16,
         17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49],
        // Q
        [1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20,
         23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68],
        // H
        [1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25,
         25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81],
    ];
}

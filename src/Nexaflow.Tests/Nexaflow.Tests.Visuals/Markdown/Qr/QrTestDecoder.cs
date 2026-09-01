using Nexaflow.Visuals.Text.Markdown.Qr;
using System.IO;

namespace Nexaflow.Tests.Visuals.Markdown.Qr;

/// <summary>
/// Reads a finished <see cref="QrMatrix"/> back to the string it encodes ΓÇö a scanner without the
/// camera.
///
/// <para>
/// It exists because nothing else can tell you a QR code is right. Asserting the module count, or
/// that a border came back, says only that something square was produced; the questions that matter ΓÇö
/// did the bits land in the zigzag in the right order, is the chosen mask really the one the format
/// information names, is the ReedΓÇôSolomon parity actually valid ΓÇö are answerable only by undoing the
/// encode and checking what falls out.
/// </para>
///
/// <para>
/// Where it can, it takes a different route to the same answer rather than mirroring the encoder,
/// because two copies of one mistake agree with each other. The Galois-field arithmetic here is
/// log/antilog table lookup against the encoder's carry-less shift-multiply; the parity check is
/// polynomial <em>evaluation</em> (every syndrome must vanish) against the encoder's long division;
/// the format information is decoded by re-deriving all thirty-two published code words and matching.
/// What it does share is the block-layout table, read through
/// <see cref="QrEncoder.BlockLayout"/> ΓÇö those tables are pinned from outside instead, by
/// <c>QrEncoderTests.Capacity_MatchesPublishedByteModeLimits</c>.
/// </para>
/// </summary>
internal static class QrTestDecoder
{
    // ΓöÇΓöÇ GF(256) by log/antilog tables, primitive polynomial 0x11D ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    private static readonly byte[] Exp = new byte[512];
    private static readonly byte[] Log = new byte[256];

    static QrTestDecoder()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            Exp[i] = (byte)x;
            Log[x] = (byte)i;
            x <<= 1;
            if (x >= 256) x ^= 0x11D;
        }
        for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
    }

    private static int Mul(int a, int b) => a == 0 || b == 0 ? 0 : Exp[Log[a] + Log[b]];

    // ΓöÇΓöÇ Decode ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    /// <summary>
    /// Decodes <paramref name="matrix"/>, throwing with a specific reason at whichever stage first
    /// fails to make sense.
    /// </summary>
    internal static string Decode(QrMatrix matrix)
    {
        int size    = matrix.Size;
        int version = matrix.Version;

        var (ecl, mask) = ReadFormatInformation(matrix);
        if (ecl != matrix.ErrorCorrection)
            throw new InvalidDataException(
                $"The format information says error correction {ecl}, the symbol says {matrix.ErrorCorrection}.");
        if (mask != matrix.Mask)
            throw new InvalidDataException(
                $"The format information says mask {mask}, the symbol says {matrix.Mask}.");

        if (version >= 7 && ReadVersionInformation(matrix) != version)
            throw new InvalidDataException(
                $"The version information does not read back as version {version}.");

        var function = FunctionMap(version);
        var codewords = ReadCodewords(matrix, function, mask);
        var data = DeinterleaveAndCheckParity(codewords, version, ecl);

        return ReadPayload(data, version);
    }

    // ΓöÇΓöÇ Format and version information ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    /// <summary>
    /// Reads the first copy of the format information and identifies it by matching against every
    /// (level, mask) code word ΓÇö the standard's own thirty-two, re-derived here by plain polynomial
    /// long division rather than by the encoder's shift loop.
    /// </summary>
    private static (QrErrorCorrection Ecl, int Mask) ReadFormatInformation(QrMatrix m)
    {
        int bits = 0;
        for (int i = 0; i <= 5; i++)  bits |= (m[8, i] ? 1 : 0) << i;
        bits |= (m[8, 7] ? 1 : 0) << 6;
        bits |= (m[8, 8] ? 1 : 0) << 7;
        bits |= (m[7, 8] ? 1 : 0) << 8;
        for (int i = 9; i < 15; i++) bits |= (m[14 - i, 8] ? 1 : 0) << i;

        for (int level = 0; level < 4; level++)
        {
            for (int mask = 0; mask < 8; mask++)
            {
                if (FormatCodeWord(LevelBits[level], mask) == bits)
                    return ((QrErrorCorrection)level, mask);
            }
        }

        throw new InvalidDataException($"The format information ({bits:X4}) is not a valid code word.");
    }

    /// <summary>Format bits per level, in <see cref="QrErrorCorrection"/> order: L, M, Q, H.</summary>
    private static readonly int[] LevelBits = [1, 0, 3, 2];

    /// <summary>BCH(15,5) over the five data bits, masked with 0x5412 ΓÇö as long division.</summary>
    internal static int FormatCodeWord(int levelBits, int mask)
    {
        int data = (levelBits << 3) | mask;

        int value = data << 10;
        for (int bit = 14; bit >= 10; bit--)
            if (((value >> bit) & 1) != 0)
                value ^= 0x537 << (bit - 10);

        return ((data << 10) | value) ^ 0x5412;
    }

    /// <summary>Reads the bottom-left copy of the version information and strips its BCH(18,6) parity.</summary>
    private static int ReadVersionInformation(QrMatrix m)
    {
        int bits = 0;
        for (int i = 0; i < 18; i++)
            bits |= (m[m.Size - 11 + i % 3, i / 3] ? 1 : 0) << i;

        int version = bits >> 12;

        int value = version << 12;
        for (int bit = 17; bit >= 12; bit--)
            if (((value >> bit) & 1) != 0)
                value ^= 0x1F25 << (bit - 12);

        return ((version << 12) | value) == bits ? version : -1;
    }

    // ΓöÇΓöÇ The grid ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    /// <summary>
    /// Which modules carry no data, laid out from the specification rather than copied: finder
    /// patterns with their separators, the two timing lines, the alignment grid, both format copies
    /// and ΓÇö from version 7 ΓÇö the version blocks.
    /// </summary>
    private static bool[] FunctionMap(int version)
    {
        int size = version * 4 + 17;
        var map  = new bool[size * size];

        void Mark(int x, int y)
        {
            if (x >= 0 && x < size && y >= 0 && y < size) map[y * size + x] = true;
        }

        foreach (var (cx, cy) in new[] { (3, 3), (size - 4, 3), (3, size - 4) })
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                    Mark(cx + dx, cy + dy);

        for (int i = 0; i < size; i++) { Mark(6, i); Mark(i, 6); }

        var centres = AlignmentPatternCentres(version);
        for (int i = 0; i < centres.Count; i++)
        {
            for (int j = 0; j < centres.Count; j++)
            {
                bool corner = (i == 0 && j == 0)
                           || (i == 0 && j == centres.Count - 1)
                           || (i == centres.Count - 1 && j == 0);
                if (corner) continue;

                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        Mark(centres[i] + dx, centres[j] + dy);
            }
        }

        for (int i = 0; i <= 5; i++)  Mark(8, i);
        Mark(8, 7); Mark(8, 8); Mark(7, 8);
        for (int i = 9; i < 15; i++)  Mark(14 - i, 8);
        for (int i = 0; i < 8; i++)   Mark(size - 1 - i, 8);
        for (int i = 8; i < 15; i++)  Mark(8, size - 15 + i);
        Mark(8, size - 8);

        if (version >= 7)
        {
            for (int i = 0; i < 18; i++)
            {
                int a = size - 11 + i % 3, b = i / 3;
                Mark(a, b);
                Mark(b, a);
            }
        }

        return map;
    }

    /// <summary>Alignment-pattern centres for a version. Pinned against the published rows by
    /// <c>QrEncoderTests.AlignmentPatterns_SitAtThePublishedCentres</c>.</summary>
    internal static List<int> AlignmentPatternCentres(int version)
    {
        if (version == 1) return [];

        int count = version / 7 + 2;
        int step  = version == 32 ? 26 : (version * 4 + count * 2 + 1) / (count * 2 - 2) * 2;

        var result = new List<int>();
        for (int i = 0, pos = version * 4 + 10; i < count - 1; i++, pos -= step)
            result.Insert(0, pos);
        result.Insert(0, 6);
        return result;
    }

    /// <summary>Walks the zigzag, undoing the mask as it goes, and gathers the codewords back up.</summary>
    private static byte[] ReadCodewords(QrMatrix m, bool[] function, int mask)
    {
        int size = m.Size;
        var bits = new List<bool>(size * size);

        for (int right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;

            for (int vert = 0; vert < size; vert++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int x = right - j;
                    bool upward = ((right + 1) & 2) == 0;
                    int y = upward ? size - 1 - vert : vert;

                    if (function[y * size + x]) continue;
                    bits.Add(m[x, y] ^ Masked(x, y, mask));
                }
            }
        }

        var codewords = new byte[bits.Count / 8];
        for (int i = 0; i < codewords.Length * 8; i++)
            if (bits[i])
                codewords[i >> 3] |= (byte)(1 << (7 - (i & 7)));

        return codewords;
    }

    private static bool Masked(int x, int y, int mask) => mask switch
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

    // ΓöÇΓöÇ Blocks and parity ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    /// <summary>
    /// Undoes the interleave, then checks each block's parity by evaluating it at the generator's
    /// roots. A valid block is a multiple of the generator polynomial, so every one of those
    /// evaluations must come out zero ΓÇö an independent statement of the same property the encoder
    /// established by division.
    /// </summary>
    private static byte[] DeinterleaveAndCheckParity(byte[] codewords, int version, QrErrorCorrection ecl)
    {
        var (total, numBlocks, eccLen) = QrEncoder.BlockLayout(version, ecl);

        if (codewords.Length != total)
            throw new InvalidDataException($"Read {codewords.Length} codewords, expected {total}.");

        int shortLen = total / numBlocks;
        int numShort = numBlocks - total % numBlocks;

        var blocks = new byte[numBlocks][];
        for (int i = 0; i < numBlocks; i++)
            blocks[i] = new byte[shortLen + (i < numShort ? 0 : 1)];

        // The interleave visits every block at each position, stepping over the short blocks' missing
        // last data codeword.
        for (int i = 0, k = 0; i <= shortLen; i++)
        {
            for (int j = 0; j < numBlocks; j++)
            {
                if (i == shortLen - eccLen && j < numShort) continue;
                int index = j < numShort && i > shortLen - eccLen ? i - 1 : i;
                blocks[j][index] = codewords[k++];
            }
        }

        var data = new List<byte>(total);
        for (int j = 0; j < numBlocks; j++)
        {
            for (int i = 0; i < eccLen; i++)
            {
                int syndrome = 0;
                foreach (byte b in blocks[j])
                    syndrome = Mul(syndrome, Exp[i]) ^ b;      // Horner at the root 2^i

                if (syndrome != 0)
                    throw new InvalidDataException(
                        $"Block {j} fails its ReedΓÇôSolomon parity: syndrome {i} is {syndrome}, not 0.");
            }

            data.AddRange(blocks[j].AsSpan(0, blocks[j].Length - eccLen).ToArray());
        }

        return [.. data];
    }

    // ΓöÇΓöÇ Bit stream ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    /// <summary>Reads the mode indicator, the character count and the payload back out of the data codewords.</summary>
    private static string ReadPayload(byte[] data, int version)
    {
        int position = 0;

        int Take(int count)
        {
            int value = 0;
            for (int i = 0; i < count; i++, position++)
            {
                if (position >= data.Length * 8)
                    throw new InvalidDataException("The bit stream ran out mid-value.");
                value = (value << 1) | ((data[position >> 3] >> (7 - (position & 7))) & 1);
            }
            return value;
        }

        int mode = Take(4);
        return mode switch
        {
            0b0001 => ReadNumeric(Take, Take(version < 10 ? 10 : version < 27 ? 12 : 14)),
            0b0010 => ReadAlphanumeric(Take, Take(version < 10 ? 9 : version < 27 ? 11 : 13)),
            0b0100 => ReadBytes(Take, Take(version < 10 ? 8 : 16)),
            _      => throw new InvalidDataException($"Mode indicator {mode:X} is not one this encoder emits."),
        };
    }

    private static string ReadNumeric(Func<int, int> take, int count)
    {
        var sb = new System.Text.StringBuilder(count);
        for (int i = 0; i < count;)
        {
            int n = Math.Min(3, count - i);
            sb.Append(take(n * 3 + 1).ToString().PadLeft(n, '0'));
            i += n;
        }
        return sb.ToString();
    }

    private const string AlphanumericCharset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    private static string ReadAlphanumeric(Func<int, int> take, int count)
    {
        var sb = new System.Text.StringBuilder(count);
        for (int i = 0; i < count;)
        {
            if (i + 1 < count)
            {
                int pair = take(11);
                sb.Append(AlphanumericCharset[pair / 45]).Append(AlphanumericCharset[pair % 45]);
                i += 2;
            }
            else
            {
                sb.Append(AlphanumericCharset[take(6)]);
                i++;
            }
        }
        return sb.ToString();
    }

    private static string ReadBytes(Func<int, int> take, int count)
    {
        var bytes = new byte[count];
        for (int i = 0; i < count; i++) bytes[i] = (byte)take(8);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}

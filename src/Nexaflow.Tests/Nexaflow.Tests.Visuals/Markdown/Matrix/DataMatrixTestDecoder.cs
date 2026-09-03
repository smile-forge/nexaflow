using System;
using System.Collections.Generic;
using System.Text;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.DataMatrix;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// A Data Matrix reader without the camera: finds the size, strips the finder patterns, reads the
/// codewords back out of the mapping, checks every Reed–Solomon syndrome and decodes the data.
///
/// <para>
/// Where it can it takes a different route to the same answer as the encoder. Parity is checked by
/// evaluating the received polynomial at each root rather than by re-dividing — two copies of one
/// mistake agree with each other, and evaluation and division are not copies. The one thing it shares
/// is the placement walk, which is a transcription of the standard's annex with no second derivation
/// to take; <see cref="DataMatrixEncoderTests"/> pins that with invariants and a golden vector instead.
/// </para>
/// </summary>
internal static class DataMatrixTestDecoder
{
    internal sealed record Decoded(string Text, int[] Codewords, bool Gs1, DataMatrixMacro Macro, int? Eci, string Encodation);

    internal static Decoded Decode(IModuleMatrix matrix)
    {
        if (!DataMatrixEncoder.TryGetSize(matrix.Height, matrix.Width, out var size))
            throw new InvalidOperationException($"{matrix.Height}×{matrix.Width} is not a Data Matrix size");

        CheckFinderPatterns(matrix, size);

        var mapping   = Unframe(matrix, size);
        var codewords = ReadCodewords(mapping, size);

        CheckParity(codewords, size);

        var data = codewords.AsSpan(0, size.DataCodewords).ToArray();
        return ReadData(data, codewords);
    }

    // ── The symbol's structure ─────────────────────────────────────────────

    /// <summary>Every region must carry its L and its alternating edge, or a reader could not find it.</summary>
    private static void CheckFinderPatterns(IModuleMatrix m, DataMatrixSize size)
    {
        int rr = size.RegionRows, rc = size.RegionColumns;

        for (int ry = 0; ry < size.RegionsDown; ry++)
        for (int rx = 0; rx < size.RegionsAcross; rx++)
        {
            int top = ry * (rr + 2), left = rx * (rc + 2);

            for (int y = 0; y < rr + 2; y++)
            {
                if (!m[left, top + y]) throw new InvalidOperationException($"left finder edge broken at region ({rx},{ry}) row {y}");
                bool expectRight = y % 2 == 1;
                if (m[left + rc + 1, top + y] != expectRight) throw new InvalidOperationException($"right timing edge wrong at region ({rx},{ry}) row {y}");
            }
            for (int x = 0; x < rc + 2; x++)
            {
                if (!m[left + x, top + rr + 1]) throw new InvalidOperationException($"bottom finder edge broken at region ({rx},{ry}) col {x}");
                bool expectTop = x % 2 == 0;
                if (m[left + x, top] != expectTop) throw new InvalidOperationException($"top timing edge wrong at region ({rx},{ry}) col {x}");
            }
        }
    }

    /// <summary>The data modules of every region, laid edge to edge as the placement algorithm sees them.</summary>
    private static bool[] Unframe(IModuleMatrix m, DataMatrixSize size)
    {
        int rr = size.RegionRows, rc = size.RegionColumns, mc = size.MappingColumns;
        var mapping = new bool[size.MappingRows * mc];

        for (int ry = 0; ry < size.RegionsDown; ry++)
        for (int rx = 0; rx < size.RegionsAcross; rx++)
        for (int y = 0; y < rr; y++)
        for (int x = 0; x < rc; x++)
            mapping[(ry * rr + y) * mc + rx * rc + x] = m[rx * (rc + 2) + 1 + x, ry * (rr + 2) + 1 + y];

        return mapping;
    }

    private static int[] ReadCodewords(bool[] mapping, DataMatrixSize size)
    {
        int mr = size.MappingRows, mc = size.MappingColumns;
        var placement = new DataMatrixEncoder.Placement(mr, mc);
        placement.Run();

        var codewords = new int[size.DataCodewords + size.EccCodewords];

        for (int r = 0; r < mr; r++)
        for (int c = 0; c < mc; c++)
        {
            int slot = placement[r, c];
            if (slot == 0) continue;

            if (mapping[r * mc + c]) codewords[(slot >> 3) - 1] |= 1 << (slot & 7);
        }

        return codewords;
    }

    // ── Parity ─────────────────────────────────────────────────────────────

    /// <summary>
    /// De-interleaves the blocks and evaluates each received polynomial at every root of the generator.
    /// A codeword that was produced by dividing by the generator is zero at all of its roots.
    /// </summary>
    private static void CheckParity(int[] codewords, DataMatrixSize size)
    {
        var field   = GaloisField.DataMatrix;
        int blocks  = size.Blocks;
        int eccEach = size.EccCodewords / blocks;

        for (int b = 0; b < blocks; b++)
        {
            var received = new List<int>();
            for (int i = b; i < size.DataCodewords; i += blocks) received.Add(codewords[i]);
            for (int p = 0; p < eccEach; p++) received.Add(codewords[size.DataCodewords + b + p * blocks]);

            // Roots start at g¹ for Data Matrix.
            for (int root = 1; root <= eccEach; root++)
            {
                int x = field.Exp(root);
                int y = 0;
                foreach (int coefficient in received) y = field.Add(field.Multiply(y, x), coefficient);

                if (y != 0) throw new InvalidOperationException($"block {b}: syndrome at root g^{root} is {y}, not 0");
            }
        }
    }

    // ── Data ───────────────────────────────────────────────────────────────

    private static Decoded ReadData(int[] data, int[] all)
    {
        var text  = new StringBuilder();
        var bytes = new List<byte>();
        bool gs1  = false;
        int? eci  = null;
        var macro = DataMatrixMacro.None;
        bool sawC40 = false;

        int i = 0;
        bool upperShift = false;

        while (i < data.Length)
        {
            int w = data[i++];

            if (w == 129) break;                                             // pad: the end

            if (w >= 1 && w <= 128)
            {
                bytes.Add((byte)((w - 1) + (upperShift ? 128 : 0)));
                upperShift = false;
                continue;
            }

            if (w >= 130 && w <= 229)
            {
                int pair = w - 130;
                bytes.Add((byte)('0' + pair / 10));
                bytes.Add((byte)('0' + pair % 10));
                continue;
            }

            switch (w)
            {
                case 230:                                                    // latch C40
                    sawC40 = true;
                    i = ReadC40(data, i, bytes, gs1);
                    continue;
                case 232: gs1 = true; if (bytes.Count > 0) bytes.Add(0x1D); continue;   // FNC1: first = GS1, later = separator
                case 235: upperShift = true; continue;
                case 236: macro = DataMatrixMacro.Macro05; continue;
                case 237: macro = DataMatrixMacro.Macro06; continue;
                case 241:
                    int e = data[i++];
                    eci = e - 1;
                    continue;
                default:
                    throw new InvalidOperationException($"codeword {w} at {i - 1} is not one this decoder reads");
            }
        }

        var encoding = eci == 26 ? Encoding.UTF8 : Encoding.Latin1;
        return new Decoded(encoding.GetString(bytes.ToArray()), all, gs1, macro, eci, sawC40 ? "C40" : "ASCII");
    }

    /// <summary>
    /// C40 until an unlatch, a pad, or the implicit end: one codeword left in the data field means it
    /// is ASCII and the unlatch was not spent on it.
    /// </summary>
    private static int ReadC40(int[] data, int i, List<byte> bytes, bool gs1)
    {
        int shift = 0;

        while (i < data.Length)
        {
            if (data[i] == 254) return i + 1;                                // unlatch
            if (data[i] == 129) return i;                                    // pad ends the symbol
            if (i + 1 >= data.Length) return i;                              // one left: implicit unlatch, it is ASCII

            int v = data[i] * 256 + data[i + 1] - 1;
            i += 2;

            foreach (int c in new[] { v / 1600, v / 40 % 40, v % 40 })
            {
                if (shift == 0)
                {
                    switch (c)
                    {
                        case 0: case 1: case 2: shift = c + 1; continue;
                        case 3:  bytes.Add((byte)' '); continue;
                        case >= 4 and <= 13:  bytes.Add((byte)('0' + c - 4));  continue;
                        default: bytes.Add((byte)('A' + c - 14)); continue;
                    }
                }

                switch (shift)
                {
                    case 1: bytes.Add((byte)c); break;                                   // controls
                    case 2:
                        if (c == 27) bytes.Add(0x1D);                                    // FNC1 as a separator
                        else bytes.Add((byte)"!\"#$%&'()*+,-./:;<=>?@[\\]^_"[c]);
                        break;
                    case 3: bytes.Add((byte)('a' + c - 14)); break;                      // lower case
                }
                shift = 0;
            }

            // A pad value 0 at the very end of a final pair is the standard's filler, not a shift — it
            // only appears as the third value of the last pair, where the shift it would start has
            // nothing to follow it. A dangling shift at the end is therefore dropped.
        }

        return i;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// An Aztec reader without the camera: works out which family and size the symbol is, checks the core,
/// reads the mode message back and verifies its Reed–Solomon, walks the spiral out, verifies that
/// Reed–Solomon too, undoes the bit stuffing and decodes the message.
///
/// <para>
/// Where it can it takes a different route to the same answer as the encoder. Parity is checked by
/// evaluating the received polynomial at each root rather than by re-dividing — two copies of one
/// mistake agree with each other, and evaluation and division are not copies — and the message is
/// decoded through the forward character tables rather than the reverse lookups the encoder searches.
/// The one thing it shares is the cell order, which is a transcription with no second derivation to
/// take; <see cref="AztecReferenceImageTests"/> pins that against symbols this code did not make.
/// </para>
/// </summary>
internal static class AztecTestDecoder
{
    internal sealed record Decoded(string Text, bool Compact, int Layers, int Size, int CodewordBits,
                                   int DataCodewords, int TotalCodewords, bool Gs1, int? Eci);

    internal static Decoded Decode(IModuleMatrix matrix)
    {
        if (matrix.Width != matrix.Height)
            throw new InvalidOperationException($"an Aztec symbol is square; this is {matrix.Width}×{matrix.Height}");

        int size   = matrix.Width;
        int centre = size / 2;
        bool compact = !IsFull(matrix, size, centre);
        int radius = AztecLayout.CoreRadius(compact);

        CheckCore(matrix, centre, radius);
        CheckOrientation(matrix, centre, radius);

        var (layers, dataCodewords) = ReadMode(matrix, centre, radius, compact);

        if (AztecLayout.Size(compact, layers) != size)
            throw new InvalidOperationException(
                $"the mode message says {(compact ? "compact" : "full")} {layers}, which is "
              + $"{AztecLayout.Size(compact, layers)} modules, but the symbol is {size}");

        int codewordBits = AztecEncoder.CodewordBits(layers);
        int capacity     = AztecLayout.CapacityBits(compact, layers);
        int totalWords   = capacity / codewordBits;

        if (dataCodewords > totalWords)
            throw new InvalidOperationException(
                $"the mode message claims {dataCodewords} data codewords of {totalWords}");

        var stream = AztecLayout.DataCells(compact, layers)
                                .Select(cell => matrix[cell.Col, cell.Row])
                                .ToList();

        // Whatever the codeword width did not divide is unused, and sits at the start of the spiral.
        var words = Words(stream.Skip(capacity % codewordBits).ToList(), codewordBits);
        CheckParity(Field(codewordBits), words, totalWords - dataCodewords, "message");

        var message = Unstuff(words.Take(dataCodewords).ToArray(), codewordBits);
        var (text, gs1, eci) = ReadMessage(message);

        return new Decoded(text, compact, layers, size, codewordBits,
                           dataCodewords, totalWords, gs1, eci);
    }

    // ── The core ───────────────────────────────────────────────────────────

    /// <summary>
    /// Which family this is. A full core has a light ring at radius five and a dark one at radius six,
    /// where a compact symbol has its mode message and its data — ninety-six modules that would have to
    /// come out uniform by coincidence to fool this. Below nineteen modules there is no full symbol at
    /// all, and the two families genuinely share the sizes above it, so the rings are the only answer.
    /// </summary>
    private static bool IsFull(IModuleMatrix matrix, int size, int centre) =>
        size >= 19 && Uniform(matrix, centre, 5, false) && Uniform(matrix, centre, 6, true);

    private static bool Uniform(IModuleMatrix matrix, int centre, int radius, bool dark)
    {
        foreach (var (row, col) in Ring(centre, radius))
            if (matrix[col, row] != dark) return false;
        return true;
    }

    private static void CheckCore(IModuleMatrix matrix, int centre, int radius)
    {
        for (int r = 0; r < radius; r++)
            if (!Uniform(matrix, centre, r, r % 2 == 0))
                throw new InvalidOperationException($"the core's ring at radius {r} is not uniform");
    }

    /// <summary>The corner marks, which carry three dark modules then two, one and none clockwise.</summary>
    private static void CheckOrientation(IModuleMatrix matrix, int centre, int radius)
    {
        int lo = centre - radius, hi = centre + radius;

        var corners = new (string Name, (int Row, int Col)[] Cells)[]
        {
            ("top-left",     [(lo, lo), (lo, lo + 1), (lo + 1, lo)]),
            ("top-right",    [(lo, hi), (lo, hi - 1), (lo + 1, hi)]),
            ("bottom-right", [(hi, hi), (hi, hi - 1), (hi - 1, hi)]),
            ("bottom-left",  [(hi, lo), (hi, lo + 1), (hi - 1, lo)]),
        };

        int[] expected = [3, 2, 1, 0];
        for (int i = 0; i < corners.Length; i++)
        {
            int dark = corners[i].Cells.Count(cell => matrix[cell.Col, cell.Row]);
            if (dark != expected[i])
                throw new InvalidOperationException(
                    $"the {corners[i].Name} orientation mark has {dark} dark modules, expected {expected[i]}");
        }
    }

    private static IEnumerable<(int Row, int Col)> Ring(int centre, int radius)
    {
        if (radius == 0) { yield return (centre, centre); yield break; }

        for (int d = -radius; d <= radius; d++)
        {
            yield return (centre - radius, centre + d);
            yield return (centre + radius, centre + d);
            yield return (centre + d, centre - radius);
            yield return (centre + d, centre + radius);
        }
    }

    // ── The mode message ───────────────────────────────────────────────────

    private static (int Layers, int DataCodewords) ReadMode(IModuleMatrix matrix, int centre,
                                                            int radius, bool compact)
    {
        var bits = AztecLayout.ModeCells(centre, radius, compact)
                              .Select(cell => matrix[cell.Col, cell.Row])
                              .ToList();

        int expected = compact ? 28 : 40;
        if (bits.Count != expected)
            throw new InvalidOperationException($"read {bits.Count} mode bits, expected {expected}");

        var nibbles = Words(bits, 4);
        CheckParity(GaloisField.AztecMode, nibbles, compact ? 5 : 6, "mode message");

        int described = compact ? 2 : 4;
        int word = 0;
        for (int i = 0; i < described; i++) word = word << 4 | nibbles[i];

        int countBits = compact ? 6 : 11;
        return ((word >> countBits) + 1, (word & (1 << countBits) - 1) + 1);
    }

    // ── Reed–Solomon, checked by evaluation ────────────────────────────────

    private static GaloisField Field(int codewordBits) => codewordBits switch
    {
        6  => GaloisField.Aztec6,
        8  => GaloisField.Aztec8,
        10 => GaloisField.Aztec10,
        _  => GaloisField.Aztec12,
    };

    /// <summary>
    /// A codeword sequence is valid when the polynomial it spells vanishes at every root of the
    /// generator — g¹ upwards for Aztec. Horner's rule, so no division is repeated.
    /// </summary>
    private static void CheckParity(GaloisField field, int[] words, int checkWords, string what)
    {
        for (int i = 1; i <= checkWords; i++)
        {
            int root = field.Exp(i);
            int value = 0;
            foreach (int word in words) value = field.Add(field.Multiply(value, root), word);

            if (value != 0)
                throw new InvalidOperationException($"the {what}'s syndrome at g^{i} is {value}, not zero");
        }
    }

    // ── Bits and codewords ─────────────────────────────────────────────────

    private static int[] Words(IReadOnlyList<bool> bits, int width)
    {
        if (bits.Count % width != 0)
            throw new InvalidOperationException($"{bits.Count} bits is not a whole number of {width}-bit words");

        var words = new int[bits.Count / width];
        for (int i = 0; i < words.Length; i++)
            for (int b = 0; b < width; b++)
                words[i] = words[i] << 1 | (bits[i * width + b] ? 1 : 0);

        return words;
    }

    /// <summary>
    /// Undoes the stuffing: a codeword whose leading <c>width − 1</c> bits are all the same carries a
    /// forced complementary bit at the end, which is not message content. That the forced bit really is
    /// the complement is checked rather than assumed — it is the one place a stuffing bug would hide.
    /// </summary>
    private static List<bool> Unstuff(int[] words, int width)
    {
        var bits = new List<bool>();
        int uniform = (1 << width - 1) - 1;

        foreach (int word in words)
        {
            int leading = word >> 1;
            int last    = word & 1;
            int keep    = width;

            if (leading == 0 || leading == uniform)
            {
                if (last == (leading == 0 ? 0 : 1))
                    throw new InvalidOperationException(
                        $"codeword {word:X} leads with {width - 1} equal bits but does not end with the complement");
                keep = width - 1;
            }

            for (int b = width - 1; b >= width - keep; b--) bits.Add((word >> b & 1) != 0);
        }

        return bits;
    }

    // ── The message ────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the bit stream back through the forward character tables, following the latches and shifts
    /// as a reader would.
    /// </summary>
    private static (string Text, bool Gs1, int? Eci) ReadMessage(List<bool> bits)
    {
        var bytes = new List<byte>();
        var latched = AztecCharacterSet.Upper;
        AztecCharacterSet? shifted = null;
        bool gs1 = false;
        int? eci = null;
        int at = 0;

        while (true)
        {
            var reading = shifted ?? latched;
            int width = AztecCharacterSets.Width(reading);
            shifted = null;

            if (IsPadding(bits, at) || bits.Count - at < width) break;

            int code = Take(bits, ref at, width);

            if (AztecCharacterSets.Text(reading, code) is { } text)
            {
                bytes.AddRange(Encoding.ASCII.GetBytes(text));
                continue;
            }

            if (code == AztecCharacterSets.ByteShift && reading is AztecCharacterSet.Upper
                                                     or AztecCharacterSet.Lower or AztecCharacterSet.Mixed)
            {
                int count = Take(bits, ref at, 5);
                if (count == 0) count = Take(bits, ref at, 11) + 31;
                for (int i = 0; i < count; i++) bytes.Add((byte)Take(bits, ref at, 8));
                continue;
            }

            if (reading == AztecCharacterSet.Punct && code == AztecCharacterSets.Flg)
            {
                int digits = Take(bits, ref at, 3);
                if (digits == 0) { gs1 = true; continue; }

                int value = 0;
                for (int i = 0; i < digits; i++) value = value * 10 + (Take(bits, ref at, 4) - 2);
                eci = value;
                continue;
            }

            if (code == AztecCharacterSets.PunctShift && reading != AztecCharacterSet.Punct)
            {
                shifted = AztecCharacterSet.Punct;
                continue;
            }

            switch (reading, code)
            {
                case (AztecCharacterSet.Upper, 28): latched = AztecCharacterSet.Lower; break;
                case (AztecCharacterSet.Upper, 29): latched = AztecCharacterSet.Mixed; break;
                case (AztecCharacterSet.Upper, 30): latched = AztecCharacterSet.Digit; break;
                case (AztecCharacterSet.Lower, 28): shifted = AztecCharacterSet.Upper; break;
                case (AztecCharacterSet.Lower, 29): latched = AztecCharacterSet.Mixed; break;
                case (AztecCharacterSet.Lower, 30): latched = AztecCharacterSet.Digit; break;
                case (AztecCharacterSet.Mixed, 28): latched = AztecCharacterSet.Lower; break;
                case (AztecCharacterSet.Mixed, 29): latched = AztecCharacterSet.Upper; break;
                case (AztecCharacterSet.Mixed, 30): latched = AztecCharacterSet.Punct; break;
                case (AztecCharacterSet.Punct, 31): latched = AztecCharacterSet.Upper; break;
                case (AztecCharacterSet.Digit, 14): latched = AztecCharacterSet.Upper; break;
                case (AztecCharacterSet.Digit, 15): shifted = AztecCharacterSet.Upper; break;
                default:
                    throw new InvalidOperationException($"code {code} has no meaning in {reading}");
            }
        }

        return (Encoding.UTF8.GetString([.. bytes]), gs1, eci);
    }

    /// <summary>
    /// Whether what is left is the padding a partial last codeword was filled with. Padding is ones, and
    /// there is less of it than a codeword is wide — and a real code of all ones is a byte shift, which
    /// always has at least eighteen bits behind it, so a short all-ones tail can be nothing else.
    /// </summary>
    private static bool IsPadding(List<bool> bits, int at) =>
        bits.Count - at < 12 && Enumerable.Range(at, bits.Count - at).All(i => bits[i]);

    private static int Take(List<bool> bits, ref int at, int width)
    {
        if (bits.Count - at < width)
            throw new InvalidOperationException($"the message ends mid-code, wanting {width} more bits");

        int value = 0;
        for (int i = 0; i < width; i++) value = value << 1 | (bits[at + i] ? 1 : 0);
        at += width;
        return value;
    }
}

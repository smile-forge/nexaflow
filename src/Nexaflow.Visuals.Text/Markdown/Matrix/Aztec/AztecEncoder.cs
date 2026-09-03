using System;
using System.Collections.Generic;
using System.Text;

namespace Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

/// <summary>
/// Encodes a message as an Aztec symbol: the bit stream from
/// <see cref="AztecHighLevelEncoder"/> broken into codewords, protected by Reed–Solomon, described by a
/// mode message, and laid out by <see cref="AztecLayout"/>.
///
/// <para>
/// The size is chosen rather than given. Aztec has no version table — a symbol is a core plus however
/// many layers the message needs — so the encoder tries the sizes in ascending order and takes the
/// first that holds the message with the error correction asked for. That order is why compact symbols
/// come first: at any given side length a compact symbol carries more than a full one, so the full
/// range is only reached when the compact family has run out.
/// </para>
/// </summary>
public static class AztecEncoder
{
    /// <summary>
    /// Encodes <paramref name="payload"/>, or explains why it will not fit.
    /// </summary>
    public static bool TryEncode(string payload, AztecOptions options,
                                 out AztecSymbol? symbol, out string? error)
    {
        symbol = null;
        error  = null;

        if (payload.Length == 0)
        {
            error = "There is nothing to encode.";
            return false;
        }

        if (options.ErrorCorrectionPercent is < AztecOptions.MinErrorCorrectionPercent
                                            or > AztecOptions.MaxErrorCorrectionPercent)
        {
            error = $"An error-correction level of {options.ErrorCorrectionPercent}% is outside "
                  + $"{AztecOptions.MinErrorCorrectionPercent}–{AztecOptions.MaxErrorCorrectionPercent}.";
            return false;
        }

        // The text sets cover ASCII; anything else travels as bytes through a byte shift, and UTF-8 is
        // what a reader assumes when nothing says otherwise. An author who needs it said explicitly has
        // `eci:` for that.
        byte[] bytes = Encoding.UTF8.GetBytes(payload);
        var message  = AztecHighLevelEncoder.Encode(bytes, options);

        foreach (var (compact, layers) in Candidates(options))
        {
            int codewordBits = CodewordBits(layers);
            int capacityBits = AztecLayout.CapacityBits(compact, layers);
            int totalWords   = capacityBits / codewordBits;

            var data = Codewords(message, codewordBits);
            if (data.Length > MaxDataCodewords(compact)) continue;

            int required = RequiredCheckWords(totalWords, options.ErrorCorrectionPercent);
            if (data.Length + required > totalWords) continue;

            symbol = Build(compact, layers, codewordBits, capacityBits, totalWords, data, message.Count);
            return true;
        }

        error = TooBig(options, message.Count);
        return false;
    }

    // ── Choosing the symbol ────────────────────────────────────────────────

    /// <summary>
    /// The sizes to try, smallest first. A forced layer count collapses this to the one symbol the
    /// author asked for, so the failure is reported rather than worked around.
    /// </summary>
    private static IEnumerable<(bool Compact, int Layers)> Candidates(AztecOptions options)
    {
        if (options.Layers is int forced)
        {
            if (options.Format != AztecFormat.Full && forced <= AztecOptions.MaxCompactLayers)
                yield return (true, forced);
            if (options.Format != AztecFormat.Compact && forced <= AztecOptions.MaxFullLayers)
                yield return (false, forced);
            yield break;
        }

        if (options.Format != AztecFormat.Full)
            for (int layers = 1; layers <= AztecOptions.MaxCompactLayers; layers++)
                yield return (true, layers);

        if (options.Format != AztecFormat.Compact)
            for (int layers = 1; layers <= AztecOptions.MaxFullLayers; layers++)
                yield return (false, layers);
    }

    /// <summary>
    /// Bits per codeword, which the layer count decides — the larger the symbol, the wider its
    /// codewords, so that the number of them stays inside what the mode message can state.
    /// </summary>
    internal static int CodewordBits(int layers) =>
        layers <= 2 ? 6 : layers <= 8 ? 8 : layers <= 22 ? 10 : 12;

    /// <summary>The Galois field for a codeword width. Aztec's eight-bit field is Data Matrix's.</summary>
    private static GaloisField Field(int codewordBits) => codewordBits switch
    {
        6  => GaloisField.Aztec6,
        8  => GaloisField.Aztec8,
        10 => GaloisField.Aztec10,
        _  => GaloisField.Aztec12,
    };

    /// <summary>
    /// The most codewords the mode message can name: six bits' worth in a compact symbol, eleven in a
    /// full one. A large compact symbol can therefore hold fewer codewords than it has room for.
    /// </summary>
    private static int MaxDataCodewords(bool compact) => compact ? 64 : 2048;

    /// <summary>
    /// The check words a symbol must carry: the requested share of its capacity, and never fewer than
    /// three, which is the floor the standard sets so that even an unprotected-looking symbol can still
    /// detect damage.
    /// </summary>
    private static int RequiredCheckWords(int totalWords, int percent) =>
        Math.Min(totalWords, (totalWords * percent + 99) / 100 + AztecOptions.MinimumCheckWords);

    // ── Codewords ──────────────────────────────────────────────────────────

    /// <summary>
    /// The message's bits as codewords, with the stuffing the standard demands: when the first
    /// <c>width − 1</c> bits of a codeword are all the same, the last one is forced to the opposite
    /// value instead of taking the next message bit.
    ///
    /// <para>
    /// The reason is the reference grid. A codeword of all ones or all zeros is a run long enough to
    /// read as grid rather than as data, so the encoding makes those two words unwriteable. A partial
    /// last codeword is filled with ones, and the same rule keeps that from coming out all ones.
    /// </para>
    /// </summary>
    internal static int[] Codewords(IReadOnlyList<bool> bits, int width)
    {
        var words = new List<int>();
        int uniform = (1 << width - 1) - 1;
        int at = 0;

        while (at < bits.Count)
        {
            int word = 0;

            for (int placed = 0; placed < width; placed++)
            {
                if (placed == width - 1 && (word == 0 || word == uniform))
                {
                    word = word << 1 | (word == 0 ? 1 : 0);
                    continue;
                }

                bool bit = at < bits.Count ? bits[at] : true;   // the tail is padded with ones
                at++;
                word = word << 1 | (bit ? 1 : 0);
            }

            words.Add(word);
        }

        return [.. words];
    }

    // ── Building ───────────────────────────────────────────────────────────

    private static AztecSymbol Build(bool compact, int layers, int codewordBits, int capacityBits,
                                     int totalWords, int[] data, int messageBits)
    {
        var field = Field(codewordBits);
        var check = ReedSolomon.Parity(field, data,
                                       ReedSolomon.Generator(field, totalWords - data.Length, firstRoot: 1));

        // Whatever the codeword width does not divide is left over at the start of the spiral, as zeros.
        var stream = new List<bool>(capacityBits);
        for (int pad = capacityBits % codewordBits; pad > 0; pad--) stream.Add(false);
        foreach (int word in data)  Append(stream, word, codewordBits);
        foreach (int word in check) Append(stream, word, codewordBits);

        var modules = AztecLayout.Build(compact, layers, ModeMessage(compact, layers, data.Length), stream);

        return new AztecSymbol(modules, compact, layers, codewordBits, data.Length, totalWords, messageBits);
    }

    /// <summary>
    /// The mode message: the layer count and the number of data codewords, each one less than itself so
    /// the smallest symbol reads as zero, split into four-bit words and given Reed–Solomon check words
    /// of its own over GF(16).
    ///
    /// <para>
    /// It has to be protected separately because it is what a reader needs before it can read anything
    /// else — damage to the eight bits that say how big the symbol is would otherwise cost the whole
    /// message, however well the message itself was protected.
    /// </para>
    /// </summary>
    internal static List<bool> ModeMessage(bool compact, int layers, int dataCodewords)
    {
        int countBits = compact ? 6 : 11;
        int word      = layers - 1 << countBits | dataCodewords - 1;
        int nibbles   = compact ? 2 : 4;

        var described = new int[nibbles];
        for (int i = 0; i < nibbles; i++) described[i] = word >> (nibbles - 1 - i) * 4 & 0xF;

        var check = ReedSolomon.Parity(GaloisField.AztecMode, described,
                                       ReedSolomon.Generator(GaloisField.AztecMode,
                                                             compact ? 5 : 6, firstRoot: 1));

        var bits = new List<bool>();
        foreach (int nibble in described) Append(bits, nibble, 4);
        foreach (int nibble in check)     Append(bits, nibble, 4);
        return bits;
    }

    private static void Append(List<bool> bits, int value, int width)
    {
        for (int bit = width - 1; bit >= 0; bit--) bits.Add((value >> bit & 1) != 0);
    }

    /// <summary>Why nothing fitted, said in terms of the constraint the author actually set.</summary>
    private static string TooBig(AztecOptions options, int messageBits)
    {
        if (options.Layers is int forced)
        {
            string family = options.Format == AztecFormat.Full ? "full" : "compact";
            return $"This message needs {messageBits} bits, which will not fit a {family} {forced}-layer "
                 + $"Aztec symbol at {options.ErrorCorrectionPercent}% error correction. "
                 + "Raise `layers:`, lower `ecc:`, or drop them both and let the encoder choose.";
        }

        if (options.Format == AztecFormat.Compact)
            return $"This message needs {messageBits} bits. A compact Aztec symbol holds at most "
                 + $"{AztecLayout.CapacityBits(true, AztecOptions.MaxCompactLayers)}, error correction "
                 + "included — use `format: full`.";

        return $"This message needs {messageBits} bits, and the largest Aztec symbol holds "
             + $"{AztecLayout.CapacityBits(false, AztecOptions.MaxFullLayers)} including its "
             + $"{options.ErrorCorrectionPercent}% error correction. Shorten it, or lower `ecc:`.";
    }
}

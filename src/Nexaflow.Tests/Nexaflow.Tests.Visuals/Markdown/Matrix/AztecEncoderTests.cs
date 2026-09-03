using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.Aztec;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// The Aztec encoder: the size and capacity tables, the codeword stuffing, the choice of symbol, and
/// the whole pipeline read back through <see cref="AztecTestDecoder"/>.
///
/// <para>
/// The two tables are checked against the numbers the standard publishes, not against what the code
/// computes, because they are the part a closed-form derivation could get plausibly wrong: the size
/// formula has to reproduce 15 to 151 including where the reference grid interrupts it, and the
/// capacity formula has to reproduce all thirty-two full-range codeword counts. Either one being out
/// by a layer would still produce symbols, just not ones anything else can read.
/// </para>
/// </summary>
[TestClass]
[CoversNode("aztec-encoder")]
public class AztecEncoderTests
{
    // ── The published tables ───────────────────────────────────────────────

    /// <summary>Modules on a side, one to four layers compact and one to thirty-two full.</summary>
    [TestMethod]
    public void SymbolSizesMatchTheStandardsTable()
    {
        int[] compact = [15, 19, 23, 27];
        int[] full =
        [
            19,  23,  27,  31,  37,  41,  45,  49,  53,  57,  61,  67,  71,  75,  79,  83,
            87,  91,  95, 101, 105, 109, 113, 117, 121, 125, 131, 135, 139, 143, 147, 151,
        ];

        for (int layers = 1; layers <= compact.Length; layers++)
            Assert.AreEqual(compact[layers - 1], AztecLayout.Size(true, layers), $"compact {layers}");

        for (int layers = 1; layers <= full.Length; layers++)
            Assert.AreEqual(full[layers - 1], AztecLayout.Size(false, layers), $"full {layers}");
    }

    /// <summary>
    /// Total codewords per size — capacity divided by the codeword width the layer count picks. This is
    /// the table that catches a wrong codeword width as well as a wrong capacity, because the two
    /// jumps in width show up as the counts at nine and twenty-three layers going down rather than up.
    /// </summary>
    [TestMethod]
    public void CodewordCountsMatchTheStandardsTable()
    {
        int[] compact = [17, 40, 51, 76];
        int[] full =
        [
              21,   48,   60,   88,  120,  156,  196,  240,  230,  272,  316,  364,
             416,  470,  528,  588,  652,  720,  790,  864,  940, 1020,  920,  992,
            1066, 1144, 1224, 1306, 1392, 1480, 1570, 1664,
        ];

        for (int layers = 1; layers <= compact.Length; layers++)
            Assert.AreEqual(compact[layers - 1], TotalCodewords(true, layers), $"compact {layers}");

        for (int layers = 1; layers <= full.Length; layers++)
            Assert.AreEqual(full[layers - 1], TotalCodewords(false, layers), $"full {layers}");
    }

    private static int TotalCodewords(bool compact, int layers) =>
        AztecLayout.CapacityBits(compact, layers) / AztecEncoder.CodewordBits(layers);

    /// <summary>Every symbol's data cells account for exactly its capacity, each cell used once.</summary>
    [TestMethod]
    public void EveryLayerIsFilledExactlyOnce()
    {
        foreach (bool compact in (bool[])[true, false])
        {
            int most = compact ? AztecOptions.MaxCompactLayers : AztecOptions.MaxFullLayers;
            for (int layers = 1; layers <= most; layers++)
            {
                var cells = AztecLayout.DataCells(compact, layers);
                int size  = AztecLayout.Size(compact, layers);

                Assert.AreEqual(AztecLayout.CapacityBits(compact, layers), cells.Count,
                                $"{(compact ? "compact" : "full")} {layers} cell count");
                Assert.AreEqual(cells.Count, cells.Distinct().Count(),
                                $"{(compact ? "compact" : "full")} {layers} repeats a cell");
                Assert.IsTrue(cells.All(c => c.Row >= 0 && c.Row < size && c.Col >= 0 && c.Col < size),
                              $"{(compact ? "compact" : "full")} {layers} places a cell outside the symbol");
            }
        }
    }

    // ── Stuffing ───────────────────────────────────────────────────────────

    /// <summary>
    /// No codeword may be all ones or all zeros, whatever the message — that is the point of the
    /// stuffing, and a run that wide would read as reference grid rather than as data.
    /// </summary>
    [TestMethod]
    public void StuffingLeavesNoUniformCodeword()
    {
        var random = new Random(20260903);

        foreach (int width in (int[])[6, 8, 10, 12])
            for (int trial = 0; trial < 200; trial++)
            {
                var bits = new List<bool>();
                int length = random.Next(1, 400);

                // Long runs of one value are exactly the input that provokes stuffing, so the corpus is
                // mostly runs rather than noise.
                while (bits.Count < length)
                {
                    bool value = random.Next(2) == 0;
                    for (int run = random.Next(1, 20); run > 0 && bits.Count < length; run--) bits.Add(value);
                }

                foreach (int word in AztecEncoder.Codewords(bits, width))
                {
                    Assert.AreNotEqual(0, word, $"an all-zero codeword at width {width}");
                    Assert.AreNotEqual((1 << width) - 1, word, $"an all-one codeword at width {width}");
                }
            }
    }

    [TestMethod]
    public void StuffingIsUndoneByReadingItBack()
    {
        var bits = Enumerable.Range(0, 137).Select(i => i % 11 < 4).ToList();
        var words = AztecEncoder.Codewords(bits, 6);

        // Whatever came out, the message is a prefix of what reading the codewords back gives; the rest
        // is the ones the last codeword was padded with.
        var read = Unstuff(words, 6);
        Assert.IsTrue(read.Count >= bits.Count);
        CollectionAssert.AreEqual(bits, read.Take(bits.Count).ToList());
        Assert.IsTrue(read.Skip(bits.Count).All(bit => bit), "the padding is ones");
    }

    private static List<bool> Unstuff(int[] words, int width)
    {
        var bits = new List<bool>();
        int uniform = (1 << width - 1) - 1;

        foreach (int word in words)
        {
            int leading = word >> 1;
            int keep = leading == 0 || leading == uniform ? width - 1 : width;
            for (int b = width - 1; b >= width - keep; b--) bits.Add((word >> b & 1) != 0);
        }

        return bits;
    }

    // ── The whole pipeline ─────────────────────────────────────────────────

    [TestMethod]
    public void MessagesRoundTrip()
    {
        string[] payloads =
        [
            "A",
            "nexaflow",
            "An Aztec Code",
            "HELLO WORLD",
            "Order 4417 shipped 2026-09-03.",
            "https://example.org/tickets/9f3a?seat=12B",
            "0123456789012345678901234567890123456789",
            "mixed CASE with punctuation: (yes), \"quoted\"; and more!",
            "tab\tand\r\nnewline",
            "Grüße, Ω — 日本語",
            "a",
            "  ",
            "~|^_`@\\",
            "The quick brown fox jumps over the lazy dog. " +
            "The quick brown fox jumps over the lazy dog. " +
            "The quick brown fox jumps over the lazy dog.",
        ];

        foreach (string payload in payloads)
        {
            Assert.IsTrue(AztecEncoder.TryEncode(payload, AztecOptions.Default, out var symbol, out string? error),
                          $"'{Short(payload)}': {error}");

            var decoded = AztecTestDecoder.Decode(symbol!);
            Assert.AreEqual(payload, decoded.Text, $"'{Short(payload)}' did not round-trip");
            Assert.AreEqual(symbol!.Compact, decoded.Compact);
            Assert.AreEqual(symbol.Layers, decoded.Layers);
            Assert.AreEqual(symbol.DataCodewords, decoded.DataCodewords);
        }
    }

    /// <summary>
    /// A message the compact family cannot hold. Nothing but the full range reaches this size, so it
    /// exercises the reference grid, the alignment map and the wider codewords at once.
    /// </summary>
    [TestMethod]
    public void LongMessagesUseTheFullRangeAndStillRoundTrip()
    {
        string payload = string.Join(' ', Enumerable.Repeat("Nexaflow renders symbols inline.", 40));

        Assert.IsTrue(AztecEncoder.TryEncode(payload, AztecOptions.Default, out var symbol, out string? error), error);
        Assert.IsFalse(symbol!.Compact, "a message this long has to be full range");
        Assert.IsTrue(symbol.Layers > 4);
        Assert.IsTrue(symbol.CodewordBits >= 8);
        Assert.AreEqual(payload, AztecTestDecoder.Decode(symbol).Text);
    }

    /// <summary>The reference grid only appears above four layers, so a symbol either side of it is worth having.</summary>
    [TestMethod]
    public void FullRangeSymbolsRoundTripAtEverySizeThatFits()
    {
        foreach (int layers in (int[])[1, 2, 3, 4, 5, 6, 11, 12])
        {
            string payload = new('X', Math.Max(1, AztecLayout.CapacityBits(false, layers) / 40));
            var options = new AztecOptions { Format = AztecFormat.Full, Layers = layers };

            Assert.IsTrue(AztecEncoder.TryEncode(payload, options, out var symbol, out string? error),
                          $"full {layers}: {error}");

            Assert.AreEqual(layers, symbol!.Layers);
            Assert.AreEqual(AztecLayout.Size(false, layers), symbol.Size);

            var decoded = AztecTestDecoder.Decode(symbol);
            Assert.AreEqual(payload, decoded.Text, $"full {layers} did not round-trip");
            Assert.IsFalse(decoded.Compact);
        }
    }

    [TestMethod]
    public void CompactAndFullCarryTheSameMessageInDifferentSymbols()
    {
        const string payload = "Boarding pass 4417";

        Assert.IsTrue(AztecEncoder.TryEncode(payload, new AztecOptions { Format = AztecFormat.Compact },
                                             out var small, out _));
        Assert.IsTrue(AztecEncoder.TryEncode(payload, new AztecOptions { Format = AztecFormat.Full },
                                             out var large, out _));

        Assert.IsTrue(small!.Compact);
        Assert.IsFalse(large!.Compact);
        Assert.IsTrue(large.Size > small.Size, "a full symbol of the same message is the larger one");

        Assert.AreEqual(payload, AztecTestDecoder.Decode(small).Text);
        Assert.AreEqual(payload, AztecTestDecoder.Decode(large).Text);
    }

    [TestMethod]
    public void AutoPrefersCompactWhileItFits()
    {
        Assert.IsTrue(AztecEncoder.TryEncode("short", AztecOptions.Default, out var symbol, out _));
        Assert.IsTrue(symbol!.Compact);
    }

    // ── What the author asked for ──────────────────────────────────────────

    [TestMethod]
    public void ForcedLayersFixTheSize()
    {
        var options = new AztecOptions { Format = AztecFormat.Compact, Layers = 4 };

        Assert.IsTrue(AztecEncoder.TryEncode("x", options, out var symbol, out _));
        Assert.AreEqual(4, symbol!.Layers);
        Assert.AreEqual(27, symbol.Size);
        Assert.AreEqual("x", AztecTestDecoder.Decode(symbol).Text);
    }

    [TestMethod]
    public void ForcedLayersTooSmallAreRefusedRatherThanGrown()
    {
        string payload = new('W', 200);
        var options = new AztecOptions { Format = AztecFormat.Compact, Layers = 1 };

        Assert.IsFalse(AztecEncoder.TryEncode(payload, options, out _, out string? error));
        StringAssert.Contains(error!, "compact 1-layer");
    }

    [TestMethod]
    public void MoreErrorCorrectionTakesABiggerSymbol()
    {
        const string payload = "Order 4417 shipped 2026-09-03 from the north depot.";

        Assert.IsTrue(AztecEncoder.TryEncode(payload, new AztecOptions { ErrorCorrectionPercent = 10 },
                                             out var relaxed, out _));
        Assert.IsTrue(AztecEncoder.TryEncode(payload, new AztecOptions { ErrorCorrectionPercent = 75 },
                                             out var strict, out _));

        Assert.IsTrue(strict!.Size >= relaxed!.Size);
        Assert.IsTrue(strict.ErrorCorrectionPercent > relaxed.ErrorCorrectionPercent);
        Assert.IsTrue(strict.ErrorCorrectionPercent >= 75,
                      $"asked for 75%, got {strict.ErrorCorrectionPercent}%");
        Assert.AreEqual(payload, AztecTestDecoder.Decode(strict).Text);
    }

    /// <summary>Whatever the message leaves over becomes error correction rather than padding.</summary>
    [TestMethod]
    public void SpareCapacityBecomesErrorCorrection()
    {
        Assert.IsTrue(AztecEncoder.TryEncode("hi", AztecOptions.Default, out var symbol, out _));
        Assert.AreEqual(symbol!.TotalCodewords, symbol.DataCodewords + symbol.CheckCodewords);
        Assert.IsTrue(symbol.ErrorCorrectionPercent > AztecOptions.DefaultErrorCorrectionPercent);
    }

    [TestMethod]
    public void AGs1MessageIsFlaggedWithFnc1()
    {
        Assert.IsTrue(Gs1ElementString.TryParse("(01)04150123456782(17)261231(10)LOT7",
                                                out string? payload, out string? error), error);

        var options = new AztecOptions { Gs1 = true };
        Assert.IsTrue(AztecEncoder.TryEncode(payload!, options, out var symbol, out error), error);

        var decoded = AztecTestDecoder.Decode(symbol!);
        Assert.IsTrue(decoded.Gs1, "the symbol should carry FNC1");
        Assert.AreEqual(payload, decoded.Text);
    }

    [TestMethod]
    public void AnEciNumberIsCarriedAtTheHeadOfTheMessage()
    {
        var options = new AztecOptions { Eci = 26 };
        Assert.IsTrue(AztecEncoder.TryEncode("Grüße", options, out var symbol, out string? error), error);

        var decoded = AztecTestDecoder.Decode(symbol!);
        Assert.AreEqual(26, decoded.Eci);
        Assert.AreEqual("Grüße", decoded.Text);
    }

    // ── The dynamic program earns its keep ─────────────────────────────────

    /// <summary>
    /// A run of digits costs four bits each, letters five, so the same number of characters must come
    /// out smaller as digits. A greedy encoder that never latches to the digit set passes everything
    /// else in this class and fails this.
    /// </summary>
    [TestMethod]
    public void DigitRunsCostLessThanLetterRuns()
    {
        Assert.IsTrue(AztecEncoder.TryEncode(new string('7', 60), AztecOptions.Default, out var digits, out _));
        Assert.IsTrue(AztecEncoder.TryEncode(new string('Q', 60), AztecOptions.Default, out var letters, out _));

        Assert.IsTrue(digits!.MessageBits < letters!.MessageBits,
                      $"digits took {digits.MessageBits} bits, letters {letters.MessageBits}");
    }

    /// <summary>
    /// A lone capital in a lower-case run is a shift, not a pair of latches — five bits rather than
    /// fourteen, which is the difference the search is there to find.
    /// </summary>
    [TestMethod]
    public void ALoneCapitalIsShiftedIntoRatherThanLatched()
    {
        Assert.IsTrue(AztecEncoder.TryEncode("aaaaXaaaa", AztecOptions.Default, out var shifted, out _));
        Assert.IsTrue(AztecEncoder.TryEncode("aaaaaaaaa", AztecOptions.Default, out var plain, out _));

        Assert.AreEqual(plain!.MessageBits + 5, shifted!.MessageBits,
                        "the upper shift, and nothing else — the capital costs what a letter costs");
    }

    /// <summary>Bytes with no character code at all can only travel in a byte run, and must survive it.</summary>
    [TestMethod]
    public void CharactersWithNoCodeTravelAsBytes()
    {
        string payload = "ctrl\u000E\u0011\u001A end";

        Assert.IsTrue(AztecEncoder.TryEncode(payload, AztecOptions.Default, out var symbol, out string? error), error);
        Assert.AreEqual(payload, AztecTestDecoder.Decode(symbol!).Text);
    }

    /// <summary>A run over thirty-one bytes needs the long length field, which is a separate code path.</summary>
    [TestMethod]
    public void LongByteRunsUseTheExtendedLengthField()
    {
        string payload = new string('\u000E', 40) + "tail";

        Assert.IsTrue(AztecEncoder.TryEncode(payload, AztecOptions.Default, out var symbol, out string? error), error);
        Assert.AreEqual(payload, AztecTestDecoder.Decode(symbol!).Text);
    }

    // ── Refusals ───────────────────────────────────────────────────────────

    [TestMethod]
    public void AnEmptyPayloadIsRefused()
    {
        Assert.IsFalse(AztecEncoder.TryEncode(string.Empty, AztecOptions.Default, out _, out string? error));
        StringAssert.Contains(error!, "nothing to encode");
    }

    [TestMethod]
    public void AMessageBeyondTheLargestSymbolIsRefused()
    {
        Assert.IsFalse(AztecEncoder.TryEncode(new string('M', 4000), AztecOptions.Default, out _, out string? error));
        StringAssert.Contains(error!, "largest Aztec symbol");
    }

    [TestMethod]
    public void ACompactOnlyBlockSaysToUseTheFullRange()
    {
        var options = new AztecOptions { Format = AztecFormat.Compact };

        Assert.IsFalse(AztecEncoder.TryEncode(new string('M', 200), options, out _, out string? error));
        StringAssert.Contains(error!, "format: full");
    }

    private static string Short(string text) => text.Length <= 32 ? text : text[..32] + "…";
}

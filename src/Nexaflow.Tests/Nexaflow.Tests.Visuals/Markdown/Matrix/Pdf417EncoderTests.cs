using System;
using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Matrix.Pdf417;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// The PDF417 encoder: the table checked against the standard's own rules, a golden vector taken from
/// a symbol made elsewhere, and everything else read back by <see cref="Pdf417TestDecoder"/>.
/// </summary>
[TestClass]
[CoversNode("pdf417-encoder")]
public class Pdf417EncoderTests
{
    /// <summary>
    /// Every character in the table is 17 modules of four bars and four spaces, each one to six wide,
    /// in the cluster it is filed under.
    /// <para>
    /// The table is data that cannot be derived, so it is checked rather than trusted: a single
    /// mistyped value would encode a symbol that looks perfectly plausible and scans as something else,
    /// and this is the assertion that catches it.
    /// </para>
    /// </summary>
    [TestMethod]
    public void EveryTableEntryIsALegalSymbolCharacter()
    {
        foreach (int cluster in Pdf417Codewords.Clusters)
        {
            for (int cw = 0; cw < Pdf417Codewords.Count; cw++)
            {
                int pattern = Pdf417Codewords.Pattern(cluster, cw);
                string bits = Convert.ToString(pattern, 2).PadLeft(17, '0');

                Assert.AreEqual(17, bits.Length, $"cluster {cluster} codeword {cw} is not 17 modules");
                Assert.AreEqual('1', bits[0],  $"cluster {cluster} codeword {cw} does not start with a bar");
                Assert.AreEqual('0', bits[^1], $"cluster {cluster} codeword {cw} does not end with a space");

                var widths = Widths(bits);
                Assert.AreEqual(8, widths.Length, $"cluster {cluster} codeword {cw} has {widths.Length} elements");
                Assert.IsTrue(widths.All(v => v is >= 1 and <= 6),
                              $"cluster {cluster} codeword {cw} has an element outside 1..6: {string.Join(",", widths)}");

                int computed = ((widths[0] - widths[2] + widths[4] - widths[6]) % 9 + 9) % 9;
                Assert.AreEqual(cluster, computed, $"codeword {cw} is filed under cluster {cluster} but computes {computed}");
            }
        }
    }

    /// <summary>
    /// The data codewords for "nexaflow", taken off a symbol produced by another generator.
    /// <para>
    /// It pins text compaction exactly — the latch to lower case, the eight characters two to a
    /// codeword, and the odd tail padded with a shift — and it does so against something this code had
    /// no hand in making.
    /// </para>
    /// </summary>
    [TestMethod]
    public void MatchesAnotherGeneratorsCodewordsForTheSameText()
    {
        var symbol = Encode("nexaflow", new() { Columns = 2, ErrorCorrectionLevel = 2 });
        var decoded = Pdf417TestDecoder.Decode(symbol);

        CollectionAssert.AreEqual(new[] { 6, 823, 143, 5, 344, 689 }, decoded.Codewords.Take(6).ToArray(),
                                  "the descriptor and the five text codewords the reference symbol carries");
        Assert.AreEqual("nexaflow", decoded.Text);
        Assert.AreEqual(2, decoded.Columns);
        Assert.AreEqual(2, decoded.ErrorLevel);
    }

    [TestMethod]
    public void RoundTripsText()
    {
        foreach (string text in new[]
                 {
                     "A", "nexaflow", "Nexaflow PDF417", "Hello, World!",
                     "MiXeD CaSe and punctuation: (a) [b] {c} #1 $2 %3",
                     "the quick brown fox jumps over the lazy dog",
                 })
        {
            Assert.AreEqual(text, Pdf417TestDecoder.Decode(Encode(text)).Text, text);
        }
    }

    [TestMethod]
    public void RoundTripsALongDigitRun_ThroughNumericCompaction()
    {
        // Long enough to be worth the latch, and longer than one 44-digit group.
        string digits = string.Concat(Enumerable.Range(0, 60).Select(i => (char)('0' + i % 10)));

        var symbol = Encode(digits);
        StringAssert.Contains(symbol.Compaction, "Numeric");
        Assert.AreEqual(digits, Pdf417TestDecoder.Decode(symbol).Text);
    }

    [TestMethod]
    public void TheShapeAndLevelComeBackOutOfTheRowIndicators()
    {
        foreach (int columns in new[] { 1, 2, 5, 12 })
        {
            var symbol  = Encode("indicators carry the shape", new() { Columns = columns, ErrorCorrectionLevel = 4 });
            var decoded = Pdf417TestDecoder.Decode(symbol);

            Assert.AreEqual(columns, decoded.Columns, $"{columns} columns");
            Assert.AreEqual(symbol.Rows, decoded.Rows);
            Assert.AreEqual(4, decoded.ErrorLevel);
        }
    }

    [TestMethod]
    public void EveryErrorCorrectionLevelProducesItsOwnParityAndVerifies()
    {
        for (int level = 0; level <= 8; level++)
        {
            var symbol = Encode("level " + level, new() { ErrorCorrectionLevel = level });
            Assert.AreEqual(level, Pdf417TestDecoder.Decode(symbol).ErrorLevel, $"level {level}");
        }
    }

    [TestMethod]
    public void ARowIs17ModulesPerCharacterPlusItsFraming()
    {
        var symbol = Encode("width", new() { Columns = 3 });

        // start(17) + left(17) + data(3 × 17) + right(17) + stop(18)
        Assert.AreEqual(17 * (3 + 2) + 17 + 18, symbol.Width);
        Assert.AreEqual(symbol.Rows, symbol.Height);
    }

    [TestMethod]
    public void ATruncatedSymbolDropsTheRightIndicatorAndStop()
    {
        var full      = Encode("truncation", new() { Columns = 3 });
        var truncated = Encode("truncation", new() { Columns = 3, Truncated = true });

        Assert.AreEqual(17 * (3 + 2) + 1, truncated.Width);
        Assert.IsTrue(truncated.Width < full.Width);
    }

    [TestMethod]
    public void RefusesAShapeOrLevelOutsideTheStandard()
    {
        Assert.IsFalse(Pdf417Encoder.TryEncode("x", new() { Columns = 31 }, out _, out var error));
        StringAssert.Contains(error, "30");

        Assert.IsFalse(Pdf417Encoder.TryEncode("x", new() { ErrorCorrectionLevel = 9 }, out _, out error));
        StringAssert.Contains(error, "8");
    }

    [TestMethod]
    public void TooMuchDataSaysSo()
    {
        Assert.IsFalse(Pdf417Encoder.TryEncode(new string('A', 4000), new() { Columns = 1 }, out _, out var error));
        StringAssert.Contains(error, "Too much data");
    }

    private static Pdf417Symbol Encode(string text, Pdf417Options? options = null)
    {
        Assert.IsTrue(Pdf417Encoder.TryEncode(text, options ?? Pdf417Options.Default, out var symbol, out var error), error);
        return symbol!;
    }

    private static int[] Widths(string bits)
    {
        var widths = new System.Collections.Generic.List<int>();
        char last = bits[0];
        int run = 0;
        foreach (char c in bits) { if (c == last) run++; else { widths.Add(run); last = c; run = 1; } }
        widths.Add(run);
        return widths.ToArray();
    }
}

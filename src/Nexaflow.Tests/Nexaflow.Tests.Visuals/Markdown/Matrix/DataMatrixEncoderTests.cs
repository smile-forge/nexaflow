using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Matrix;
using Nexaflow.Visuals.Text.Markdown.Matrix.DataMatrix;

namespace Nexaflow.Tests.Visuals.Markdown.Matrix;

/// <summary>
/// The Data Matrix encoder, pinned from outside where the standard gives a figure and read back by
/// <see cref="DataMatrixTestDecoder"/> everywhere else.
/// </summary>
[TestClass]
[CoversNode("datamatrix-encoder")]
public class DataMatrixEncoderTests
{
    /// <summary>
    /// ISO/IEC 16022's own worked example: "123456" is three digit-pair codewords and five parity
    /// codewords in a 10×10 symbol. The one figure in the standard that pins field, generator, first
    /// root and digit pairing all at once — get any of them wrong and this is the test that says so.
    /// </summary>
    [TestMethod]
    public void TheStandardsWorkedExample_EncodesToItsPublishedCodewords()
    {
        var symbol  = Encode("123456");
        var decoded = DataMatrixTestDecoder.Decode(symbol);

        CollectionAssert.AreEqual(new[] { 142, 164, 186, 114, 25, 5, 88, 102 }, decoded.Codewords);
        Assert.AreEqual("10×10", symbol.Size.ToString());
    }

    [TestMethod]
    public void EverySize_PlacesEveryBitOfEveryCodewordExactlyOnce()
    {
        // The placement walk is a transcription of Annex F with nothing to derive it from, so it is
        // pinned by what has to be true of any correct walk: every data cell is used once, every
        // codeword bit lands once, and the only cells left over are the fixed corner pair.
        foreach (var size in DataMatrixEncoder.Sizes)
        {
            int mr = size.MappingRows, mc = size.MappingColumns;
            var placement = new DataMatrixEncoder.Placement(mr, mc);
            placement.Run();

            int total = size.DataCodewords + size.EccCodewords;
            var seen  = new HashSet<int>();
            int empty = 0;

            for (int r = 0; r < mr; r++)
            for (int c = 0; c < mc; c++)
            {
                int slot = placement[r, c];
                if (slot == 0) { empty++; continue; }

                Assert.IsTrue(seen.Add(slot), $"{size}: codeword {slot >> 3} bit {slot & 7} placed twice");
                Assert.IsTrue((slot >> 3) >= 1 && (slot >> 3) <= total, $"{size}: codeword {slot >> 3} is outside 1..{total}");
            }

            Assert.AreEqual(total * 8, seen.Count, $"{size}: not every bit of every codeword was placed");
            Assert.AreEqual(placement.LeavesCornerUnfilled ? 4 : 0, empty, $"{size}: unexpected empty cells");
        }
    }

    [TestMethod]
    public void RoundTrips_Ascii_DigitPairs_AndTheUpperShift()
    {
        foreach (var text in new[] { "A", "Hello, World!", "1234567890", "MIXED 12 and 3456 digits", "café à la crème" })
            Assert.AreEqual(text, DataMatrixTestDecoder.Decode(Encode(text)).Text, text);
    }

    [TestMethod]
    public void NonAscii_IsWrittenAsUtf8UnderEci26()
    {
        var decoded = DataMatrixTestDecoder.Decode(Encode("Grüße — 日本"));
        Assert.AreEqual(26, decoded.Eci);
        Assert.AreEqual("Grüße — 日本", decoded.Text);
    }

    [TestMethod]
    public void UpperCaseText_TakesC40_AndIsSmallerForIt()
    {
        // Ninety upper-case characters: 90 codewords in ASCII, 62 in C40 — the difference between
        // fitting a 32×32 and not, which is the whole reason C40 exists here.
        string text = new string('A', 90);

        var symbol  = Encode(text);
        var decoded = DataMatrixTestDecoder.Decode(symbol);

        Assert.AreEqual("C40", symbol.Encodation);
        Assert.AreEqual("32×32", symbol.Size.ToString());
        Assert.AreEqual(text, decoded.Text);
    }

    [TestMethod]
    public void C40_HandlesEveryEndingTheStandardDefines()
    {
        // The end-of-data rules turn on how many values are left over and how much room the symbol
        // has, so the same shape of text is tried at every length across a run of sizes.
        for (int length = 1; length <= 60; length++)
        {
            string text = string.Concat(Enumerable.Range(0, length).Select(i => (char)('A' + i % 26)));
            var symbol  = Encode(text);
            Assert.AreEqual(text, DataMatrixTestDecoder.Decode(symbol).Text, $"length {length} ({symbol.Encodation}, {symbol.Size})");
        }
    }

    [TestMethod]
    public void C40_ReachesLowerCaseAndPunctuationThroughShifts()
    {
        // Mostly capitals, with enough of each shift set to have to cross into it and back.
        const string text = "ORDER-12345 for: Jane Doe (ref #77)!";
        Assert.AreEqual(text, DataMatrixTestDecoder.Decode(Encode(text)).Text);
    }

    [TestMethod]
    public void Gs1_StartsWithFnc1_AndSeparatesWithIt()
    {
        var decoded = DataMatrixTestDecoder.Decode(Encode("0104150012345623" + "10LOT7" + "" + "21SN1", new() { Gs1 = true }));

        Assert.IsTrue(decoded.Gs1);
        Assert.AreEqual("0104150012345623" + "10LOT7" + "" + "21SN1", decoded.Text);
    }

    [TestMethod]
    public void Macro06_IsOneCodeword_AndReadsBack()
    {
        var decoded = DataMatrixTestDecoder.Decode(Encode("9N110123456224", new() { Macro = DataMatrixMacro.Macro06 }));

        Assert.AreEqual(DataMatrixMacro.Macro06, decoded.Macro);
        Assert.AreEqual("9N110123456224", decoded.Text);
    }

    [TestMethod]
    public void MultiBlockSymbols_InterleaveTheirParity()
    {
        // 52×52 is the first size with two Reed–Solomon blocks; 144×144 has ten, two of them a
        // codeword shorter. Both have to de-interleave and check clean.
        foreach (var (text, expected) in new[] { (new string('x', 200), "52×52"), (new string('x', 1500), "144×144") })
        {
            var symbol  = Encode(text);
            Assert.AreEqual(expected, symbol.Size.ToString());
            Assert.AreEqual(text, DataMatrixTestDecoder.Decode(symbol).Text);
        }
    }

    [TestMethod]
    public void Rectangles_AreChosenOnlyWhenAsked_AndRoundTrip()
    {
        var any  = Encode("ABCDEFGH");
        var rect = Encode("ABCDEFGH", new() { Shape = DataMatrixShape.Rectangle });
        var sq   = Encode("ABCDEFGH", new() { Shape = DataMatrixShape.Square });

        Assert.IsTrue(rect.Size.Rows < rect.Size.Columns, $"rectangle asked for, got {rect.Size}");
        Assert.IsTrue(sq.Size.IsSquare, $"square asked for, got {sq.Size}");
        Assert.IsTrue(any.Size.DataCodewords <= Math.Min(rect.Size.DataCodewords, sq.Size.DataCodewords),
                      "any: the smallest of either family");

        foreach (var s in new[] { any, rect, sq })
            Assert.AreEqual("ABCDEFGH", DataMatrixTestDecoder.Decode(s).Text, s.Size.ToString());
    }

    [TestMethod]
    public void AForcedSize_IsUsed_AndRefusedWhenTooSmall()
    {
        var forced = Encode("HI", new() { Size = (24, 24) });
        Assert.AreEqual("24×24", forced.Size.ToString());
        Assert.AreEqual("HI", DataMatrixTestDecoder.Decode(forced).Text);

        Assert.IsFalse(DataMatrixEncoder.TryEncode(new string('x', 50), new() { Size = (10, 10) }, out _, out var error));
        StringAssert.Contains(error, "10×10");
    }

    [TestMethod]
    public void TooMuchData_SaysSo()
    {
        Assert.IsFalse(DataMatrixEncoder.TryEncode(new string('x', 2000), DataMatrixOptions.Default, out _, out var error));
        StringAssert.Contains(error, "Too much data");
    }

    [TestMethod]
    public void TheSizeTable_MatchesTheStandardsTotals()
    {
        // Each size's codewords fill its mapping area: 8 bits per codeword, one bit per data module, plus
        // the fixed 2×2 corner on the sizes whose walk leaves one — which pins the table against a
        // transcription error in any row.
        foreach (var size in DataMatrixEncoder.Sizes)
        {
            var placement = new DataMatrixEncoder.Placement(size.MappingRows, size.MappingColumns);
            placement.Run();
            int corner = placement.LeavesCornerUnfilled ? 4 : 0;
            Assert.AreEqual(size.MappingRows * size.MappingColumns, (size.DataCodewords + size.EccCodewords) * 8 + corner, size.ToString());
        }

        Assert.AreEqual(30, DataMatrixEncoder.Sizes.Length);
    }

    private static DataMatrixSymbol Encode(string text, DataMatrixOptions? options = null)
    {
        Assert.IsTrue(DataMatrixEncoder.TryEncode(text, options ?? DataMatrixOptions.Default, out var symbol, out var error), error);
        return symbol!;
    }
}

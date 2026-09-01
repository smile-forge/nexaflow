using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Qr;

namespace Nexaflow.Tests.Visuals.Markdown.Qr;

/// <summary>
/// The encoder against the specification, in two halves.
///
/// <para>
/// The round trips ask whether a symbol can be read back: <see cref="QrTestDecoder"/> undoes the mask,
/// walks the zigzag, checks every block's ReedΓÇôSolomon parity and hands back the string. That covers
/// placement, masking and parity together, which is most of what can go wrong.
/// </para>
///
/// <para>
/// It cannot cover the specification's own tables, though ΓÇö a symbol built from a wrong capacity row
/// and read back the same way still round-trips. So the rest of these are anchored on published
/// figures: byte-mode capacities, alignment-pattern centres, and format code words. Between them they
/// pin both tables at each end and the arithmetic in the middle.
/// </para>
/// </summary>
[TestClass]
[CoversNode("qr-encoder")]
public class QrEncoderTests
{
    // ΓöÇΓöÇ Round trips ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    [TestMethod]
    public void RoundTrip_ByteMode_AtEveryErrorCorrectionLevel()
    {
        const string payload = "https://markdown.org/tools/diagrams/qr/";

        foreach (var ecl in Enum.GetValues<QrErrorCorrection>())
        {
            var matrix = QrEncoder.Encode(payload, ecl);
            Assert.AreEqual(payload, QrTestDecoder.Decode(matrix), $"at error correction {ecl}");
            Assert.AreEqual(ecl, matrix.ErrorCorrection);
        }
    }

    [TestMethod]
    public void RoundTrip_NumericAndAlphanumericModes_UseTheNarrowerEncoding()
    {
        // A digit string and an upper-case string encode more densely than raw bytes; the proof that
        // the narrower mode was really used is that the same payload needs a smaller symbol.
        var numeric = QrEncoder.Encode(new string('7', 60), QrErrorCorrection.Medium);
        var bytes   = QrEncoder.Encode(new string('a', 60), QrErrorCorrection.Medium);

        Assert.AreEqual(new string('7', 60), QrTestDecoder.Decode(numeric));
        Assert.IsTrue(numeric.Version < bytes.Version,
            $"numeric took version {numeric.Version}, byte mode {bytes.Version}");

        var alnum = QrEncoder.Encode("HELLO WORLD 123", QrErrorCorrection.Quartile);
        Assert.AreEqual("HELLO WORLD 123", QrTestDecoder.Decode(alnum));
    }

    [TestMethod]
    public void RoundTrip_GrowsThroughEveryVersion()
    {
        // Walks the payload up until the symbol reaches version 40, so every version ΓÇö and with it
        // every row of the block tables, and both character-count widths ΓÇö is built and read back.
        int seen = 0;
        int version = 0;

        for (int length = 1; length <= 1300 && version < QrEncoder.MaxVersion; length += 7)
        {
            string payload = string.Create(length, length, (span, _) =>
            {
                for (int i = 0; i < span.Length; i++) span[i] = (char)('a' + i % 26);
            });

            var matrix = QrEncoder.Encode(payload, QrErrorCorrection.High);
            Assert.AreEqual(payload, QrTestDecoder.Decode(matrix), $"payload of {length} characters");

            if (matrix.Version > version) { version = matrix.Version; seen++; }
        }

        Assert.AreEqual(QrEncoder.MaxVersion, version, "the walk should reach version 40");
        Assert.AreEqual(QrEncoder.MaxVersion, seen, "every version should have been used on the way");
    }

    [TestMethod]
    public void RoundTrip_KeepsNonAsciiIntact()
    {
        // Byte mode carries UTF-8, so anything outside ASCII is only correct if the byte count ΓÇö not
        // the character count ΓÇö went into the header.
        const string payload = "Gr├╝├ƒe ┬╖ µùÑµ£¼Φ¬₧ ┬╖ emoji ≡ƒÄë";

        Assert.AreEqual(payload, QrTestDecoder.Decode(QrEncoder.Encode(payload, QrErrorCorrection.Medium)));
    }

    [TestMethod]
    public void RoundTrip_EmptyPayload_StillProducesAReadableSymbol()
    {
        var matrix = QrEncoder.Encode(string.Empty, QrErrorCorrection.Low);

        Assert.AreEqual(1, matrix.Version);
        Assert.AreEqual(string.Empty, QrTestDecoder.Decode(matrix));
    }

    // ΓöÇΓöÇ Anchored on the published specification ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    [TestMethod]
    public void Capacity_MatchesPublishedByteModeLimits()
    {
        // Version, then the byte-mode character limit at L, M, Q and H. These are the specification's
        // own figures, and they pin both block tables: the capacity is the total codewords for the
        // version less the error-correction codewords per block times the number of blocks.
        (int Version, int[] Limits)[] published =
        [
            (1,  [  17,   14,   11,    7]),
            (2,  [  32,   26,   20,   14]),
            (10, [ 271,  213,  151,  119]),
            (40, [2953, 2331, 1663, 1273]),
        ];

        foreach (var (version, limits) in published)
        {
            for (int level = 0; level < 4; level++)
            {
                var ecl = (QrErrorCorrection)level;
                int headerBits = 4 + (version < 10 ? 8 : 16);
                int actual = (QrEncoder.DataCodewords(version, ecl) * 8 - headerBits) / 8;

                Assert.AreEqual(limits[level], actual, $"version {version} at {ecl}");
            }
        }
    }

    [TestMethod]
    public void Capacity_IsExactlyReached_WithoutSpillingToTheNextVersion()
    {
        // The boundary the capacity table describes: 17 bytes fit a version-1 L symbol and 18 do not.
        Assert.AreEqual(1, QrEncoder.Encode(new string('a', 17), QrErrorCorrection.Low).Version);
        Assert.AreEqual(2, QrEncoder.Encode(new string('a', 18), QrErrorCorrection.Low).Version);
    }

    [TestMethod]
    public void AlignmentPatterns_SitAtThePublishedCentres()
    {
        // The specification's Annex E rows. Version 1 has none; 32 is the one version whose spacing
        // does not follow the general rule.
        (int Version, int[] Centres)[] published =
        [
            (1,  []),
            (2,  [6, 18]),
            (7,  [6, 22, 38]),
            (32, [6, 34, 60, 86, 112, 138]),
            (40, [6, 30, 58, 86, 114, 142, 170]),
        ];

        foreach (var (version, centres) in published)
        {
            CollectionAssert.AreEqual(centres, QrTestDecoder.AlignmentPatternCentres(version).ToArray(),
                $"version {version}");
        }

        // And they are really drawn there: an alignment pattern is a dark 5├ù5 ring around a dark centre.
        // 20 bytes: past version 1's 17 at level L, so this is the first version with an alignment pattern.
        var matrix = QrEncoder.Encode("alignment patterns!!", QrErrorCorrection.Low);
        Assert.AreEqual(2, matrix.Version, "this payload should land on version 2");

        Assert.IsTrue(matrix[18, 18], "the centre module of the version-2 alignment pattern");
        Assert.IsFalse(matrix[17, 18], "the light ring around it");
        Assert.IsTrue(matrix[16, 18], "the dark ring around that");
    }

    [TestMethod]
    public void FormatInformation_MatchesThePublishedCodeWords()
    {
        // Table C.1, mask pattern 0, for L / M / Q / H in turn.
        string[] published =
        [
            "111011111000100",   // L
            "101010000010010",   // M
            "011010101011111",   // Q
            "001011010001001",   // H
        ];
        int[] levelBits = [1, 0, 3, 2];

        for (int level = 0; level < 4; level++)
        {
            string actual = Convert.ToString(QrTestDecoder.FormatCodeWord(levelBits[level], 0), 2)
                                   .PadLeft(15, '0');

            Assert.AreEqual(published[level], actual, $"format code word for level index {level}");
        }
    }

    [TestMethod]
    public void FunctionPatterns_AreWhereAReaderLooksForThem()
    {
        var matrix = QrEncoder.Encode("function patterns", QrErrorCorrection.Medium);
        int size = matrix.Size;

        Assert.AreEqual(matrix.Version * 4 + 17, size);

        // Each finder is a 7├ù7 ring-in-a-ring, fenced off by a light separator.
        foreach (var (ox, oy) in new[] { (0, 0), (size - 7, 0), (0, size - 7) })
        {
            for (int dy = 0; dy < 7; dy++)
            {
                for (int dx = 0; dx < 7; dx++)
                {
                    int ring = Math.Max(Math.Abs(dx - 3), Math.Abs(dy - 3));
                    Assert.AreEqual(ring != 2, matrix[ox + dx, oy + dy], $"finder at ({ox},{oy}) module ({dx},{dy})");
                }
            }
        }

        // The timing patterns run between the finders, dark at every even coordinate.
        for (int i = 8; i < size - 8; i++)
        {
            Assert.AreEqual(i % 2 == 0, matrix[6, i], $"vertical timing module at y={i}");
            Assert.AreEqual(i % 2 == 0, matrix[i, 6], $"horizontal timing module at x={i}");
        }

        Assert.IsTrue(matrix[8, size - 8], "the module beside the bottom-left finder is always dark");
    }

    // ΓöÇΓöÇ Failure ΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇΓöÇ

    [TestMethod]
    public void TooMuchData_IsReported_NotThrown()
    {
        bool encoded = QrEncoder.TryEncode(new string('a', 3000), QrErrorCorrection.High,
                                           out var matrix, out string? error);

        Assert.IsFalse(encoded);
        Assert.IsNull(matrix);
        StringAssert.Contains(error!, "version-40");
        StringAssert.Contains(error!, "ec");
    }

    [TestMethod]
    public void HigherErrorCorrection_CostsCapacity()
    {
        string payload = new('a', 200);

        Assert.IsTrue(QrEncoder.Encode(payload, QrErrorCorrection.High).Version
                    > QrEncoder.Encode(payload, QrErrorCorrection.Low).Version,
            "the same payload should need a larger symbol at H than at L");
    }
}

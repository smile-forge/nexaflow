using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Barcode;

namespace Nexaflow.Tests.Visuals.Markdown.Barcode;

/// <summary>
/// The symbology encoders, anchored from outside wherever a published value exists.
///
/// <para>
/// A barcode's tables cannot be checked by round-tripping alone — a wrong pattern read back through the
/// same wrong table agrees with itself. So the anchors here are values that exist in the world: the
/// example codes the block syntax documents are real retail numbers, and their last digit is a check
/// digit somebody else computed. Reproducing it exercises the arithmetic and the digit order at once.
/// </para>
/// <para>
/// Module counts are the second anchor. Every one of these formats has a width fixed by its structure —
/// an EAN-13 is 95 modules whatever the digits — so a count that comes out right means the guards, the
/// digit widths and the count of them are all as the standard says.
/// </para>
/// </summary>
[TestClass]
[CoversNode("barcode-symbologies")]
public class BarcodeEncoderTests
{
    private static BarcodePattern Encode(BarcodeSymbology symbology, string value)
    {
        Assert.IsTrue(BarcodeEncoder.TryEncode(symbology, value, out var pattern, out string? error),
            $"{symbology} '{value}': {error}");
        return pattern!;
    }

    private static string Rejects(BarcodeSymbology symbology, string value)
    {
        Assert.IsFalse(BarcodeEncoder.TryEncode(symbology, value, out _, out string? error),
            $"expected {symbology} to refuse '{value}'");
        Assert.IsFalse(string.IsNullOrWhiteSpace(error), "a refusal should say why");
        return error!;
    }

    // ── Anchored on published codes ────────────────────────────────────────

    [TestMethod]
    public void CheckDigits_MatchPublishedRetailCodes()
    {
        // Each of these is a real code whose final digit was computed by someone else; leaving it off
        // must reproduce it exactly.
        (BarcodeSymbology Symbology, string Full)[] published =
        [
            (BarcodeSymbology.Ean13, "5901234123457"),
            (BarcodeSymbology.Ean8,  "96385074"),
            (BarcodeSymbology.Upc,   "036000291452"),
        ];

        foreach (var (symbology, full) in published)
        {
            // Written without its check digit, the encoder must add the same one back.
            Assert.AreEqual(full, Encode(symbology, full[..^1]).Text, $"{symbology} check digit");

            // Written with it, it is accepted as-is...
            Assert.AreEqual(full, Encode(symbology, full).Text);

            // ...and a wrong one is refused rather than drawn, because a barcode that scans as the wrong
            // product is worse than one that does not scan.
            char wrong = full[^1] == '0' ? '1' : '0';
            StringAssert.Contains(Rejects(symbology, full[..^1] + wrong), "check digit");
        }
    }

    [TestMethod]
    public void StructuralWidths_AreWhatTheStandardsFix()
    {
        // Guards and digit widths together fix these, whatever the value.
        Assert.AreEqual(95, Encode(BarcodeSymbology.Ean13, "5901234123457").Width, "EAN-13");
        Assert.AreEqual(95, Encode(BarcodeSymbology.Upc,   "036000291452").Width, "UPC-A");
        Assert.AreEqual(67, Encode(BarcodeSymbology.Ean8,  "96385074").Width,     "EAN-8");
        Assert.AreEqual(51, Encode(BarcodeSymbology.UpcE,  "01234565").Width,     "UPC-E");
        // The add-ons: a five-module guard, seven per digit, and a two-module separator between each.
        Assert.AreEqual(5 + 5 * 7 + 4 * 2, Encode(BarcodeSymbology.Ean5, "12345").Width, "EAN-5 add-on");
        Assert.AreEqual(5 + 2 * 7 + 1 * 2, Encode(BarcodeSymbology.Ean2, "12").Width,    "EAN-2 add-on");

        // Code 128 is eleven modules a symbol plus a thirteen-module stop. "12345678" in subset C is
        // four data symbols, so start + 4 + check = 6 symbols and the stop.
        Assert.AreEqual(6 * 11 + 13, Encode(BarcodeSymbology.Code128C, "12345678").Width, "CODE128C");

        // The same digits under CODE128 pick subset C too, being an even run.
        Assert.AreEqual(Encode(BarcodeSymbology.Code128C, "12345678").ToString(),
                        Encode(BarcodeSymbology.Code128,  "12345678").ToString(),
                        "CODE128 should choose C for an even digit run");
    }

    [TestMethod]
    public void EveryBarcodeStartsAndEndsWithInk()
    {
        // A symbol that began or ended with paper would have no edge for a scanner to find — the quiet zone
        // is the renderer's, and the pattern itself has to be bounded by bars.
        //
        // The add-ons are the exception, and deliberately: they are printed to the right of a main symbol,
        // and their 01011 guard opens with a space precisely so it cannot run into it.
        foreach (var (symbology, value) in Samples)
        {
            if (symbology is BarcodeSymbology.Ean5 or BarcodeSymbology.Ean2) continue;

            var pattern = Encode(symbology, value);
            Assert.IsTrue(pattern[0], $"{symbology} should start with a bar");
            Assert.IsTrue(pattern[pattern.Width - 1], $"{symbology} should end with a bar");
        }

        foreach (var symbology in new[] { BarcodeSymbology.Ean5, BarcodeSymbology.Ean2 })
        {
            var pattern = Encode(symbology, symbology == BarcodeSymbology.Ean5 ? "12345" : "12");
            Assert.IsFalse(pattern[0], $"{symbology}'s guard opens with a space");
            Assert.IsTrue(pattern[pattern.Width - 1], $"{symbology} should still end with a bar");
        }
    }

    [TestMethod]
    public void EverySymbologyEncodesItsDocumentedExample()
    {
        foreach (var (symbology, value) in Samples)
            Assert.IsTrue(Encode(symbology, value).Width > 0, $"{symbology} produced nothing");
    }

    /// <summary>One value per symbology — the examples the block syntax documents.</summary>
    private static readonly (BarcodeSymbology Symbology, string Value)[] Samples =
    [
        (BarcodeSymbology.Code128,  "MARKDOWN-128"),
        (BarcodeSymbology.Code128A, "MARKDOWN128A"),
        (BarcodeSymbology.Code128B, "Markdown 128B"),
        (BarcodeSymbology.Code128C, "12345678"),
        (BarcodeSymbology.Ean13,    "5901234123457"),
        (BarcodeSymbology.Ean8,     "96385074"),
        (BarcodeSymbology.Ean5,     "12345"),
        (BarcodeSymbology.Ean2,     "12"),
        (BarcodeSymbology.Upc,      "036000291452"),
        (BarcodeSymbology.UpcE,     "01234565"),
        (BarcodeSymbology.Code39,   "MARKDOWN-39"),
        (BarcodeSymbology.Itf,      "1234567890"),
        (BarcodeSymbology.Itf14,    "1234567890123"),
        (BarcodeSymbology.Msi,      "1234567"),
        (BarcodeSymbology.Msi10,    "1234567"),
        (BarcodeSymbology.Msi11,    "1234567"),
        (BarcodeSymbology.Msi1010,  "1234567"),
        (BarcodeSymbology.Msi1110,  "1234567"),
        (BarcodeSymbology.Pharmacode, "1234"),
        (BarcodeSymbology.Codabar,  "A40156B"),
    ];

    // ── What each format will not carry ────────────────────────────────────

    [TestMethod]
    public void EachFormatRefusesWhatItCannotCarry()
    {
        StringAssert.Contains(Rejects(BarcodeSymbology.Code128A, "lower case"), "subset A");
        StringAssert.Contains(Rejects(BarcodeSymbology.Code128C, "1234567"),    "even");
        StringAssert.Contains(Rejects(BarcodeSymbology.Code128C, "12ab"),       "digits");
        StringAssert.Contains(Rejects(BarcodeSymbology.Code39,   "lower~case"), "not in Code 39");
        StringAssert.Contains(Rejects(BarcodeSymbology.Itf,      "12345"),      "even");
        StringAssert.Contains(Rejects(BarcodeSymbology.Ean13,    "123"),        "12 digits");
        StringAssert.Contains(Rejects(BarcodeSymbology.Pharmacode, "2"),        "3 to 131070");
        StringAssert.Contains(Rejects(BarcodeSymbology.Pharmacode, "999999"),   "3 to 131070");
        StringAssert.Contains(Rejects(BarcodeSymbology.Codabar,   "A12A34A"),   "start/stop");
    }

    [TestMethod]
    public void Code39_FoldsLowerCaseRatherThanRefusingIt() =>
        // Lower case is not a different character in Code 39, it is simply absent — folding is what a
        // reader of this format expects, and it is what goes under the bars.
        Assert.AreEqual("MARKDOWN-39", Encode(BarcodeSymbology.Code39, "markdown-39").Text);

    [TestMethod]
    public void Codabar_WrapsAValueThatBroughtNoStartStopMark()
    {
        Assert.AreEqual("A40156B", Encode(BarcodeSymbology.Codabar, "A40156B").Text);
        // Wrapped in A...B when the value brings no marks of its own — which pair is a free choice in
        // Codabar, and this is the one the generators make.
        Assert.AreEqual("A40156B", Encode(BarcodeSymbology.Codabar, "40156").Text);
    }

    [TestMethod]
    public void Msi_AppendsTheCheckDigitsItsNameAsksFor()
    {
        // The value passes through; what differs is how many check digits follow it.
        Assert.AreEqual("1234567",   Encode(BarcodeSymbology.Msi,     "1234567").Text);
        Assert.AreEqual(8,           Encode(BarcodeSymbology.Msi10,   "1234567").Text.Length);
        Assert.AreEqual(8,           Encode(BarcodeSymbology.Msi11,   "1234567").Text.Length);
        Assert.AreEqual(9,           Encode(BarcodeSymbology.Msi1010, "1234567").Text.Length);
        Assert.AreEqual(9,           Encode(BarcodeSymbology.Msi1110, "1234567").Text.Length);
    }

    [TestMethod]
    public void FormatNames_AllParse()
    {
        foreach (string name in BarcodeEncoder.FormatNames)
            Assert.IsTrue(BarcodeEncoder.TryParseSymbology(name, out _), name);

        // Spelling variations a writer would reasonably reach for.
        Assert.IsTrue(BarcodeEncoder.TryParseSymbology("code-128", out var dashed));
        Assert.AreEqual(BarcodeSymbology.Code128, dashed);
        Assert.IsTrue(BarcodeEncoder.TryParseSymbology("upca", out var upca));
        Assert.AreEqual(BarcodeSymbology.Upc, upca);
        Assert.IsFalse(BarcodeEncoder.TryParseSymbology("qr", out _));
    }

    [TestMethod]
    public void PublicationNumbersBecomeTheEan13TheyArePrintedAs()
    {
        // Each of these is a numbering scheme that reserved an EAN-13 prefix, so the test is whether the
        // right thirteen digits come out. An ISBN-13's own check digit is the EAN check digit, and an
        // ISMN's likewise — so reproducing the number as printed on the book is the whole assertion.
        Assert.AreEqual("9781565812314", Encode(BarcodeSymbology.Isbn, "978-1-56581-231-4").Text);
        Assert.AreEqual("9790260532113", Encode(BarcodeSymbology.Ismn, "979-0-2605-3211-3").Text);

        // An ISSN is 977, the seven digits that identify the serial, and a two-digit issue variant. Its own
        // trailing check character never reaches the bars — the EAN check digit replaces it, which is why an
        // ISSN ending in X still encodes.
        Assert.AreEqual("9770311175001", Encode(BarcodeSymbology.Issn, "0311-175X").Text);

        // A ten-digit ISBN predates the EAN and is promoted: prefix 978, keep the nine identifying digits,
        // and compute a new check digit, because the old one checked a different number.
        StringAssert.StartsWith(Encode(BarcodeSymbology.Isbn, "0-306-40615-2").Text, "9780306406");
    }

    [TestMethod]
    public void APublicationAddOnIsDrawnBesideTheMainSymbol()
    {
        // The little block of extra bars carrying a price on a book or an issue number on a journal. It is
        // a separate symbol with its own guard, set apart by a gap wide enough that a scanner reads two
        // symbols rather than one long one.
        var plain = Encode(BarcodeSymbology.Isbn, "978-1-56581-231-4");
        var priced = Encode(BarcodeSymbology.Isbn, "978-1-56581-231-4 90000");

        Assert.AreEqual(95, plain.Width, "an EAN-13 on its own");
        Assert.IsTrue(priced.Width > plain.Width + 40, "a five-digit add-on and its gap should follow it");
        Assert.AreEqual("9781565812314 90000", priced.Text);

        var issue = Encode(BarcodeSymbology.Issn, "0311-175X 00 17");
        Assert.AreEqual("9770311175001 17", issue.Text);
        Assert.IsTrue(issue.Width > plain.Width, "a two-digit add-on should follow it too");
    }

    [TestMethod]
    public void PublicationNumbersRefuseWhatIsNotOne()
    {
        StringAssert.Contains(Rejects(BarcodeSymbology.Isbn, "123"),          "ISBN");
        StringAssert.Contains(Rejects(BarcodeSymbology.Ismn, "978-1-2-3"),    "ISMN");
        StringAssert.Contains(Rejects(BarcodeSymbology.Issn, "0311"),         "ISSN");
        StringAssert.Contains(Rejects(BarcodeSymbology.Isbn, "978-1-56581-231-4 999"), "add-on");
    }
}

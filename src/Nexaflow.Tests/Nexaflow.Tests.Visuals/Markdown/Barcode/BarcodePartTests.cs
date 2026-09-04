using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Barcode;

namespace Nexaflow.Tests.Visuals.Markdown.Barcode;

/// <summary>
/// What a symbol's text says, and — the point of the type — which of it the author actually typed.
///
/// <para>
/// These formats do not print what they are given. Codabar puts a start and a stop mark around it, an
/// EAN-13 works out a thirteenth digit, a UPC-E fills in both ends, an ISBN takes the hyphens out and
/// puts a scheme's name on top. What is printed is then one string to look at and several to reason
/// about, and the tree's job is to say which of those pieces an edit could be applied to rather than to
/// guess. Everything here is about that boundary.
/// </para>
/// </summary>
[TestClass]
[CoversNode("symbol-tree")]
public class BarcodePartTests
{
    private static BarcodePart Read(BarcodeSymbology symbology, string value)
    {
        Assert.IsTrue(BarcodeEncoder.TryEncode(symbology, value, out var pattern, out string? error),
            $"{symbology} '{value}': {error}");
        Assert.IsNotNull(pattern!.Symbol, "an encoded pattern always knows what it is made of");
        return pattern.Symbol!;
    }

    private static BarcodePart[] Labels(BarcodePart symbol) =>
        [.. symbol.Children.Where(c => c.Role is BarcodeRole.Label or BarcodeRole.AddOn)];

    /// <summary>The leaves of a run, in printed order, as "kind:text".</summary>
    private static string[] Pieces(BarcodePart part) =>
        [.. part.SelfAndDescendants()
              .Where(p => p.Children.Count == 0)
              .Select(p => $"{p.Kind}:{p.Printed}")];

    // ── One string to look at, several to reason about ─────────────────────

    [TestMethod]
    public void CodabarBracketsTheValueInMarksNobodyTyped()
    {
        // The case that shows the shape of the whole thing. What is drawn reads as one run of characters
        // and is three: a start mark, the value, a stop mark. Only the middle one can be edited, and only
        // because it really is what was typed.
        var symbol = Read(BarcodeSymbology.Codabar, "12345");

        CollectionAssert.AreEqual(
            new[] { "EncodedText:A", "Character:1", "Character:2", "Character:3", "Character:4",
                    "Character:5", "EncodedText:B" },
            Pieces(Labels(symbol).Single()));

        Assert.AreEqual("12345", symbol.Written(), "and the middle is the value, entire");
    }

    [TestMethod]
    public void CodabarGivenItsOwnMarksPrintsExactlyWhatWasTyped()
    {
        var symbol = Read(BarcodeSymbology.Codabar, "A12345B");

        Assert.AreEqual("A12345B", symbol.Written());
        Assert.IsFalse(symbol.SelfAndDescendants().Any(p => p.Kind == BarcodeKind.EncodedText),
            "nothing was added, so nothing is encoded");
    }

    [TestMethod]
    public void AnEan13WorksOutItsLastDigitAndNotTheOthers()
    {
        // Twelve typed, thirteen printed. The twelve are the value and the thirteenth is a fact about all
        // of them — so the check digit stands for the whole value rather than for the digit beside it.
        const string value = "590123412345";
        var symbol = Read(BarcodeSymbology.Ean13, value);

        Assert.AreEqual(value, symbol.Written(), "every digit that was typed is still the value");

        var check = symbol.SelfAndDescendants().Single(p => p.Kind == BarcodeKind.EncodedText);
        Assert.AreEqual("7", check.Printed);
        Assert.AreEqual(0, check.Start);
        Assert.AreEqual(value.Length, check.Length, "worked out from all of it");
    }

    [TestMethod]
    public void AUpcEFillsInBothEndsAndLeavesTheMiddleAlone()
    {
        // A UPC-E prints its number system outside the bars on the left and its check digit outside on the
        // right, so the three pieces are also three printed runs — the split falls where the format was
        // already going to break the line.
        var symbol = Read(BarcodeSymbology.UpcE, "012345");

        var labels = Labels(symbol);
        Assert.AreEqual(3, labels.Length, "number system, body, check digit");

        Assert.AreEqual(BarcodeKind.EncodedText, labels[0].Kind, "the number system was filled in");
        Assert.AreEqual(BarcodeKind.EncodedText, labels[2].Kind, "and so was the check digit");

        CollectionAssert.AreEqual(
            new[] { "Character:0", "Character:1", "Character:2", "Character:3", "Character:4", "Character:5" },
            Pieces(labels[1]),
            "while the six digits between them are the ones that were typed");

        Assert.AreEqual("012345", symbol.Written());
    }

    // ── Where what is printed is what was typed ────────────────────────────

    [TestMethod]
    public void ACode128PrintsTheValueSoEveryCharacterOfItIsSource()
    {
        const string value = "HELLO123";
        var symbol = Read(BarcodeSymbology.Code128, value);

        var characters = symbol.SelfAndDescendants().Where(p => p.IsSource).ToList();
        Assert.AreEqual(value.Length, characters.Count, "one node per character of the value");
        Assert.AreEqual(value, symbol.Written());

        for (int i = 0; i < value.Length; i++)
        {
            Assert.AreEqual(i, characters[i].Start);
            Assert.AreEqual(1, characters[i].Length, "a character covers exactly one character");
        }
    }

    [TestMethod]
    public void AnEan13TypedInFullIsPrintedAsTyped()
    {
        const string value = "5901234123457";
        var symbol = Read(BarcodeSymbology.Ean13, value);

        Assert.AreEqual(value, symbol.Written());
        Assert.IsFalse(symbol.SelfAndDescendants().Any(p => p.Kind == BarcodeKind.EncodedText));
    }

    // ── Where none of it is ────────────────────────────────────────────────

    [TestMethod]
    public void AFormatThatRearrangesItsInputOffersNoneOfItAsSource()
    {
        // Not everything that transforms merely adds to an end. An ISBN's printed digits are the value
        // with its hyphens taken out, so the value is nowhere in them in one piece — and a piece that
        // cannot say which characters it is had better not claim any.
        var isbn = Read(BarcodeSymbology.Isbn, "978-1-56581-231-4");

        foreach (var label in Labels(isbn))
            Assert.IsFalse(label.SelfAndDescendants().Any(p => p.IsSource),
                "the digits under the bars are a rendering of the number, not the number");

        // Code 39 upper-cases, so a lower-case value is nowhere in what is printed either.
        var code39 = Read(BarcodeSymbology.Code39, "abc123");
        foreach (var label in Labels(code39))
            Assert.IsFalse(label.SelfAndDescendants().Any(p => p.IsSource));
    }

    [TestMethod]
    public void APublicationsCaptionIsTheValueEvenThoughItsDigitsAreNot()
    {
        // The caption carries the number as it was written, hyphens and all, which makes it the one place
        // a publication's value appears as itself — and so the one place it could be edited.
        const string value = "978-1-56581-231-4";
        var symbol = Read(BarcodeSymbology.Isbn, value);

        var caption = symbol.Children.Single(c => c.Kind == BarcodeKind.Caption);

        Assert.AreEqual(BarcodeKind.EncodedText, caption.Children.First().Kind, "nobody typed the scheme's name");
        Assert.AreEqual("ISBN ", caption.Children.First().Printed);
        Assert.AreEqual(value, caption.Written(), "and the rest of the line is the value, character for character");
    }

    // ── The boundary with the layout ───────────────────────────────────────

    [TestMethod]
    public void TheBarsThemselvesAreNotPartOfWhatTheSymbolSays()
    {
        // No piece of what an author typed is a guard pattern. The bars are how a value is drawn, so they
        // belong to the layout built from this and not to this — which is also why the layout has more
        // pieces in it than the tree does, and why neither is derivable from the other.
        var symbol = Read(BarcodeSymbology.Ean13, "5901234123457");

        Assert.IsTrue(
            symbol.SelfAndDescendants().All(p => p.Kind is BarcodeKind.Symbol or BarcodeKind.Caption
                                                        or BarcodeKind.Group or BarcodeKind.Character
                                                        or BarcodeKind.EncodedText),
            "the parse tree is the symbol's text and nothing else");
    }

    // ── Invariants ────────────────────────────────────────────────────────

    [TestMethod]
    public void NoPieceEverClaimsCharactersTheValueHasNotGot()
    {
        (BarcodeSymbology Symbology, string Value)[] cases =
        [
            (BarcodeSymbology.Code128, "HELLO123"),
            (BarcodeSymbology.Code39, "ABC-123"),
            (BarcodeSymbology.Code39, "abc123"),
            (BarcodeSymbology.Ean13, "590123412345"),
            (BarcodeSymbology.Ean13, "5901234123457"),
            (BarcodeSymbology.Ean8, "96385074"),
            (BarcodeSymbology.Upc, "036000291452"),
            (BarcodeSymbology.UpcE, "012345"),
            (BarcodeSymbology.UpcE, "01234565"),
            (BarcodeSymbology.Isbn, "978-1-56581-231-4"),
            (BarcodeSymbology.Itf, "12345678"),
            (BarcodeSymbology.Itf14, "1234567890123"),
            (BarcodeSymbology.Msi10, "1234567"),
            (BarcodeSymbology.Codabar, "12345"),
            (BarcodeSymbology.Codabar, "A12345B"),
            (BarcodeSymbology.Pharmacode, "1234"),
        ];

        foreach (var (symbology, value) in cases)
        {
            var symbol = Read(symbology, value);

            foreach (var piece in symbol.SelfAndDescendants())
            {
                Assert.IsTrue(piece.Start >= 0 && piece.End <= value.Length,
                    $"{symbology} '{value}': {piece} reaches outside the value");

                if (piece.IsSource)
                    Assert.AreEqual(value.Substring(piece.Start, piece.Length), piece.Text,
                        $"{symbology} '{value}': {piece} says it is source but is not what is there");
            }

            // What it claims as source is the value entire or none of it — never a scattering of it, which
            // is what an edit spliced against a partial claim would corrupt.
            var written = symbol.Written();
            Assert.IsTrue(written.Length == 0 || written == value,
                $"{symbology} '{value}': claimed source '{written}' is neither all of the value nor none");
        }
    }
}

using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Barcode;

namespace Nexaflow.Tests.Visuals.Markdown.Barcode;

/// <summary>
/// What a symbol is made of, and — the point of the type — which of it the author actually typed.
///
/// <para>
/// These formats do not print what they are given. An EAN-13 works out a thirteenth digit, an ISBN takes
/// the hyphens out and puts a scheme's name on top, a UPC-E fills in both ends. So "which characters of
/// the value is this digit" has a real answer only sometimes, and the tree's job is to say which times
/// those are rather than to guess. Everything here is about that boundary.
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

    private static BarcodePart[] Characters(BarcodePart part) =>
        [.. part.SelfAndDescendants().Where(p => p.Kind == BarcodeKind.Character)];

    // ── Where what is printed is what was typed ────────────────────────────

    [TestMethod]
    public void ACode128PrintsTheValueSoEveryCharacterOfItIsSource()
    {
        const string value = "HELLO123";
        var symbol = Read(BarcodeSymbology.Code128, value);

        var characters = Characters(symbol);
        Assert.AreEqual(value.Length, characters.Length, "one node per character of the value");
        Assert.AreEqual(value, symbol.Written(), "and together they are the value");

        for (int i = 0; i < value.Length; i++)
        {
            Assert.AreEqual(i, characters[i].Start);
            Assert.AreEqual(1, characters[i].Length, "a character covers exactly one character");
            Assert.AreEqual(value[i].ToString(), characters[i].Text);
        }

        Assert.IsFalse(
            Labels(symbol).SelectMany(l => l.SelfAndDescendants()).Any(p => p.Kind == BarcodeKind.EncodedText),
            "nothing about a Code128's number is worked out, so nothing in it is encoded");
    }

    [TestMethod]
    public void AnEan13TypedInFullIsPrintedAsTyped()
    {
        // Thirteen digits with the check digit already right: the encoder passes it straight through, so
        // what is on the page is what was typed and every digit of it is somewhere a caret could stand.
        const string value = "5901234123457";
        var symbol = Read(BarcodeSymbology.Ean13, value);

        Assert.AreEqual(value, symbol.Written());
        Assert.AreEqual(value.Length, Characters(symbol).Length);
    }

    // ── Where it is not ───────────────────────────────────────────────────

    [TestMethod]
    public void AnEan13MissingItsCheckDigitPrintsSomethingNobodyTyped()
    {
        // Twelve digits in, thirteen out. The printed number is no longer the value — it is one character
        // longer — so none of it is offered as source, and the whole of it stands for the whole value.
        const string value = "590123412345";
        var symbol = Read(BarcodeSymbology.Ean13, value);

        Assert.AreEqual(string.Empty, symbol.Written(), "none of what is printed is the value");
        Assert.AreEqual(0, Characters(symbol).Length);

        var encoded = Labels(symbol).SelectMany(l => l.SelfAndDescendants())
                                    .Where(p => p.Kind == BarcodeKind.EncodedText)
                                    .ToList();

        Assert.AreNotEqual(0, encoded.Count, "what is printed is encoded, and says so");
        foreach (var piece in encoded)
        {
            Assert.AreEqual(0, piece.Start, "it was worked out from the whole value");
            Assert.AreEqual(value.Length, piece.Length);
        }
    }

    [TestMethod]
    public void TheGroupingOfAPrintedNumberDoesNotMakePartOfItEditable()
    {
        // An EAN-13 prints one digit outside the bars and then six under each half. With the check digit
        // added, the first seven of those happen to sit at the same offsets as the value — and calling
        // them source because of that would make editability an accident of where the guard bars fall.
        var symbol = Read(BarcodeSymbology.Ean13, "590123412345");

        Assert.IsFalse(symbol.SelfAndDescendants().Any(p => p.IsSource),
            "either the printed number is the value or it is a rendering of it, never partly each");
    }

    [TestMethod]
    public void APublicationsCaptionIsTheValueAndItsPrintedDigitsAreNot()
    {
        // The case the split exists for. The caption carries the number as it was written, hyphens and
        // all, so it is genuinely editable; the digits under the bars are that number with the hyphens
        // taken out and a check digit added, so they are a rendering of it.
        const string value = "978-1-56581-231-4";
        var symbol = Read(BarcodeSymbology.Isbn, value);

        var caption = symbol.Children.SingleOrDefault(c => c.Kind == BarcodeKind.Caption);
        Assert.IsNotNull(caption, "a publication is captioned");

        var scheme = caption!.Children.First();
        Assert.AreEqual(BarcodeKind.EncodedText, scheme.Kind, "nobody typed the scheme's name");
        Assert.AreEqual("ISBN ", scheme.Text);

        Assert.AreEqual(value, caption.Written(), "the number in the caption is the value, character for character");

        foreach (var label in Labels(symbol))
            Assert.IsFalse(label.SelfAndDescendants().Any(p => p.IsSource),
                "and nothing under the bars is");
    }

    /// <summary>
    /// A UPC-E takes six digits, or eight with the number system and check digit already on it. The same
    /// format is therefore encoded or not depending on what it was given, which is the clearest statement
    /// that this is a question about the value and not about the symbology.
    /// </summary>
    [TestMethod]
    public void AUpcEIsSourceOrNotDependingOnWhetherItWasGivenBothEnds()
    {
        var filled = Read(BarcodeSymbology.UpcE, "012345");
        Assert.AreEqual(string.Empty, filled.Written(),
            "given six digits it prints eight, and the two it added are not the only difference - "
            + "the six that were typed have moved along one");

        var whole = Read(BarcodeSymbology.UpcE, "01234565");
        Assert.AreEqual("01234565", whole.Written(),
            "given all eight it prints them, so all eight are the value");
    }

    // ── The bars themselves ───────────────────────────────────────────────

    [TestMethod]
    public void TheBarsStandForTheWholeValueAndTheirGuardsForNoneOfIt()
    {
        // A guard is the format's own punctuation — it marks where the symbol begins and where its halves
        // divide — so it stands for nothing anybody wrote, and a press on one resolves outwards to the
        // bars, which are the value encoded.
        const string value = "5901234123457";
        var symbol = Read(BarcodeSymbology.Ean13, value);

        var bars = symbol.Children.Single(c => c.Kind == BarcodeKind.Bars);
        Assert.AreEqual(0, bars.Start);
        Assert.AreEqual(value.Length, bars.Length);
        Assert.AreEqual(95, bars.Modules.Length, "an EAN-13 is 95 modules whatever the digits");

        var guards = bars.Children.Where(c => c.Kind == BarcodeKind.Guard).ToList();
        Assert.AreEqual(3, guards.Count, "start, centre and end");
        foreach (var guard in guards)
            Assert.AreEqual(0, guard.Length, "a guard is not any part of the value");
    }

    // ── Invariants ────────────────────────────────────────────────────────

    [TestMethod]
    public void NoPieceEverClaimsCharactersTheValueHasNotGot()
    {
        // The whole point is that a piece cannot lie about where it came from, so this is asserted across
        // the family rather than for one format: whatever a piece says it covers must really be there.
        (BarcodeSymbology Symbology, string Value)[] cases =
        [
            (BarcodeSymbology.Code128, "HELLO123"),
            (BarcodeSymbology.Code39, "ABC-123"),
            (BarcodeSymbology.Ean13, "590123412345"),
            (BarcodeSymbology.Ean13, "5901234123457"),
            (BarcodeSymbology.Ean8, "96385074"),
            (BarcodeSymbology.Upc, "036000291452"),
            (BarcodeSymbology.UpcE, "01234565"),
            (BarcodeSymbology.Isbn, "978-1-56581-231-4"),
            (BarcodeSymbology.Itf, "12345678"),
            (BarcodeSymbology.Codabar, "A12345B"),
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

            // And what it claims as source, in order, is a subsequence of the value rather than a reordering.
            var written = symbol.Written();
            Assert.IsTrue(written.Length == 0 || value.Contains(written),
                $"{symbology} '{value}': claimed source '{written}' is not in the value");
        }
    }
}

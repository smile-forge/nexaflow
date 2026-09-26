using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Barcode;
using Nexaflow.Markdown.Barcode.Stages;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Barcode;

/// <summary>
/// What is printed with a barcode's bars, as its stage says it (<see cref="EncodeBarcode"/>) — and, the point of it, which of it
/// the author actually typed.
///
/// <para>
/// These formats do not print what they are given. Codabar puts a start and a stop mark around it, an EAN-13 works out a
/// thirteenth digit, a UPC-E fills in both ends, an ISBN takes the hyphens out and puts a scheme's name on top. What is printed is
/// then one string to look at and several to reason about, and the stage's job is to say which of those pieces stand for a
/// character as written — the only ones an edit could be applied to — rather than to guess. Everything here is about that boundary.
/// </para>
/// </summary>
[TestClass]
[CoversNode("symbol-tree")]
public class BarcodePrintingTests
{
    private static BarcodeBlockNode Read(BarcodeSymbology symbology, string value)
    {
        var tree = new EncodeBarcode(writing: false).Run(BarcodeParser.Parse($"format: {symbology}\nvalue: {value}"));
        Assert.IsInstanceOfType(tree, typeof(BarcodeBlockNode),
            $"{symbology} '{value}': {string.Join("; ", tree.SelfAndDescendants().Select(node => node.Trouble).OfType<string>())}");
        return (BarcodeBlockNode)tree;
    }

    /// <summary>The value, as the characters it is written with.</summary>
    private static List<ContentNode> Characters(ContentNode block) =>
        [.. block.SelfAndDescendants().Where(node => node.Kind == BarcodeKinds.Character)];

    /// <summary>The runs of the number, in the order they are printed — not the caption.</summary>
    private static BarcodeRunNode[] Labels(ContentNode block) =>
        [.. block.Children.OfType<BarcodeRunNode>().Where(run => run.Kind != BarcodeKinds.Caption)];

    private static BarcodeRunNode Caption(ContentNode block) =>
        block.Children.OfType<BarcodeRunNode>().Single(run => run.Kind == BarcodeKinds.Caption);

    /// <summary>What a run prints, in order, as "kind:text".</summary>
    private static string[] Pieces(BarcodeRunNode run) =>
        run.Kind == BarcodeKinds.Worked ? [$"{BarcodeKinds.Worked}:{run.Run.Text}"] : [.. run.Children.Select(piece => $"{piece.Kind}:{piece.Text}")];

    /// <summary>Where each character of the value printed within <paramref name="within"/> stands in the value.</summary>
    private static int[] Claimed(ContentNode block, ContentNode within)
    {
        var characters = Characters(block);
        return [.. within.SelfAndDescendants().OfType<BarcodePrintedNode>()
                         .Select(printed => characters.FindIndex(character => ReferenceEquals(character, printed.Character)))];
    }

    /// <summary>The value as <paramref name="within"/> prints it — every character of it that stands for one written, in the order written.</summary>
    private static string Written(ContentNode block, ContentNode within) =>
        string.Concat(Claimed(block, within).Order().Select(at => Characters(block)[at].Text));

    // ── One string to look at, several to reason about ─────────────────────

    [TestMethod]
    public void CodabarBracketsTheValueInMarksNobodyTyped()
    {
        // The case that shows the shape of the whole thing. What is drawn reads as one run of characters and is three: a start
        // mark, the value, a stop mark. Only the middle one can be edited, and only because it really is what was typed.
        var block = Read(BarcodeSymbology.Codabar, "12345");

        CollectionAssert.AreEqual(
            new[] { "worked:A", "printed:1", "printed:2", "printed:3", "printed:4", "printed:5", "worked:B" },
            Pieces(Labels(block).Single()));

        Assert.AreEqual("12345", Written(block, block), "and the middle is the value, entire");
    }

    [TestMethod]
    public void CodabarGivenItsOwnMarksPrintsExactlyWhatWasTyped()
    {
        var block = Read(BarcodeSymbology.Codabar, "A12345B");

        Assert.AreEqual("A12345B", Written(block, block));
        Assert.IsFalse(block.SelfAndDescendants().Any(node => node.Kind == BarcodeKinds.Worked), "nothing was added, so nothing is worked out");
    }

    [TestMethod]
    public void AnEan13WorksOutItsLastDigitAndNotTheOthers()
    {
        // Twelve typed, thirteen printed. The twelve are the value and the thirteenth is a fact about all of them, so it stands
        // for nothing anybody typed.
        const string value = "590123412345";
        var block = Read(BarcodeSymbology.Ean13, value);

        Assert.AreEqual(value, Written(block, block), "every digit that was typed is still the value");
        Assert.AreEqual("7", block.SelfAndDescendants().Single(node => node.Kind == BarcodeKinds.Worked).Text);
    }

    [TestMethod]
    public void AUpcEFillsInBothEndsAndLeavesTheMiddleAlone()
    {
        // A UPC-E prints its number system outside the bars on the left and its check digit outside on the right, so the three
        // pieces are also three printed runs — the split falls where the format was already going to break the line.
        var block = Read(BarcodeSymbology.UpcE, "012345");

        var labels = Labels(block);
        Assert.AreEqual(3, labels.Length, "number system, body, check digit");

        Assert.AreEqual(BarcodeKinds.Worked, labels[0].Kind, "the number system was filled in");
        Assert.AreEqual(BarcodeKinds.Worked, labels[2].Kind, "and so was the check digit");

        CollectionAssert.AreEqual(
            new[] { "printed:0", "printed:1", "printed:2", "printed:3", "printed:4", "printed:5" },
            Pieces(labels[1]),
            "while the six digits between them are the ones that were typed");

        Assert.AreEqual("012345", Written(block, block));
    }

    // ── Where what is printed is what was typed ────────────────────────────

    [TestMethod]
    public void ACode128PrintsTheValueSoEveryCharacterOfItIsWritten()
    {
        const string value = "HELLO123";
        var block = Read(BarcodeSymbology.Code128, value);

        CollectionAssert.AreEqual(Enumerable.Range(0, value.Length).ToArray(), Claimed(block, block),
                                  "one printed character for each character of the value, in the order written");
    }

    [TestMethod]
    public void AnEan13TypedInFullIsPrintedAsTyped()
    {
        const string value = "5901234123457";
        var block = Read(BarcodeSymbology.Ean13, value);

        Assert.AreEqual(value, Written(block, block));
        Assert.IsFalse(block.SelfAndDescendants().Any(node => node.Kind == BarcodeKinds.Worked));
    }

    // ── Where none of it is ────────────────────────────────────────────────

    [TestMethod]
    public void AFormatThatRearrangesItsInputOffersNoneOfItAsWritten()
    {
        // Not everything that transforms merely adds to an end. An ISBN's printed digits are the value with its hyphens taken
        // out, so the value is nowhere in them in one piece — and a piece that cannot say which characters it is had better not
        // claim any.
        var block = Read(BarcodeSymbology.Isbn, "978-1-56581-231-4");

        foreach (var label in Labels(block))
            Assert.AreEqual(0, Claimed(block, label).Length, "the digits under the bars are a rendering of the number, not the number");
    }

    [TestMethod]
    public void APublicationsCaptionIsTheValueEvenThoughItsDigitsAreNot()
    {
        // The caption carries the number as it was written, hyphens and all, which makes it the one place a publication's value
        // appears as itself — and so the one place it could be edited.
        const string value = "978-1-56581-231-4";
        var block = Read(BarcodeSymbology.Isbn, value);

        var caption = Caption(block);

        Assert.AreEqual(BarcodeKinds.Worked, caption.Children[0].Kind, "nobody typed the scheme's name");
        Assert.AreEqual("ISBN ", caption.Children[0].Text);
        Assert.AreEqual(value, Written(block, caption), "and the rest of the line is the value, character for character");
    }

    [TestMethod]
    public void APublicationsNumberAndAddOnAreTheValueWhateverFollowsThem()
    {
        // Reported from the app: an ISMN could be selected and edited, an ISBN and an ISSN could not — and the samples differed in
        // one thing. The ISBN had an add-on and the ISSN an issue variant, so the value as a whole was nowhere in the caption, and
        // nothing on either symbol was found to be what had been typed.
        const string isbn = "978-1-56581-231-4 90000";
        var book = Read(BarcodeSymbology.Isbn, isbn);

        Assert.AreEqual("978-1-56581-231-4", Written(book, Caption(book)), "the caption is the number as it was written");

        var addOn = Labels(book).Single(run => run.Run.Placement == BarcodeTextPlacement.Above);
        Assert.AreEqual("90000", Written(book, addOn), "and the add-on over its own bars is the add-on");
        Assert.AreEqual(isbn.IndexOf("90000", StringComparison.Ordinal), Claimed(book, addOn).Min(), "where it was typed, after the space");

        var journal = Read(BarcodeSymbology.Issn, "0311-175X 00 17");
        Assert.AreEqual("0311-175X", Written(journal, Caption(journal)));
        Assert.AreEqual("17", Written(journal, Labels(journal).Single(run => run.Run.Placement == BarcodeTextPlacement.Above)));
    }

    // ── What is drawn ─────────────────────────────────────────────────────

    [TestMethod]
    public void TheBarsAreOneNodeAndWhatIsPrintedIsTheRest()
    {
        // What the stage hangs under the block is what a builder draws: the bars once, then each run printed with them. None of
        // it is anything written, so the block still prints as its fields.
        var block = Read(BarcodeSymbology.Ean13, "5901234123457");
        var drawn = block.Children.Where(child => child.IsDerived).ToList();

        Assert.IsInstanceOfType(drawn[0], typeof(BarcodeBarsNode));
        Assert.IsTrue(drawn.Skip(1).All(run => run is BarcodeRunNode), "and then the runs of the number");
        Assert.AreEqual(3, drawn.Count - 1, "the first digit outside the bars, and six under each half");
        Assert.AreEqual("format: Ean13\nvalue: 5901234123457", block.Print());
    }

    // ── Invariants ────────────────────────────────────────────────────────

    [TestMethod]
    public void NoCharacterOfTheValueIsPrintedAsTwoThingsOrAsAnotherCharacter()
    {
        (BarcodeSymbology Symbology, string Value)[] cases =
        [
            (BarcodeSymbology.Code128, "HELLO123"),
            (BarcodeSymbology.Code39, "ABC-123"),
            (BarcodeSymbology.Isbn, "978-1-56581-231-4 90000"),
            (BarcodeSymbology.Issn, "0311-175X 00 17"),
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
            var block = Read(symbology, value);

            foreach (var printed in block.SelfAndDescendants().OfType<BarcodePrintedNode>())
                Assert.AreEqual(printed.Character.Text, printed.Text, $"{symbology} '{value}': prints what it stands for");

            // And no character of it claimed twice, which is what an edit spliced through two pieces at once would corrupt. A
            // claim may be only part of the value — a publication claims its number where the caption prints it and its add-on
            // over its own bars, and neither the space between them nor the digits under the bars — but each is claimed once.
            var claimed = Claimed(block, block);
            Assert.IsTrue(claimed.All(at => at >= 0), $"{symbology} '{value}': stands for a character of the value");
            Assert.AreEqual(claimed.Length, claimed.Distinct().Count(), $"{symbology} '{value}': a character of the value is printed twice");
        }
    }
}

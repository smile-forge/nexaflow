using System;
using System.Linq;
using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Barcode;

namespace Nexaflow.Tests.Visuals.Markdown.Barcode;

/// <summary>
/// The barcode's layout, and what the shared queries make of it.
///
/// <para>
/// Nothing here is barcode-specific machinery. <c>LayoutQuery</c> answers where the caret can stand and
/// what a press landed on for a formula and for this alike, so what these assert is that the tree handed
/// to it says the right things — above all that a caret is offered only where the printing really is the
/// value, and never inside a digit the format worked out for itself.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("symbol-tree")]
[CoversNode("barcode-editing")]
public class BarcodeLayoutTests
{
    /// <summary>A block laid out as a document being written lays it: somewhere its value can be put right in place.</summary>
    private static Laid Written(string source) => BarcodeBuilder.Lay(source, StyleFormat.Dark, isReadOnly: false);

    private static Piece Root(string source) => Written(source).Root;

    /// <summary>Where the value begins in the block — what every place in it is counted from.</summary>
    private static int ValueAt(string source, string value) => source.IndexOf(value, StringComparison.Ordinal);

    private static Piece[] Of(Piece root, BarcodeKind kind) =>
        [.. root.SelfAndDescendants().Where(n => n.Kind == kind.ToString())];

    private static Point Middle(Piece node) =>
        new(node.Bounds.X + node.Bounds.Width / 2, node.Bounds.Y + node.Bounds.Height / 2);

    // ── Where a caret may stand ───────────────────────────────────────────

    [TestMethod]
    public void ACode128OffersAStopBetweenEveryCharacter() => UiThread.Run(() =>
    {
        const string value = "HELLO123";
        const string source = "format: CODE128\nvalue: " + value;

        CollectionAssert.AreEqual(
            Enumerable.Range(ValueAt(source, value), value.Length + 1).ToArray(),
            Root(source).CaretStops().ToArray(),
            "what is printed is the value, so every boundary in it is somewhere to stand");
    });

    [TestMethod]
    public void NothingInsideAWorkedOutDigitIsAStop() => UiThread.Run(() =>
    {
        // Twelve typed, thirteen printed. The thirteenth is a fact about all of them and belongs to
        // nobody's keystroke, so there is nowhere in it for a caret to be — the stops end where the value
        // ends, not where the printing does.
        const string value = "590123412345";
        const string source = "format: EAN13\nvalue: " + value;

        Assert.AreEqual(ValueAt(source, value) + value.Length, Root(source).CaretStops().Max(),
            "the last stop is the end of the value, not the end of the printed number");
    });

    [TestMethod]
    public void APublicationWithAnAddOnCanStillBeEdited() => UiThread.Run(() =>
    {
        // The number in its caption and the add-on over its bars are both what was typed, so both are somewhere to stand.
        const string value = "978-1-56581-231-4 90000";
        const string source = "format: ISBN\nvalue: " + value;
        var at = ValueAt(source, value);

        CollectionAssert.IsSubsetOf(new[] { at, at + 17, at + 18, at + value.Length }, Root(source).CaretStops().ToArray(),
            "either end of the number in the caption, and either end of the add-on");
    });

    [TestMethod]
    public void ALowerCaseLetterInACode39IsRefusedAndTheValueStaysEditable() => UiThread.Run(() =>
    {
        // Reported from the app: a lower-case letter typed into a Code 39 was drawn as a capital while the source
        // kept the small one, and the symbol could then be neither selected nor typed into.
        const string value = "MARKdOWN-39";
        const string source = "format: CODE39\nvalue: " + value;
        var laid = Written(source);

        Assert.AreEqual(1, laid.Trouble.Count, "a letter Code 39 cannot carry is an error like any other");
        Assert.AreEqual(ValueAt(source, value), laid.Trouble[0].Start, "said under the value");
        Assert.IsFalse(laid.ShowsSource, "and put right where it is shown");
        CollectionAssert.AreEqual(Enumerable.Range(ValueAt(source, value), value.Length + 1).ToArray(), laid.Root.CaretStops().ToArray(),
                                  "every character, as typed, is somewhere to stand");
    });

    [TestMethod]
    public void AValueThatWillNotEncodeWhereNothingIsWritten_IsShownAsWrittenWithTheValueMarked() => UiThread.Run(() =>
    {
        const string source = "format: CODE39\nvalue: MARKdOWN-39";

        var looked = BarcodeBuilder.Lay(source, StyleFormat.Dark, isReadOnly: true);
        Assert.IsTrue(looked.ShowsSource, "only being looked at, there is nowhere to put it right but its source");
        Assert.AreEqual(ValueAt(source, "MARKdOWN-39"), looked.Trouble.Single().Start, "with the value marked");

        var hidden = Written(source + "\ndisplayValue: false");
        Assert.IsTrue(hidden.ShowsSource, "and so it is where the value is not printed to be typed into");
    });

    [TestMethod]
    public void ABlockThatDoesNotRead_IsShownAsWrittenWithThePieceAtFaultMarked() => UiThread.Run(() =>
    {
        const string source = "format: CODE128\nvalue: X\nheight: tall";
        var laid = Written(source);

        Assert.IsTrue(laid.ShowsSource);
        Assert.AreEqual(ValueAt(source, "tall"), laid.Trouble.Single().Start, "the height that is not a number");
    });

    [TestMethod]
    public void APieceTheFormatWorkedOutHoldsNoPlaceInTheSource() => UiThread.Run(() =>
    {
        var root = Root("format: EAN13\nvalue: 590123412345");

        foreach (var node in Of(root, BarcodeKind.EncodedText))
            Assert.AreEqual(0, node.Sits().Length,
                "it stands for the whole value in the parse tree, and for no offsets here");
    });

    // ── Where a press lands ───────────────────────────────────────────────

    [TestMethod]
    public void PressingADigitOfTheValueFindsThatCharacter() => UiThread.Run(() =>
    {
        const string source = "format: CODE128\nvalue: HELLO123";
        var root = Root(source);

        var third = Of(root, BarcodeKind.Character)[2];
        var found = root.PieceAt(Middle(third));

        Assert.AreEqual(ValueAt(source, "HELLO123") + 2, found!.Sits().Start);
        Assert.AreEqual(1, found.Sits().Length);
    });

    // ── What is drawn but says nothing ────────────────────────────────────

    [TestMethod]
    public void TheBarsAreDrawnAndStandForNothing() => UiThread.Run(() =>
    {
        var root = Root("format: EAN13\nvalue: 590123412345");

        var bars = root.SelfAndDescendants().Single(n => n.Kind == "Bars");

        Assert.IsTrue(bars.Bounds.Width > 0 && bars.Bounds.Height > 0, "they are on the page");
        Assert.AreEqual(0, bars.Sits().Length,
            "and hold no place in the source, so the caret is never stood against one");
    });

    // ── A publication ─────────────────────────────────────────────────────

    [TestMethod]
    public void APublicationTakesItsCaretInTheCaptionAndNotUnderTheBars() => UiThread.Run(() =>
    {
        // The caption carries the number as it was written; the digits under the bars are that number
        // with the hyphens taken out and a check digit added. So the caption is the editable half.
        const string value = "978-1-56581-231-4";
        var root = Root("format: ISBN\nvalue: " + value);

        var caption = root.SelfAndDescendants().Single(n => n.Kind == nameof(BarcodeKind.Caption));

        Assert.AreEqual(value.Length, Of(root, BarcodeKind.Character).Length,
            "one stop per character of the value, all of them in the caption");

        foreach (var character in Of(root, BarcodeKind.Character))
            Assert.IsTrue(caption.Bounds.Contains(Middle(character)),
                "and none of them under the bars");
    });

    [TestMethod]
    public void ABrokenPublicationKeepsTheCaptionSoItCanStillBeRepaired() => UiThread.Run(() =>
    {
        // A value one character short does not encode, so there is no symbol to read a caption off. It is
        // built from the value instead, because losing the caret the moment the number goes wrong would
        // take away the only place it could be put right.
        var root = Root("format: ISBN\nvalue: 978-1-56581-231-");

        Assert.AreNotEqual(0, Of(root, BarcodeKind.Character).Length,
            "the caption is still the value, so the caret still has somewhere to be");
    });

    [TestMethod]
    public void AValueNotYetWritten_IsAHoleUnderAFaintSymbol_WhereSomebodyIsWriting() => UiThread.Run(() =>
    {
        const string source = "format: CODE128\nvalue:";

        var written = Written(source);
        var hole = written.Root.SelfAndDescendants().Single(piece => piece.Kind == LayoutText.HoleKind);

        Assert.IsFalse(written.ShowsSource, "the barcode stays on the page");
        Assert.AreEqual(0, written.Trouble.Count, "nothing is wrong yet, there is only something still to write");
        Assert.AreEqual(source.Length, hole.Sits().Start, "and the hole stands where the value goes");

        var looked = BarcodeBuilder.Lay(source, StyleFormat.Dark, isReadOnly: true);
        Assert.IsTrue(looked.ShowsSource, "only being read, a missing value is shown as written");
        Assert.AreEqual(source.IndexOf("value:", StringComparison.Ordinal), looked.Trouble.Single().Start, "with its line marked");
    });
}

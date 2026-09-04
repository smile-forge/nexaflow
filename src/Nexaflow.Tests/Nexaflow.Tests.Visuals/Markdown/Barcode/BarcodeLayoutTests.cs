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
    private static ILayoutNode Root(string source) => UiRoot(source)!;

    private static ILayoutNode? UiRoot(string source)
    {
        Assert.IsTrue(BarcodeBlockParser.TryParse(source, out var block, out string? error), error);
        return ((IEditableBlock)new BarcodeElement(block!, MarkdownPalette.Dark)).Root;
    }

    private static ILayoutNode[] Of(ILayoutNode root, BarcodeKind kind) =>
        [.. root.SelfAndDescendants().Where(n => n.Kind == kind.ToString())];

    private static Point Middle(ILayoutNode node) =>
        new(node.Bounds.X + node.Bounds.Width / 2, node.Bounds.Y + node.Bounds.Height / 2);

    // ── Where a caret may stand ───────────────────────────────────────────

    [TestMethod]
    public void ACode128OffersAStopBetweenEveryCharacter() => UiThread.Run(() =>
    {
        const string value = "HELLO123";
        var root = Root("format: CODE128\nvalue: " + value);

        CollectionAssert.AreEqual(
            Enumerable.Range(0, value.Length + 1).ToArray(),
            root.CaretStops().ToArray(),
            "what is printed is the value, so every boundary in it is somewhere to stand");
    });

    [TestMethod]
    public void NothingInsideAWorkedOutDigitIsAStop() => UiThread.Run(() =>
    {
        // Twelve typed, thirteen printed. The thirteenth is a fact about all of them and belongs to
        // nobody's keystroke, so there is nowhere in it for a caret to be — the stops end where the value
        // ends, not where the printing does.
        const string value = "590123412345";
        var root = Root("format: EAN13\nvalue: " + value);

        Assert.AreEqual(value.Length, root.CaretStops().Max(),
            "the last stop is the end of the value, not the end of the printed number");
    });

    [TestMethod]
    public void APieceTheFormatWorkedOutHoldsNoPlaceInTheSource() => UiThread.Run(() =>
    {
        var root = Root("format: EAN13\nvalue: 590123412345");

        foreach (var node in Of(root, BarcodeKind.EncodedText))
            Assert.AreEqual(0, node.SourceLength,
                "it stands for the whole value in the parse tree, and for no offsets here");
    });

    // ── Where a press lands ───────────────────────────────────────────────

    [TestMethod]
    public void PressingAWorkedOutDigitFindsThatDigitAndNotANeighbour() => UiThread.Run(() =>
    {
        var root = Root("format: EAN13\nvalue: 590123412345");

        var check = Of(root, BarcodeKind.EncodedText).Single();
        Assert.AreSame(check, root.NodeAt(Middle(check)),
            "so the element can answer that pressing it means the whole number");
    });

    [TestMethod]
    public void PressingADigitOfTheValueFindsThatCharacter() => UiThread.Run(() =>
    {
        var root = Root("format: CODE128\nvalue: HELLO123");

        var third = Of(root, BarcodeKind.Character)[2];
        var found = root.NodeAt(Middle(third));

        Assert.AreEqual(2, found!.SourceStart);
        Assert.AreEqual(1, found.SourceLength);
    });

    // ── What is drawn but says nothing ────────────────────────────────────

    [TestMethod]
    public void TheBarsAreDrawnAndStandForNothing() => UiThread.Run(() =>
    {
        var root = Root("format: EAN13\nvalue: 590123412345");

        var bars = root.SelfAndDescendants().Single(n => n.Kind == "Bars");

        Assert.IsTrue(bars.Bounds.Width > 0 && bars.Bounds.Height > 0, "they are on the page");
        Assert.AreEqual(0, bars.SourceLength,
            "and hold no place in the source, so the caret is never stood against one");
        Assert.IsFalse(bars.IsInk, "nor are they something a drag picks out");
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
        var root = UiRoot("format: ISBN\nvalue: 978-1-56581-231-");

        Assert.IsNotNull(root);
        Assert.AreNotEqual(0, Of(root!, BarcodeKind.Character).Length,
            "the caption is still the value, so the caret still has somewhere to be");
    });
}

using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// A run of text as one piece (<see cref="LayoutWords"/>): a press means the letter it landed on, the caret stands
/// between two letters, and a stretch taking part of a run is washed as those letters — none of which costs a piece
/// per character.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("layout-words")]
public class LayoutWordsTests
{
    //  The source these trees stand for:  pie "Dogs" : 386
    //  offsets                            0   4      11
    private const string Label = "Dogs";
    private const string Value = "386";

    private static readonly TestPart LabelPart = new(4, 4);
    private static readonly TestPart ValuePart = new(11, 3);

    private static FormattedText Set(string text) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 12, Brushes.Black, 1.0);

    /// <summary>A label and a value, each one run of text, inside a piece standing for the whole line.</summary>
    private static LayoutTree Line(bool maps = true)
    {
        var build = new LayoutBuilder();
        build.Open("line", new TestPart(0, 14));

        LayoutText.Words(build, Set(Label), new Point(10, 0), 200, TextAlignment.Left, LabelPart, "label");
        LayoutText.Words(build, Set(Value), new Point(80, 0), 200, TextAlignment.Left, ValuePart, "value", maps);

        build.Close();
        return build.Seal();
    }

    private static Piece Run(Piece root) => root.Children.Single(piece => piece.Kind == "value");

    [TestMethod]
    public void APressMeansTheLetterItLandedOn() => UiThread.Run(() =>
    {
        var root = Line().Root;
        var words = Run(root).Words!;
        var anchor = Run(root).Anchor;

        foreach (var index in new[] { 0, 1, 2 })
        {
            var letter = words.Covers(index, index + 1);
            var middle = letter.Y + (letter.Height / 2);

            Assert.AreEqual(ValuePart.Start + index,
                root.OffsetAt(new Point(anchor.X + letter.X + (letter.Width * 0.25), anchor.Y + middle)),
                $"the left half of letter {index} means before it");

            Assert.AreEqual(ValuePart.Start + index + 1,
                root.OffsetAt(new Point(anchor.X + letter.X + (letter.Width * 0.75), anchor.Y + middle)),
                $"and the right half means after it");
        }
    });

    [TestMethod]
    public void TheCaretStandsBetweenTwoLetters() => UiThread.Run(() =>
    {
        var root = Line().Root;
        var value = Run(root);
        var bars = Enumerable.Range(0, Value.Length + 1).Select(index => root.CaretRect(ValuePart.Start + index).X).ToList();

        CollectionAssert.AreEqual(bars.OrderBy(x => x).ToList(), bars, "one bar per position, left to right");
        Assert.AreEqual(value.Anchor.X + value.Words!.Caret(0).X, bars[0], 0.001, "the first stands where the run starts");
        Assert.IsTrue(bars[^1] > bars[0], "and the last past its letters");
    });

    [TestMethod]
    public void PartOfARunIsWashedAsThoseLetters() => UiThread.Run(() =>
    {
        var root = Line().Root;
        var value = Run(root);

        var middle = root.RangeRects(ValuePart.Start + 1, 1).Single();
        Assert.AreEqual(value.Anchor.X + value.Words!.Covers(1, 2).X, middle.X, 0.001);
        Assert.IsTrue(middle.Width < value.Bounds.Width, "one letter, not the whole run");
    });

    [TestMethod]
    public void AWholeRunIsWashedAsItsBox() => UiThread.Run(() =>
    {
        var root = Line().Root;

        var washed = root.RangeRects(ValuePart.Start, ValuePart.Length).Single();
        var box = Run(root).Bounds;

        Assert.AreEqual(box.X, washed.X, 0.01);
        Assert.AreEqual(box.Width, washed.Width, 0.01);
        Assert.AreEqual(box.Height, washed.Height, 0.01);
    });

    [TestMethod]
    public void ARunThatShowsSomethingWorkedOutHasNoPositionsInside() => UiThread.Run(() =>
    {
        var root = Line(maps: false).Root;
        var value = Run(root);
        var letter = value.Words!.Covers(1, 2);

        Assert.IsFalse(root.WordsAt(ValuePart.Start + 1).Exists, "a caret cannot stand inside what nobody typed");

        var press = root.OffsetAt(new Point(value.Anchor.X + letter.X, value.Anchor.Y + letter.Y + 1));
        Assert.IsTrue(press == ValuePart.Start || press == ValuePart.End(),
            "a press means one end of it, the way a press on any drawing does");
    });

    [TestMethod]
    public void ARunGoesWhereverItsTreeIsPutDown() => UiThread.Run(() =>
    {
        var build = new LayoutBuilder();
        build.Open("page");
        build.Graft(Line(), new Point(300, 100));
        build.Close();

        var root = build.Seal().Root;
        var value = root.SelfAndDescendants().Single(piece => piece.Kind == "value");
        var letter = value.Words!.Covers(2, 3);

        Assert.AreEqual(ValuePart.Start + 2,
            root.OffsetAt(new Point(value.Anchor.X + letter.X + (letter.Width * 0.25), value.Anchor.Y + letter.Y + 1)));

        Assert.AreEqual(value.Anchor.X + value.Words!.Caret(2).X, root.CaretRect(ValuePart.Start + 2).X, 0.001);
    });

    [TestMethod]
    public void AWordIsLettersAndDigitsTogether() => UiThread.Run(() =>
    {
        var words = new LayoutWords(Set("Dogs 38.6"), default, true);

        Assert.AreEqual((0, 4), words.WordAt(2), "a word of letters");
        Assert.AreEqual((5, 7), words.WordAt(6), "the digits before the point");
        Assert.AreEqual((5, 7), words.WordAt(7), "a caret between a digit and the point means the digits");
        Assert.AreEqual((8, 9), words.WordAt(8), "and the digit after it is its own word");

        // The point is only a word of its own where nothing wordy is written before it.
        Assert.AreEqual((3, 4), new LayoutWords(Set("38 .6"), default, true).WordAt(3));
    });

    [TestMethod]
    public void APressOnARunBrokenIntoLinesMeansTheLetterOnTheLineItLandedOn() => UiThread.Run(() =>
    {
        const string said = "one two three four five six";
        var set = Set(said);
        set.MaxTextWidth = set.Width / 2.5;

        var words = new LayoutWords(set, default, Maps: true);
        var first = words.Covers(0, 1);
        var last = words.Covers(said.Length - 1, said.Length);

        Assert.IsTrue(last.Y > first.Y, "set as more than one line");
        Assert.AreEqual(said.Length, words.IndexAt(new Point(last.Right + 50, last.Y + (last.Height / 2))),
                        "past the end of the last line is the end of the run");
        Assert.AreEqual(said.Length - 1, words.IndexAt(new Point(last.X + (last.Width * 0.25), last.Y + (last.Height / 2))),
                        "the left half of the last letter is before it, on its own line");
        Assert.AreEqual(0, words.IndexAt(new Point(first.X, first.Y - 50)), "above every line is the first");
        Assert.IsTrue(words.IndexAt(new Point(first.Right + 400, first.Y + (first.Height / 2))) < said.Length / 2,
                      "past the end of the first line is the end of that line");
    });
}

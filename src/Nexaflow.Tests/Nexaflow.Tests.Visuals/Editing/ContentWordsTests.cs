using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// Writing in a run of text (<see cref="LayoutWords"/>) through the element that hosts it: a press puts the caret
/// between two letters, the arrows step through them, and a run showing a value worked out is shown as written for
/// as long as the caret is in it.
///
/// <para>
/// The content is a number and a letter beside it — the smallest thing that has somewhere to go when the caret
/// leaves the number.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[CoversNode("layout-words")]
public class ContentWordsTests
{
    private const string Source = "386 x";

    private static readonly SourceSpan Number = new(0, 3);
    private static readonly SourceSpan Letter = new(4, 1);

    /// <summary>
    /// The number set to two decimal places, unless it is being shown as written — which is the whole of what a
    /// formatter is — and a letter beside it that always reads as itself.
    /// </summary>
    private static Laid Lay(EditState state, double pixelsPerDip)
    {
        var written = state.Raw is { } zone && zone.Start == Number.Start && zone.End == Number.End();
        var number = written
            ? state.Source[Number.Start..Number.End()]
            : double.Parse(state.Source[Number.Start..Number.End()], CultureInfo.InvariantCulture).ToString("0.00", CultureInfo.InvariantCulture);

        var build = new LayoutBuilder();
        build.Open("row", new SourceSpan(0, state.Source.Length));

        // A formatted view of its own source: pressing it shows what was written, which is what `writes` says.
        LayoutText.Words(build, Set(number, pixelsPerDip), default, 400, TextAlignment.Left, Number, "value", written, writes: true);
        LayoutText.Words(build, Set("x", pixelsPerDip), new Point(200, 0), 400, TextAlignment.Left, Letter, "letter");

        build.Close();

        var tree = build.Seal();
        return new Laid(tree, tree.Root.Bounds.Size, []);
    }

    private static FormattedText Set(string text, double pixelsPerDip) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 12, Brushes.Black, pixelsPerDip);

    private static ContentElement Element(bool readOnly = false)
    {
        var element = new ContentElement(Source, MarkdownPalette.Dark, (state, _, pixelsPerDip) => Lay(state, pixelsPerDip))
        {
            IsReadOnly = readOnly,
        };

        element.Measure(new Size(400, double.PositiveInfinity));
        element.Arrange(new Rect(0, 0, 400, element.DesiredSize.Height));
        return element;
    }

    private static Piece Run(ContentElement element, string kind) =>
        element.Laid.Root.SelfAndDescendants().Single(piece => piece.Kind == kind);

    /// <summary>Where the middle of a letter of a run is, in the element's own coordinates.</summary>
    private static Point Middle(Piece run, int index)
    {
        var letter = run.Words!.Covers(index, index + 1);
        return new Point(run.Anchor.X + letter.X + (letter.Width / 2), run.Anchor.Y + letter.Y + (letter.Height / 2));
    }

    [TestMethod]
    public void PressingAValueShowsWhatWasWritten() => UiThread.Run(() =>
    {
        var element = Element();
        Assert.AreEqual("386.00", Run(element, "value").Words!.Glyphs.Text, "it reads as what it says");

        element.BeginPointerSelect(Middle(Run(element, "value"), 1));

        Assert.AreEqual((Number.Start, Number.Length), element.ShownAsWritten, "and is written in as what was written");
        Assert.AreEqual("386", Run(element, "value").Words!.Glyphs.Text);
        Assert.AreEqual(0, element.SelectionLength, "a press in text is a caret, not a thing picked up");
    });

    [TestMethod]
    public void AndTheCaretStepsThroughItALetterAtATime() => UiThread.Run(() =>
    {
        var element = Element();
        element.BeginPointerSelect(Middle(Run(element, "value"), 1));

        var caret = element.Caret;
        Assert.IsTrue(caret > Number.Start && caret <= Number.End(), $"the caret landed inside the value, at {caret}");

        Assert.IsTrue(element.MoveCaret(forward: false));
        Assert.AreEqual(caret - 1, element.Caret, "one letter back");

        Assert.IsTrue(element.MoveCaret(forward: true));
        Assert.IsTrue(element.MoveCaret(forward: true));
        Assert.AreEqual(caret + 1, element.Caret, "and two forward");
    });

    [TestMethod]
    public void AndLeavingItPutsTheValueBack() => UiThread.Run(() =>
    {
        var element = Element();
        element.BeginPointerSelect(Middle(Run(element, "value"), 1));
        Assert.IsNotNull(element.ShownAsWritten);

        element.BeginPointerSelect(Middle(Run(element, "letter"), 0));

        Assert.IsNull(element.ShownAsWritten, "the value is no longer being written in");
        Assert.AreEqual("386.00", Run(element, "value").Words!.Glyphs.Text, "so it reads as what it says again");
        Assert.AreEqual(Letter.Start, element.Caret, "and the caret is where it was pressed");
    });

    [TestMethod]
    public void PressingAValueThatCannotBeWrittenInLeavesItAlone() => UiThread.Run(() =>
    {
        var element = Element(readOnly: true);
        element.BeginPointerSelect(Middle(Run(element, "value"), 1));

        Assert.IsNull(element.ShownAsWritten, "there is nobody to write in it");
        Assert.AreEqual("386.00", Run(element, "value").Words!.Glyphs.Text);
    });

    [TestMethod]
    public void PressingTextThatIsWhatWasWrittenPutsTheCaretInIt() => UiThread.Run(() =>
    {
        var element = Element();
        element.BeginPointerSelect(Middle(Run(element, "letter"), 0));

        Assert.AreEqual(0, element.SelectionLength);
        Assert.IsTrue(element.Caret == Letter.Start || element.Caret == Letter.End(),
            $"the caret is beside the letter pressed, at {element.Caret}");
    });
}

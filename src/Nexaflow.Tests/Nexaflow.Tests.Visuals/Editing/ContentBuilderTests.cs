using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// Coverage for <see cref="ContentBuilder"/> — the one promise every builder makes: laying something out
/// always produces a layout.
///
/// <para>
/// Asserted here rather than through a formula or a tune, because it is not either of their rules. A
/// builder that hands back nothing makes everything downstream carry a second state, and the point of the
/// base class is that no builder is in a position to do so however badly its own reading goes.
/// </para>
/// <para>
/// Needs an STA thread for WPF's font machinery — the fallback sets characters. It opens no window.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-source-map")]
public class ContentBuilderTests
{
    /// <summary>A builder that draws nothing, however it is asked.</summary>
    private sealed class Unwilling(string source, Func<Laid?> read)
        : ContentBuilder(ContentReading.Of(ContentNode.Leaf(Kinds.Verbatim, source)), EditState.For(source),
                         StyleFormat.Dark, isReadOnly: true, Nexaflow.Tests.Visuals.Markdown.Laying.NestingNothing)
    {
        protected override Laid? Build() => read();

        protected override FormattedText Characters(string text) =>
            new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 13, Brushes.Black, 1.0);
    }

    [TestMethod]
    public void ABuilderThatCanReadNothingStillLaysTheSourceOut() => UiThread.Run(() =>
    {
        var laid = new Unwilling("a+b", () => null).Lay();

        Assert.IsTrue(laid.ShowsSource, "the source is shown as its own characters");
        Assert.AreEqual(3, laid.Root.SelfAndDescendants().Single(piece => piece.Kind == LayoutText.SourceKind).Sits().Length, "standing for every character of it");
        Assert.IsTrue(laid.Size.Width > 0 && laid.Size.Height > 0, "and it takes up room");
        Assert.AreEqual(0, laid.Trouble.Count, "nothing went wrong — there was simply nothing to read");
    });

    [TestMethod]
    public void EveryCharacterIsAPlaceEvenWhenNoneOfItCouldBeRead() => UiThread.Run(() =>
    {
        // The reason the fallback is a layout rather than a drawing: unreadable source is exactly what
        // somebody is in the middle of fixing, so the caret has to be able to walk it and a selection has
        // to be able to take part of it.
        var laid = new Unwilling("a+b", () => null).Lay();

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, laid.Stops.ToArray());
    });

    [TestMethod]
    public void ABuilderThatThrowsBecomesALayoutSayingSo() => UiThread.Run(() =>
    {
        var laid = new Unwilling("x^", () => throw new InvalidOperationException("ran off the end")).Lay();

        Assert.IsTrue(laid.ShowsSource, "the source is still on the page");

        var trouble = laid.Trouble.Single();
        Assert.AreEqual(DiagnosticSeverity.Error, trouble.Severity);
        Assert.AreEqual((0, 2), (trouble.Start, trouble.Length), "covering all of it — it has no better idea");
        StringAssert.Contains(trouble.Message, "ran off the end");
    });

    [TestMethod]
    public void ABuilderThatThrowsSaysWhyUnderTheSource() => UiThread.Run(() =>
    {
        var laid = new Unwilling("x^", () => throw new InvalidOperationException("ran off the end")).Lay();
        var reason = laid.Root.SelfAndDescendants().Single(piece => piece.Kind == SourceShown.Reason);

        StringAssert.Contains(reason.Marks.ToArray().OfType<TextMark>().Single().Glyphs.Text, "ran off the end");
    var source = laid.Root.SelfAndDescendants().Single(piece => piece.Kind == LayoutText.SourceKind).Bounds;
        Assert.IsTrue(reason.Bounds.Top > source.Bottom - 5 && reason.Bounds.Top < source.Bottom, "tucked right under the source it is about");
    });

    [TestMethod]
    public void APartBlamedStandsOverItsOwnCharacters_AndIsWhatTheTroubleNames() => UiThread.Run(() =>
    {
        // A tree the builder was given: it names the part it could not draw, and the helper finds where that part's characters are.
        var tree = ContentPart.Of(ContentNode.Branch("line", [ContentNode.Leaf("word", "one "), ContentNode.Leaf("word", "two"), ContentNode.Leaf("word", " three")]));
        var two = tree.Children[1];
        var laid = SourceShown.Lay(tree, [(two, "Two is not a word here.")],
                                   text => new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 13, Brushes.Black, 1.0),
                                   StyleFormat.Dark);

        var unread = laid.Root.SelfAndDescendants().Single(piece => piece.Kind == SourceShown.Unread);
        var letters = laid.Root.SelfAndDescendants().Where(piece => piece.Kind == LayoutText.SourceKind + "-letter").ToList();

        Assert.AreSame(two, unread.Part, "standing for the part itself");
        Assert.AreEqual(letters[4].Bounds.Left, unread.Bounds.Left, 0.5, "over the characters it printed as");
        Assert.AreEqual(letters[6].Bounds.Right, unread.Bounds.Right, 0.5);
        Assert.AreEqual((4, 3), (laid.Trouble.Single().Start, laid.Trouble.Single().Length), "and the trouble is that part's");
        Assert.AreSame(two, laid.Trouble.Single().Part);
    });

    [TestMethod]
    public void ReadingThatFallsOverBeforeAnyBuilderIsShownAsWrittenWithWhy() => UiThread.Run(() =>
    {
        var element = HandLaid.Unreadable("a+b");
        element.Measure(new Size(400, double.PositiveInfinity));

        Assert.IsTrue(element.HasError, "the element still stands, saying something is wrong");
    });

    [TestMethod]
    public void EveryPlaceTheContentCanFallOverBeforeABuilderIsShownAsWrittenWithWhy() => UiThread.Run(() =>
    {
        // The builder's own promise covers a builder. These are the steps before it: reading the source, working the
        // reading over, binding what it refers to, and being handed something that is not a builder to lay it with. Each
        // of them lands in the engine's catch, and each of them has to come out as the source with a reason on it.
        (string Where, ContentElement Element, string Says)[] falling =
        [
            ("reading it", HandLaid.Unreadable("a+b"), "no reader"),
            ("working it over", HandLaid.Unstageable("a+b"), "no stage"),
            ("binding it", HandLaid.Unbindable("a+b"), "no binding"),
            ("laying it out at all", HandLaid.Unbuildable("a+b"), "not a builder"),
        ];

        foreach (var (where, element, says) in falling)
        {
            element.Measure(new Size(400, double.PositiveInfinity));
            var laid = element.Laid;

            Assert.IsTrue(laid.ShowsSource, $"{where}: the source is still on the page");
            Assert.AreEqual(3, laid.Root.SelfAndDescendants().Single(piece => piece.Kind == LayoutText.SourceKind).Sits().Length,
                            $"{where}: standing for every character of it");

            var trouble = laid.Trouble.Single();
            Assert.AreEqual((0, 3), (trouble.Start, trouble.Length), $"{where}: blaming all of it, which is all it knows");
            StringAssert.Contains(trouble.Message, says, $"{where}: and saying what happened");
        }
    });

    [TestMethod]
    public void SourceNothingCouldReadIsStillSomethingToTypeInto() => UiThread.Run(() =>
    {
        // Why the fallback is a layout rather than a drawing, one step up from ABuilderThatCanReadNothing: this is exactly
        // the document somebody is about to fix, and a fallback with nowhere to put the caret is one they cannot fix here.
        var element = HandLaid.Unreadable("a+b");
        element.Measure(new Size(400, double.PositiveInfinity));

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, element.Laid.Stops.ToArray(), "a place to stand between every character");
    });

    [TestMethod]
    public void ABlockWhoseReadingFallsOverCostsTheBlockRatherThanTheDocument() => UiThread.Run(() =>
    {
        // Content in another language is read while the document's own builder is part-way through laying it, so a throw
        // let out would be blamed on the only tree that builder has — all of it.
        var document = $"Pets:\n\n```{HandLaid.Unreading}\nx\n```\n\nThe end.\n";
        var element = new MarkdownElement(document, StyleFormat.Dark);
        element.Measure(new Size(400, double.PositiveInfinity));

        var laid = element.Laid;
        Assert.IsFalse(laid.ShowsSource, "the document is still a document, not one long quotation of itself");

        var trouble = laid.Trouble.Single();
        StringAssert.Contains(trouble.Message, "no reader", "with the block's own reason on it");
        Assert.IsTrue(laid.Root.SelfAndDescendants().Any(piece => piece.Kind == SourceShown.Reason), "and that reason written under it");
        Assert.IsTrue(trouble.Start > document.IndexOf("```", StringComparison.Ordinal)
                      && trouble.Start + trouble.Length <= document.IndexOf("The end.", StringComparison.Ordinal),
                      "marked inside the block, not over the whole document");
    });

    [TestMethod]
    public void EmptySourceIsAPlaceToPutTheCaretRatherThanNothingAtAll() => UiThread.Run(() =>
    {
        var laid = new Unwilling("", () => null).Lay();

        Assert.IsTrue(laid.Size.Height > 0, "a line's height, so a caret can be drawn against it");
        CollectionAssert.AreEqual(new[] { 0 }, laid.Stops.ToArray(), "with exactly one place to stand");
        Assert.AreEqual(0, laid.Root.Sits().Length, "and the blank standing in for it names no source");
    });
}

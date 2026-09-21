using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;
using System;
using System.Windows.Input;
using System.Windows.Controls;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Pie;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a pie through the editor that hosts it: the value in the legend is the number in the source, so a press
/// puts the caret between its digits and a keystroke changes the chart.
///
/// <para>
/// Driven through a real <see cref="Nexaflow.Visuals.Text.Markdown.InlineMarkdownEditor"/>, because that is the whole
/// claim: the caret has to be adopted by the editor and the keystroke routed back into the block, neither of which the
/// block can do for itself.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("pie")]
public class PieEditingTests
{
    /// <summary>The block on its own, which is how a document made of one diagram is edited.</summary>
    private const string Markdown = "pie showData\n  \"Dogs\" : 30\n  \"Cats\" : 10";

    /// <summary>The same pie with a title over it.</summary>
    private const string Titled = "pie showData\n  title Pets\n  \"Dogs\" : 30\n  \"Cats\" : 10";

    /// <summary>Shows the pie as the whole document, and hands back the element it drew.</summary>
    private static void Edited(Action<Nexaflow.Visuals.Text.Markdown.InlineMarkdownEditor, System.Windows.Controls.RichTextBox, ContentElement> test) =>
        MarkdownEditorHarness.Run(Markdown, (editor, rtb) =>
        {
            var pie = Find<ContentElement>(editor);
            Assert.IsNotNull(pie, "the pie did not render as content");

            test(editor, rtb, pie!);
        },
        editor => editor.SingleBlock = "mermaid");

    /// <summary>Presses just past the last digit of a slice's value, where it is drawn in the legend.</summary>
    private static void PressPastTheValue(ContentElement pie, string number)
    {
        var value = pie.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Kind == PiePiece.Value
                            && piece.Sits().Start == pie.Source.IndexOf(number, System.StringComparison.Ordinal));

        pie.BeginPointerSelect(new Point(value.Bounds.Right - 1, value.Bounds.Y + (value.Bounds.Height / 2)));
        pie.EndPointerSelect();
    }

    /// <summary>Presses just inside the end of a slice's label, where it is drawn in the legend.</summary>
    private static void PressPastTheLabel(ContentElement pie, string name)
    {
        var label = pie.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Kind == PiePiece.Label
                            && piece.Sits().Start == pie.Source.IndexOf(name, StringComparison.Ordinal));

        pie.BeginPointerSelect(new Point(label.Bounds.Right - 1, label.Bounds.Y + (label.Bounds.Height / 2)));
        pie.EndPointerSelect();
    }

    /// <summary>Presses in the title: at its middle, or just inside its end.</summary>
    private static void PressInTheTitle(ContentElement pie, bool atEnd = false)
    {
        var title = pie.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == MermaidPiece.Title).Bounds;
        var x = atEnd ? title.Right - 1 : title.X + (title.Width / 2);

        pie.BeginPointerSelect(new Point(x, title.Y + (title.Height / 2)));
        pie.EndPointerSelect();
    }

    /// <summary>The pie inside a document, fenced, which is how most are written.</summary>
    private static void InADocument(Action<InlineMarkdownEditor, ContentElement> test, string diagram = Markdown) =>
        MarkdownEditorHarness.Run("Pets:\n\n```mermaid\n" + diagram + "\n```\n", editor =>
        {
            var pie = Find<ContentElement>(editor);
            Assert.IsNotNull(pie, "the pie did not render as content");

            // What a press on it does in the app, which a press made straight on the element cannot: the editor takes the
            // keyboard and hands its keys to the block.
            Assert.IsTrue(editor.FocusBlockAtCaret(), "the editor has the pie to give the keys to");

            test(editor, pie!);
        });

    private static void Press(InlineMarkdownEditor editor, Key key)
    {
        MarkdownEditorHarness.RaiseKey(editor, key);
        MarkdownEditorHarness.Pump();
    }

    private static void Write(InlineMarkdownEditor editor, string text)
    {
        MarkdownEditorHarness.RaiseTextInput(editor, text);
        MarkdownEditorHarness.Pump();
    }

    /// <summary>Somewhere inside the first wedge's own shape, and clear of the share written on it.</summary>
    private static Point InsideAWedge(ContentElement pie)
    {
        var wedge = pie.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == PiePiece.Wedge);
        var shares = pie.Laid.Root.SelfAndDescendants().Where(piece => piece.Kind == PiePiece.Share).Select(piece => piece.Bounds).ToList();
        var box = wedge.Bounds;

        for (var across = 1; across < 20; across++)
            for (var down = 1; down < 20; down++)
            {
                var at = new Point(box.X + (box.Width * across / 20), box.Y + (box.Height * down / 20));
                if (wedge.Region!.FillContains(at - wedge.Anchor) && !shares.Any(share => share.Contains(at))) return at;
            }

        Assert.Fail("no point inside the wedge");
        return default;
    }

    [TestMethod]
    public void TypingInTheLegendChangesTheNumberItWasWrittenAs() => UiThread.Run(() =>
        Edited((editor, rtb, pie) =>
        {
            Assert.IsFalse(pie.IsReadOnly, "a pie's values are written in");

            PressPastTheValue(pie, "30");
            Assert.IsTrue(pie.HasCaret, "a press in the legend takes the caret");

            MarkdownEditorHarness.RaiseTextInput(rtb, "9");
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(pie.Source, "\"Dogs\" : 309", $"the block now reads {pie.Source}");
            StringAssert.Contains(editor.Markdown, "\"Dogs\" : 309", "and so does the document");
        }));

    [TestMethod]
    public void AndTheChartIsDrawnFromWhatWasTyped() => UiThread.Run(() =>
        Edited((editor, rtb, pie) =>
        {
            var before = Share(pie, "Dogs");

            PressPastTheValue(pie, "30");
            MarkdownEditorHarness.RaiseTextInput(rtb, "9");
            MarkdownEditorHarness.Pump();
            pie.UpdateLayout();

            Assert.IsTrue(Share(pie, "Dogs") > before, "the wedge grew with the number");
        }));

    [TestMethod]
    public void BackspaceInAValueTakesOneDigitRatherThanTheWholeNumber() => UiThread.Run(() =>
        Edited((editor, rtb, pie) =>
        {
            PressPastTheValue(pie, "30");
            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Back);
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(pie.Source, "\"Dogs\" : 3", $"the block now reads {pie.Source}");
            Assert.AreEqual(0, pie.Diagnostics.Count, "and the slice still has a value to draw");
        }));

    [TestMethod]
    public void BackspaceAtTheEndOfALabelTakesOneLetter() => UiThread.Run(() =>
        Edited((editor, rtb, pie) =>
        {
            var label = pie.Laid.Root.SelfAndDescendants()
                .First(piece => piece.Kind == PiePiece.Label
                                && piece.Sits().Start == pie.Source.IndexOf("Dogs", System.StringComparison.Ordinal));

            pie.BeginPointerSelect(new Point(label.Bounds.Right - 1, label.Bounds.Y + (label.Bounds.Height / 2)));
            pie.EndPointerSelect();

            MarkdownEditorHarness.RaiseKey(rtb, System.Windows.Input.Key.Back);
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(pie.Source, "\"Dog\" : 30", $"the block now reads {pie.Source}");
        }));

    [TestMethod]
    public void EnterAfterAValueStartsANewSliceToFillIn() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressPastTheValue(pie, "30");
            Press(editor, Key.Enter);

            StringAssert.Contains(pie.Source, "\"Dogs\" : 30\n  \"\" : \n  \"Cats\" : 10", $"a slice of its own under it: {pie.Source}");
            Assert.AreEqual(0, pie.Diagnostics.Count, "with nothing wrong with it — only nothing in it yet");
            Assert.AreEqual(2, pie.Laid.Holes.Count, "a hole for its label and one for its value");
            Assert.AreEqual(pie.Laid.Holes[0].Sits().Start, pie.Caret, "and the caret in the first");

            Write(editor, "Birds");
            Press(editor, Key.Tab);
            Write(editor, "5");

            StringAssert.Contains(pie.Source, "\"Birds\" : 5", $"filled in by typing and tabbing: {pie.Source}");
            StringAssert.Contains(editor.Markdown, "\"Birds\" : 5", "and the document says so too");
            Assert.AreEqual(3, pie.Laid.Root.SelfAndDescendants().Count(piece => piece.Kind == PiePiece.Wedge), "a third wedge");
        }));

    [TestMethod]
    public void EnterAtTheEndOfALabelStartsANewSliceRatherThanBreakingTheLabel() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressPastTheLabel(pie, "Dogs");
            Press(editor, Key.Enter);

            StringAssert.Contains(pie.Source, "\"Dogs\" : 30\n  \"\" : \n", $"under the slice, not through its label: {pie.Source}");
            Assert.AreEqual(0, pie.Diagnostics.Count, "and nothing is wrong");
        }));

    [TestMethod]
    public void DeletingAWholeValueLeavesAHoleToWriteANewOneIn() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressPastTheValue(pie, "30");
            Press(editor, Key.Back);
            Press(editor, Key.Back);

            StringAssert.Contains(pie.Source, "\"Dogs\" : \n", $"the number is gone: {pie.Source}");
            Assert.AreEqual(0, pie.Diagnostics.Count, "and nothing is wrong: it is only still to be written");
            Assert.AreEqual(1, pie.Laid.Holes.Count, "so a hole stands where it goes");
            Assert.AreEqual(pie.Laid.Holes[0].Sits().Start, pie.Caret, "with the caret in it");

            Write(editor, "7");
            StringAssert.Contains(pie.Source, "\"Dogs\" : 7\n", $"and what is typed goes where the hole was: {pie.Source}");
        }));

    [TestMethod]
    public void DeletingAWholeLabelLeavesAHoleToo() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressPastTheLabel(pie, "Cats");
            for (var letter = 0; letter < "Cats".Length; letter++) Press(editor, Key.Back);

            StringAssert.Contains(pie.Source, "\"\" : 10", $"the label is gone: {pie.Source}");
            Assert.AreEqual(0, pie.Diagnostics.Count);
            Assert.AreEqual(1, pie.Laid.Holes.Count);
            Assert.AreEqual(pie.Laid.Holes[0].Sits().Start, pie.Caret);

            Write(editor, "Mice");
            StringAssert.Contains(pie.Source, "\"Mice\" : 10", pie.Source);
        }));

    [TestMethod]
    public void AQuoteTypedIntoALabelIsWrittenAsItsEntityCode() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressPastTheLabel(pie, "Dogs");
            Write(editor, "\"");

            StringAssert.Contains(pie.Source, "\"Dogs#quot;\" : 30", pie.Source);
            Assert.AreEqual(0, pie.Diagnostics.Count, string.Join(" | ", pie.Diagnostics.Select(d => d.Message)));
            Assert.AreEqual(2, pie.Laid.Root.SelfAndDescendants().Count(piece => piece.Kind == PiePiece.Wedge), "and both slices still drawn");
        }));

    [TestMethod]
    public void BackspaceInASliceWithNothingWrittenInItTakesTheSliceBack() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            var before = pie.Source;

            PressPastTheValue(pie, "30");
            Press(editor, Key.Enter);
            Press(editor, Key.Back);

            Assert.AreEqual(before, pie.Source, "Enter pressed once too often, and taken back");
            Assert.AreEqual(before.IndexOf("30", StringComparison.Ordinal) + 2, pie.Caret, "with the caret where it was");
        }));

    [TestMethod]
    public void BackspaceAtAValueStillToComeTakesNothingMore() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressPastTheValue(pie, "30");
            Press(editor, Key.Back);
            Press(editor, Key.Back);
            Press(editor, Key.Back);

            StringAssert.Contains(pie.Source, "\"Dogs\" : \n", $"the colon is not taken: {pie.Source}");
            Assert.AreEqual(pie.Laid.Holes[0].Sits().Start, pie.Caret, "and the caret stays in the hole");
            Assert.AreEqual(0, pie.Diagnostics.Count);
        }));

    [TestMethod]
    public void DeleteAtTheEndOfAValueOrALabelTakesNothingPastIt() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            var before = pie.Source;

            PressPastTheValue(pie, "30");
            Press(editor, Key.Delete);
            Assert.AreEqual(before, pie.Source, "past a value is the end of its line");

            PressPastTheLabel(pie, "Cats");
            Press(editor, Key.Delete);
            Assert.AreEqual(before, pie.Source, "past a label is its closing quote");

            PressPastTheValue(pie, "10");
            Press(editor, Key.Delete);
            Assert.AreEqual(before, pie.Source, "and past the last value, the end of the diagram");
            StringAssert.Contains(editor.Markdown, "\"Cats\" : 10\n```", "which the document keeps too");
        }));

    [TestMethod]
    public void DeleteAtTheEndOfTheTitleTakesNothingPastIt() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            var before = pie.Source;

            PressInTheTitle(pie, atEnd: true);
            Assert.AreEqual(before.IndexOf("Pets", StringComparison.Ordinal) + "Pets".Length, pie.Caret, "precondition: past the title");

            Press(editor, Key.Delete);
            Assert.AreEqual(before, pie.Source, "past the title is the end of its line");
        }, Titled));

    [TestMethod]
    public void BackspaceAtTheStartOfALabelTakesNothingBeforeIt() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            var before = pie.Source;
            var cats = before.IndexOf("Cats", StringComparison.Ordinal);

            pie.TakeCaret(cats);
            Press(editor, Key.Back);

            Assert.AreEqual(before, pie.Source, "before a label is its opening quote");
            Assert.AreEqual(cats, pie.Caret);
        }));

    [TestMethod]
    public void DeleteInsideAWordStillTakesTheCharacterAfterTheCaret() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            pie.TakeCaret(pie.Source.IndexOf("30", StringComparison.Ordinal) + 1);
            Press(editor, Key.Delete);

            StringAssert.Contains(pie.Source, "\"Dogs\" : 3\n", pie.Source);
        }));

    [TestMethod]
    public void DownAndUpMoveBetweenTheRowsOfTheLegend() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressPastTheValue(pie, "30");

            Press(editor, Key.Down);
            Assert.AreEqual(pie.Source.IndexOf("10", StringComparison.Ordinal) + 2, pie.Caret,
                            "down from the end of a value is the end of the value under it");

            Press(editor, Key.Up);
            Assert.AreEqual(pie.Source.IndexOf("30", StringComparison.Ordinal) + 2, pie.Caret, "and up comes back");
        }));

    [TestMethod]
    public void DownFromTheTitleGoesIntoTheLegend_AndUpComesBack() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressInTheTitle(pie);

            Press(editor, Key.Down);
            var dogs = pie.Source.IndexOf("Dogs", StringComparison.Ordinal);
            var thirty = pie.Source.IndexOf("30", StringComparison.Ordinal);
            Assert.IsTrue(pie.Caret >= dogs && pie.Caret <= thirty + 2, $"into the first row of the legend, but the caret is at {pie.Caret}");

            Press(editor, Key.Up);
            var title = pie.Source.IndexOf("Pets", StringComparison.Ordinal);
            Assert.IsTrue(pie.Caret >= title && pie.Caret <= title + 4, $"and back up into the title, but the caret is at {pie.Caret}");
        }, Titled));

    [TestMethod]
    public void UndoTakesAnEditBackWithoutOpeningTheSource() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressPastTheValue(pie, "30");
            Write(editor, "9");
            StringAssert.Contains(editor.Markdown, "\"Dogs\" : 309", "precondition: the edit landed");

            editor.Undo();
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(editor.Markdown, "\"Dogs\" : 30\n", "the edit is taken back");

            var drawn = Find<ContentElement>(editor);
            Assert.IsNotNull(drawn, "and the pie is still drawn, rather than opened as its source");
            Assert.AreSame(drawn, editor.FocusedContent, "with the keys still going to it");
            Assert.AreEqual(drawn!.Source.IndexOf("30", StringComparison.Ordinal) + 2, drawn.Caret, "and the caret where it was");
        }));

    [TestMethod]
    public void ThePointerIsABarOnlyOverWhatIsWrittenIn() => UiThread.Run(() =>
        Edited((editor, rtb, pie) =>
        {
            var block = (IInteractiveBlock)pie;
            Piece First(string kind) => pie.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == kind);
            static Point Middle(Rect box) => new(box.X + (box.Width / 2), box.Y + (box.Height / 2));

            Assert.AreEqual(Cursors.IBeam, block.PointerCursor(Middle(First(PiePiece.Label).Bounds)), "a label is written in");
            Assert.AreEqual(Cursors.IBeam, block.PointerCursor(Middle(First(PiePiece.Value).Bounds)), "and so is a value");
            Assert.AreEqual(Cursors.Arrow, block.PointerCursor(Middle(First(PiePiece.Share).Bounds)), "a share is worked out");
            Assert.AreEqual(Cursors.Arrow, block.PointerCursor(Middle(First(MermaidPiece.Swatch).Bounds)), "a swatch is drawing");
            Assert.AreEqual(Cursors.Arrow, block.PointerCursor(InsideAWedge(pie)), "and so is a wedge");
            Assert.AreEqual(Cursors.Arrow, block.PointerCursor(new Point(1, 1)), "and the card round it all is nothing");
        }));

    [TestMethod]
    public void AndOverAHole() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressPastTheValue(pie, "30");
            Press(editor, Key.Enter);

            var hole = pie.Laid.Holes[0].Bounds;
            Assert.AreEqual(Cursors.IBeam, ((IInteractiveBlock)pie).PointerCursor(new Point(hole.X + (hole.Width / 2), hole.Y + (hole.Height / 2))),
                            "a hole is where writing goes");
        }));

    [TestMethod]
    public void DraggingInsideTheTitlePicksOutLetters() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("pie title Browser share\n  \"Dogs\" : 30", (editor, rtb) =>
        {
            var pie = Find<ContentElement>(editor)!;
            var title = pie.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == MermaidPiece.Title);
            var middle = title.Bounds.Y + (title.Bounds.Height / 2);

            pie.BeginPointerSelect(new Point(title.Bounds.X + 2, middle));
            pie.ExtendPointerSelect(new Point(title.Bounds.X + (title.Bounds.Width / 2), middle));
            pie.EndPointerSelect();

            Assert.AreNotEqual(0, pie.SelectionLength, "something is picked out");
            Assert.IsTrue(pie.SelectionLength < title.Sits().Length,
                          $"part of the title, not all {title.Sits().Length} characters of it");
        },
        editor => editor.SingleBlock = "mermaid"));

    [TestMethod]
    public void TheShareWrittenOnASliceIsNowhereToPutACaret() => UiThread.Run(() =>
        Edited((editor, rtb, pie) =>
        {
            var shares = pie.Laid.Root.SelfAndDescendants().Where(piece => piece.Kind == PiePiece.Share).ToList();

            Assert.IsTrue(shares.Count > 0);
            Assert.IsTrue(shares.All(share => share.Stops == Stops.None),
                          "a worked-out share is not a place to stand, so stepping never stops in one");
        }));

    [TestMethod]
    public void AndSteppingGoesFromOneValueToTheNextLabel() => UiThread.Run(() =>
        Edited((editor, rtb, pie) =>
        {
            PressPastTheValue(pie, "30");
            Assert.IsTrue(pie.MoveCaret(forward: true));

            var cats = pie.Source.IndexOf("Cats", System.StringComparison.Ordinal);
            Assert.AreEqual(cats, pie.Caret, $"the next thing written, not a stop of its own: caret at {pie.Caret}");
        }));

    /// <summary>How much of the chart a slice's wedge covers, as a share of everything drawn.</summary>
    private static double Share(ContentElement pie, string name)
    {
        var wedges = pie.Laid.Root.SelfAndDescendants().Where(piece => piece.Kind == PiePiece.Wedge).ToList();
        var mine = wedges.First(piece => pie.Source.Substring(piece.Sits().Start, piece.Sits().Length).Contains(name));

        var area = wedges.Sum(piece => piece.Bounds.Width * piece.Bounds.Height);
        return area <= 0 ? 0 : mine.Bounds.Width * mine.Bounds.Height / area;
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            if (Find<T>(VisualTreeHelper.GetChild(root, at)) is { } found) return found;

        return null;
    }
}

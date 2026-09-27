using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
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
/// Driven through a real <see cref="Nexaflow.Visuals.Text.Markdown.MarkdownSurface"/>, because that is the whole
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
    private static void Edited(Action<MarkdownSurface, DocumentBlock> test) =>
        MarkdownEditorHarness.Run(Markdown, editor =>
        {
            var pie = MarkdownEditorHarness.Block(editor);
            Assert.IsNotNull(pie, "the pie did not render as content");

            test(editor, pie!);
        },
        editor => editor.WrittenIn = "mermaid");

    /// <summary>Presses just past the last digit of a slice's value, where it is drawn in the legend.</summary>
    private static void PressPastTheValue(DocumentBlock pie, string number)
    {
        var value = pie.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Kind == PiePiece.Value
                            && piece.Sits().Start == pie.Source.IndexOf(number, pie.Start, System.StringComparison.Ordinal));

        pie.BeginPointerSelect(new Point(value.Bounds.Right - 1, value.Bounds.Y + (value.Bounds.Height / 2)));
        pie.EndPointerSelect();
    }

    /// <summary>Presses just inside the end of a slice's label, where it is drawn in the legend.</summary>
    private static void PressPastTheLabel(DocumentBlock pie, string name)
    {
        var label = pie.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Kind == PiePiece.Label
                            && piece.Sits().Start == pie.Source.IndexOf(name, pie.Start, System.StringComparison.Ordinal));

        pie.BeginPointerSelect(new Point(label.Bounds.Right - 1, label.Bounds.Y + (label.Bounds.Height / 2)));
        pie.EndPointerSelect();
    }

    /// <summary>Presses in the title: at its middle, or just inside its end.</summary>
    private static void PressInTheTitle(DocumentBlock pie, bool atEnd = false)
    {
        var title = pie.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == MermaidPiece.Title).Bounds;
        var x = atEnd ? title.Right - 1 : title.X + (title.Width / 2);

        pie.BeginPointerSelect(new Point(x, title.Y + (title.Height / 2)));
        pie.EndPointerSelect();
    }

    /// <summary>The pie inside a document, fenced, which is how most are written.</summary>
    private static void InADocument(Action<MarkdownSurface, DocumentBlock> test, string diagram = Markdown) =>
        MarkdownEditorHarness.Run("Below:\n\n```mermaid\n" + diagram + "\n```\n", editor =>
        {
            var pie = MarkdownEditorHarness.Block(editor);
            Assert.IsNotNull(pie, "the pie did not render as content");

            test(editor, pie!);
        });

    private static void Press(MarkdownSurface editor, Key key)
    {
        MarkdownEditorHarness.RaiseKey(editor, key);
        MarkdownEditorHarness.Pump();
    }

    private static void Write(MarkdownSurface editor, string text)
    {
        MarkdownEditorHarness.RaiseTextInput(editor, text);
        MarkdownEditorHarness.Pump();
    }

    /// <summary>Somewhere inside the first wedge's own shape, and clear of the share written on it.</summary>
    private static Point InsideAWedge(DocumentBlock pie)
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
        Edited((editor, pie) =>
        {
            Assert.IsFalse(pie.IsReadOnly, "a pie's values are written in");

            PressPastTheValue(pie, "30");
            Assert.IsTrue(pie.HasCaret, "a press in the legend takes the caret");

            MarkdownEditorHarness.RaiseTextInput(editor, "9");
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(pie.Source, "\"Dogs\" : 309", $"the block now reads {pie.Source}");
            StringAssert.Contains(editor.Markdown, "\"Dogs\" : 309", "and so does the document");
        }));

    [TestMethod]
    public void AndTheChartIsDrawnFromWhatWasTyped() => UiThread.Run(() =>
        Edited((editor, pie) =>
        {
            var before = Share(pie, "Dogs");

            PressPastTheValue(pie, "30");
            MarkdownEditorHarness.RaiseTextInput(editor, "9");
            MarkdownEditorHarness.Pump();
            editor.UpdateLayout();

            Assert.IsTrue(Share(pie, "Dogs") > before, "the wedge grew with the number");
        }));

    [TestMethod]
    public void BackspaceInAValueTakesOneDigitRatherThanTheWholeNumber() => UiThread.Run(() =>
        Edited((editor, pie) =>
        {
            PressPastTheValue(pie, "30");
            MarkdownEditorHarness.RaiseKey(editor, System.Windows.Input.Key.Back);
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(pie.Source, "\"Dogs\" : 3", $"the block now reads {pie.Source}");
            Assert.AreEqual(0, pie.Diagnostics.Count, "and the slice still has a value to draw");
        }));

    [TestMethod]
    public void BackspaceAtTheEndOfALabelTakesOneLetter() => UiThread.Run(() =>
        Edited((editor, pie) =>
        {
            var label = pie.Laid.Root.SelfAndDescendants()
                .First(piece => piece.Kind == PiePiece.Label
                                && piece.Sits().Start == pie.Source.IndexOf("Dogs", System.StringComparison.Ordinal));

            pie.BeginPointerSelect(new Point(label.Bounds.Right - 1, label.Bounds.Y + (label.Bounds.Height / 2)));
            pie.EndPointerSelect();

            MarkdownEditorHarness.RaiseKey(editor, System.Windows.Input.Key.Back);
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(pie.Source, "\"Dog\" : 30", $"the block now reads {pie.Source}");
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

            var drawn = MarkdownEditorHarness.Block(editor);
            Assert.IsNotNull(drawn, "and the pie is still drawn, rather than opened as its source");
            Assert.IsTrue(drawn.HasCaret, "with the keys still going to it");
            Assert.AreEqual(drawn!.Source.IndexOf("30", StringComparison.Ordinal) + 2, drawn.Caret, "and the caret where it was");
        }));

    [TestMethod]
    public void ThePointerIsABarOnlyOverWhatIsWrittenIn() => UiThread.Run(() =>
        Edited((editor, pie) =>
        {
            var block = pie;
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
            var hole = pie.Laid.Holes[0].Bounds;
            Assert.AreEqual(Cursors.IBeam, pie.PointerCursor(new Point(hole.X + (hole.Width / 2), hole.Y + (hole.Height / 2))),
                            "a hole is where writing goes");
        }, "pie\n  \"Dogs\" : 30\n  \"\" : "));

    [TestMethod]
    public void DraggingInsideTheTitlePicksOutLetters() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("pie title Browser share\n  \"Dogs\" : 30", editor =>
        {
            var pie = MarkdownEditorHarness.Block(editor)!;
            var title = pie.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == MermaidPiece.Title);
            var middle = title.Bounds.Y + (title.Bounds.Height / 2);

            pie.BeginPointerSelect(new Point(title.Bounds.X + 2, middle));
            pie.ExtendPointerSelect(new Point(title.Bounds.X + (title.Bounds.Width / 2), middle));
            pie.EndPointerSelect();

            Assert.AreNotEqual(0, pie.SelectionLength, "something is picked out");
            Assert.IsTrue(pie.SelectionLength < title.Sits().Length,
                          $"part of the title, not all {title.Sits().Length} characters of it");
        },
        editor => editor.WrittenIn = "mermaid"));

    [TestMethod]
    public void TheShareWrittenOnASliceIsNowhereToPutACaret() => UiThread.Run(() =>
        Edited((editor, pie) =>
        {
            var shares = pie.Laid.Root.SelfAndDescendants().Where(piece => piece.Kind == PiePiece.Share).ToList();

            Assert.IsTrue(shares.Count > 0);
            Assert.IsTrue(shares.All(share => share.Stops == Stops.None),
                          "a worked-out share is not a place to stand, so stepping never stops in one");
        }));

    [TestMethod]
    public void AndSteppingGoesFromOneValueToTheNextLabel() => UiThread.Run(() =>
        Edited((editor, pie) =>
        {
            PressPastTheValue(pie, "30");
            Assert.IsTrue(pie.MoveCaret(forward: true));

            var cats = pie.Source.IndexOf("Cats", System.StringComparison.Ordinal);
            Assert.AreEqual(cats, pie.Caret, $"the next thing written, not a stop of its own: caret at {pie.Caret}");
        }));

    /// <summary>How much of the chart a slice's wedge covers, as a share of everything drawn.</summary>
    private static double Share(DocumentBlock pie, string name)
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

    // ── Keys in the legend ──────────────────────────────────────────────────

    [TestMethod]
    public void ShiftEnterAtTheEndOfALabelBreaksItsLine() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressPastTheLabel(pie, "Dogs");
            Press(editor, Key.Enter, ModifierKeys.Shift);

            StringAssert.Contains(pie.Source, "\"Dogs<br>\" : 30\n", pie.Source);
            Assert.AreEqual(0, pie.Diagnostics.Count);
        }));

    [TestMethod]
    public void EnterAtTheEndOfALabelStartsTheNextSliceUnderIt_ToFillIn() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressPastTheLabel(pie, "Dogs");
            Press(editor, Key.Enter);

            StringAssert.Contains(pie.Source, "\"Dogs\" : 30\n  \"\" : \n  \"Cats\" : 10", $"a slice of its own under it: {pie.Source}");
            Assert.AreEqual(0, pie.Diagnostics.Count, "with nothing wrong with it — only nothing in it yet");
            Assert.AreEqual(2, pie.Laid.Holes.Count, "a hole for its label and one for its value");
            Assert.AreEqual(pie.Laid.Holes[0].Sits().Start, pie.Caret, "and the caret in the label's");

            Write(editor, "Birds");
            Press(editor, Key.Tab);
            Write(editor, "5");

            StringAssert.Contains(pie.Source, "\"Birds\" : 5", $"filled in by typing and tabbing: {pie.Source}");
            Assert.AreEqual(3, pie.Laid.Root.SelfAndDescendants().Count(piece => piece.Kind == PiePiece.Wedge), "a third wedge");
        }));

    [TestMethod]
    public void EnterAtTheEndOfAValueStartsTheNextSliceToo() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressPastTheValue(pie, "10");
            Press(editor, Key.Enter);

            StringAssert.Contains(pie.Source, "\"Cats\" : 10\n  \"\" : \n```", $"under the last slice, before the fence: {pie.Source}");
            Assert.AreEqual(pie.Laid.Holes[0].Sits().Start, pie.Caret);
        }));

    [TestMethod]
    public void EnterAtTheEndOfTheTitleStartsTheFirstSlice() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressInTheTitle(pie, atEnd: true);
            Press(editor, Key.Enter);

            StringAssert.Contains(pie.Source, "title Pets\n  \"\" : \n  \"Dogs\" : 30", $"first in the legend: {pie.Source}");
            Assert.AreEqual(pie.Laid.Holes[0].Sits().Start, pie.Caret);
        }, Titled));

    [TestMethod]
    public void DeleteAtTheEndOfALabelOrAValueTakesNothing() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            var before = pie.Source;

            PressPastTheLabel(pie, "Dogs");
            Press(editor, Key.Delete);
            Assert.AreEqual(before, pie.Source, "past a label is its closing quote");

            PressPastTheValue(pie, "30");
            Press(editor, Key.Delete);
            Assert.AreEqual(before, pie.Source, "past a value is the end of its line");

            PressPastTheValue(pie, "10");
            Press(editor, Key.Delete);
            Assert.AreEqual(before, pie.Source, "and past the last value, the end of the diagram");
        }));

    [TestMethod]
    public void BackspaceAtTheStartOfALabelOrAValueTakesNothing() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            var before = pie.Source;

            pie.TakeCaret(before.IndexOf("Cats", StringComparison.Ordinal));
            Press(editor, Key.Back);
            Assert.AreEqual(before, pie.Source, "before a label is its opening quote");

            pie.TakeCaret(before.IndexOf("10", StringComparison.Ordinal));
            Press(editor, Key.Back);
            Assert.AreEqual(before, pie.Source, "before a value is the colon");
        }));

    [TestMethod]
    public void TabGoesFromALabelToItsValue_AndFromAValueToTheNextLabel() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressPastTheLabel(pie, "Dogs");

            Press(editor, Key.Tab);
            Assert.AreEqual(pie.Source.IndexOf("30", StringComparison.Ordinal), pie.Caret, "to the start of its value");

            Press(editor, Key.Tab);
            Assert.AreEqual(pie.Source.IndexOf("Cats", StringComparison.Ordinal), pie.Caret, "to the start of the next label");
        }));

    [TestMethod]
    public void RightGoesFromTheEndOfALabelToItsValue_AndFromTheEndOfAValueToTheNextLabel() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressPastTheLabel(pie, "Dogs");

            Press(editor, Key.Right);
            Assert.AreEqual(pie.Source.IndexOf("30", StringComparison.Ordinal), pie.Caret, "to the start of its value");

            PressPastTheValue(pie, "30");

            Press(editor, Key.Right);
            Assert.AreEqual(pie.Source.IndexOf("Cats", StringComparison.Ordinal), pie.Caret, "to the start of the next label");
        }));

    // ── A slice chosen whole ────────────────────────────────────────────────

    [TestMethod]
    public void DeleteTakesAChosenSliceAndItsLine() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            ChooseTheRow(pie, "Dogs");
            Press(editor, Key.Delete);

            StringAssert.Contains(pie.Source, "```mermaid\npie showData\n  \"Cats\" : 10\n```", pie.Source);
            Assert.AreEqual(1, pie.Laid.Root.SelfAndDescendants().Count(piece => piece.Kind == PiePiece.Wedge), "one wedge left");

            ChooseTheRow(pie, "Cats");
            Press(editor, Key.Delete);

            StringAssert.Contains(pie.Source, "pie showData\n  \"\" : \n```", $"the last one there was left as one to write: {pie.Source}");
            Assert.AreEqual(pie.Laid.Holes[0].Sits().Start, pie.Caret, "with the caret in its label");
        }));

    [TestMethod]
    public void DeleteTakingTheLastLineLeavesNoEmptyLineBehind() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            ChooseTheRow(pie, "Cats");
            Press(editor, Key.Delete);

            StringAssert.Contains(pie.Source, "\"Dogs\" : 30\n```", pie.Source);
        }));

    [TestMethod]
    public void InsertStartsASliceAboveTheChosenOne() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            ChooseTheRow(pie, "Cats");
            Press(editor, Key.Insert);

            StringAssert.Contains(pie.Source, "\"Dogs\" : 30\n  \"\" : \n  \"Cats\" : 10", pie.Source);
            Assert.AreEqual(pie.Laid.Holes[0].Sits().Start, pie.Caret, "with the caret in its label");
        }));

    // ── The ribbon ──────────────────────────────────────────────────────────

    [TestMethod]
    public void DraggingAChosenSliceOntoAnotherRowPutsItThere() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            ChooseTheRow(pie, "Dogs");

            var from = Middle(Row(pie, "Dogs").SelfAndDescendants().First(piece => piece.Kind == MermaidPiece.Swatch).Bounds);
            var to = Middle(Row(pie, "Birds").Bounds);

            pie.BeginPointerSelect(from);
            pie.ExtendPointerSelect(new Point(from.X, from.Y + ((to.Y - from.Y) / 2)));
            pie.ExtendPointerSelect(to);
            pie.EndPointerSelect();
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(pie.Source, "pie showData\n  \"Cats\" : 10\n  \"Birds\" : 5\n  \"Dogs\" : 30\n```", $"after the row it was let go on: {pie.Source}");
            Assert.AreEqual(0, pie.Diagnostics.Count);
        }, "pie showData\n  \"Dogs\" : 30\n  \"Cats\" : 10\n  \"Birds\" : 5"));

    private static Point Middle(Rect box) => new(box.X + (box.Width / 2), box.Y + (box.Height / 2));

    [TestMethod]
    public void TheLegendsRibbonSaysWhereItGoes_AndChoosingMovesIt() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            var ribbon = Ribbon(editor, Row(pie, "Dogs").Bounds);

            CollectionAssert.AreEqual(new[] { "pie.legend.left", "pie.legend.top", "pie.legend.right", "pie.legend.bottom", "pie.legend.center" },
                                      ribbon.Offers.Where(offer => offer.Group == "Legend").Select(offer => offer.Verb).ToArray());
            Assert.AreEqual("pie.legend.right", ribbon.Offers.Single(offer => offer.Current).Verb, "where Mermaid puts it when nothing says");

            Click(ribbon, "pie.legend.left");
            StringAssert.Contains(editor.Markdown, "```mermaid\n---\nconfig:\n  pie:\n    legendPosition: left\n---\npie showData", editor.Markdown);

            Click(Ribbon(editor, Row(MarkdownEditorHarness.Block(editor)!, "Dogs").Bounds), "pie.legend.top");
            StringAssert.Contains(editor.Markdown, "    legendPosition: top\n---", $"the one field given its new value: {editor.Markdown}");
        }));

    [TestMethod]
    public void TheChartsRibbonOffersItsHoleAndItsStroke_AndChoosingWritesThem() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            var ribbon = Ribbon(editor, new Rect(InsideAWedge(pie), new Size(0, 0)));

            CollectionAssert.AreEqual(new[] { "pie.hole.0", "pie.hole.0.2", "pie.hole.0.5", "pie.hole.0.8" },
                                      ribbon.Offers.Where(offer => offer.Group == "Hole").Select(offer => offer.Verb).ToArray());
            CollectionAssert.AreEqual(new[] { "pie.stroke.0", "pie.stroke.2", "pie.stroke.4" },
                                      ribbon.Offers.Where(offer => offer.Group == "Stroke").Select(offer => offer.Verb).ToArray());
            CollectionAssert.AreEquivalent(new[] { "pie.hole.0", "pie.stroke.2" }, ribbon.Offers.Where(offer => offer.Current).Select(offer => offer.Verb).ToArray(),
                                           "no hole and a thin line, as Mermaid draws a pie when nothing says");
            Assert.IsFalse(ribbon.Offers.Any(offer => offer.Group == "Legend"), "where the legend goes is the legend's");

            Click(ribbon, "pie.hole.0.5");
            StringAssert.Contains(editor.Markdown, "config:\n  pie:\n    donutHole: 0.5\n---", editor.Markdown);

            Click(Ribbon(editor, new Rect(InsideAWedge(MarkdownEditorHarness.Block(editor)!), new Size(0, 0))), "pie.stroke.4");
            StringAssert.Contains(editor.Markdown, "config:\n  themeVariables:\n    pieStrokeWidth: 4\n  pie:\n    donutHole: 0.5\n---", editor.Markdown);
        }));

    [TestMethod]
    public void AChosenSliceIsNoPlaceToWrite_SoThereIsNoCaret() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            PressPastTheLabel(pie, "Dogs");
            Assert.IsTrue(pie.ShowsCaret, "a press in a label is a place to write");

            ChooseTheRow(pie, "Dogs");
            Assert.IsFalse(pie.ShowsCaret, "a slice chosen whole shows no caret");

            PressPastTheValue(pie, "10");
            Assert.IsTrue(pie.ShowsCaret, "and a press back in the legend is somewhere to write again");
        }));

    [TestMethod]
    public void APasteInALabelIsWrittenSoTheLabelStillReads_AndTheCaretStandsAfterIt() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            Holding(editor, " \"good\"\ndogs");
            PressPastTheLabel(pie, "Dogs");

            Assert.IsTrue(editor.Paste());
            Write(editor, "!");

            StringAssert.Contains(editor.Markdown, "  \"Dogs #quot;good#quot;<br>dogs!\" : 30\n", editor.Markdown);
            Assert.AreEqual(0, MarkdownEditorHarness.Block(editor)!.Diagnostics.Count);
        }));

    [TestMethod]
    public void APasteInAValueTakesANumber_AndNothingElse() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            Holding(editor, "5");

            PressPastTheValue(pie, "30");
            editor.Paste();
            StringAssert.Contains(editor.Markdown, "\"Dogs\" : 305\n", editor.Markdown);

            Holding(editor, "five");
            editor.Paste();
            Holding(editor, ".5.");
            editor.Paste();
            StringAssert.Contains(editor.Markdown, "\"Dogs\" : 305\n", "what is not a number is not pasted into one");
        }));

    [TestMethod]
    public void APasteInTheTitleStaysOnItsOneLine() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            Holding(editor, " and\nfriends");
            PressInTheTitle(pie, atEnd: true);

            Assert.IsTrue(Ribbon(editor, pie.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == MermaidPiece.Title).Bounds)
                              .Offers.Any(offer => offer.Verb == LayoutVerbs.Paste), "the title holds words");

            editor.Paste();

            StringAssert.Contains(editor.Markdown, "  title Pets and friends\n  \"Dogs\" : 30", editor.Markdown);
            Assert.AreEqual(0, MarkdownEditorHarness.Block(editor)!.Diagnostics.Count);
        }, Titled));

    [TestMethod]
    public void APasteOverMoreThanOnePlaceWritesNothing() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            var before = editor.Markdown;
            Holding(editor, "words");

            // From inside a label into its value: past the quote that holds the slice together, so no one place takes the words.
            var from = before.IndexOf("Dogs", StringComparison.Ordinal) + 2;
            editor.Shown.Select(from, before.IndexOf("30", StringComparison.Ordinal) + 1 - from);
            editor.Paste();

            Assert.AreEqual(before, editor.Markdown, "a paste over a label's end and a value's start writes nothing");
        }));

    [TestMethod]
    public void TheRibbonOffersPasteOnlyWithTheCaretInALabelOrAValue_AndNeverCut() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            Holding(editor, "Big ");
            PressPastTheLabel(pie, "Cats");

            var ribbon = Ribbon(editor, Row(pie, "Cats").Bounds);
            Assert.IsTrue(ribbon.Offers.Any(offer => offer.Verb == LayoutVerbs.Paste), "the caret is in a label");
            Assert.IsFalse(ribbon.Offers.Any(offer => offer.Verb == "cut"), "nothing is cut from a pie");

            Click(ribbon, LayoutVerbs.Paste);
            StringAssert.Contains(editor.Markdown, "\"CatsBig \" : 10", editor.Markdown);

            pie = MarkdownEditorHarness.Block(editor)!;
            ChooseTheRow(pie, "Dogs");
            var chosen = Ribbon(editor, Row(pie, "Dogs").Bounds);
            Assert.IsFalse(chosen.Offers.Any(offer => offer.Verb is LayoutVerbs.Paste or "cut"), "with a slice chosen there is no caret to paste at");
        }));

    [TestMethod]
    public void EveryOptionOnThePiesRibbonIsDrawn_AndSaysInWordsWhatItIs() => UiThread.Run(() =>
        InADocument((editor, pie) =>
        {
            var offers = Ribbon(editor, Row(pie, "Dogs").Bounds).Offers
                .Concat(Ribbon(editor, new Rect(InsideAWedge(pie), new Size(0, 0))).Offers)
                .Where(offer => offer.Group is not null)
                .ToList();

            Assert.AreEqual(12, offers.Count, "five places, four holes, three strokes");
            Assert.IsTrue(offers.All(offer => offer.Shape is { IsFrozen: true } shape && !shape.IsEmpty()), "each drawn as what it would make");
            Assert.IsTrue(offers.All(offer => offer.Tip is { Length: > 0 }), "and named, for whoever hovers or hears the screen read");
        }));

    /// <summary>Has <paramref name="editor"/> handed <paramref name="words"/> to paste, as the clipboard would.</summary>
    private static void Holding(MarkdownSurface editor, string words) =>
        MarkdownEditorHarness.Clipboard = new DataObject(DataFormats.UnicodeText, words);

    /// <summary>The legend's row for the slice labelled <paramref name="name"/>.</summary>
    private static Piece Row(DocumentBlock pie, string name)
    {
        var at = pie.Source.IndexOf(name, pie.Start, StringComparison.Ordinal);
        return pie.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == MermaidPiece.Key && piece.Sits().Start <= at && at < piece.Sits().End);
    }

    /// <summary>Chooses a slice whole, as a press on its swatch in the legend does.</summary>
    private static void ChooseTheRow(DocumentBlock pie, string name)
    {
        var swatch = Row(pie, name).SelfAndDescendants().First(piece => piece.Kind == MermaidPiece.Swatch).Bounds;

        pie.BeginPointerSelect(new Point(swatch.X + (swatch.Width / 2), swatch.Y + (swatch.Height / 2)));
        pie.EndPointerSelect();

        Assert.IsTrue(pie.SelectionLength > 0, $"pressing {name}'s swatch chooses its slice");
    }

    /// <summary>The ribbon a right-click in the middle of <paramref name="over"/> opens.</summary>
    private static DiagramRibbon Ribbon(MarkdownSurface editor, Rect over)
    {
        var ribbon = editor.Shown.BuildRibbon(new Point(over.X + (over.Width / 2), over.Y + (over.Height / 2))) as DiagramRibbon;
        Assert.IsNotNull(ribbon, "a right-click opens the ribbon");
        return ribbon!;
    }

    /// <summary>Clicks the ribbon's button for <paramref name="verb"/>.</summary>
    private static void Click(DiagramRibbon ribbon, string verb)
    {
        var button = Logical(ribbon).OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "Diagram_Ribbon_" + verb);
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        MarkdownEditorHarness.Pump();
    }

    private static IEnumerable<DependencyObject> Logical(DependencyObject from)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(from).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var further in Logical(child)) yield return further;
        }
    }

    private static void Press(MarkdownSurface editor, Key key, ModifierKeys modifiers)
    {
        editor.Pressed(key, modifiers);
        MarkdownEditorHarness.Pump();
    }
}

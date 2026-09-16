using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a Venn diagram through the editor that hosts it: a label drawn in a circle is the characters of the label,
/// so a press puts the caret between its letters and a keystroke changes the diagram — and Enter starts the next item
/// with a hole for its name.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("venn")]
public class VennEditingTests
{
    private const string Teams = "venn-beta\n  set A[\"Frontend\"]\n    text A1[\"React\"]\n  set B[\"Backend\"]\n  union A,B[\"Shared\"]";

    /// <summary>The diagram inside a document, fenced, with the editor handing its keys to it.</summary>
    private static void InADocument(Action<InlineMarkdownEditor, RichTextBox, ContentElement> test, string diagram = Teams) =>
        MarkdownEditorHarness.Run("Teams:\n\n```mermaid\n" + diagram + "\n```\n", (editor, rtb) =>
        {
            var venn = Find<ContentElement>(editor);
            Assert.IsNotNull(venn, "the diagram did not render as content");
            Assert.IsTrue(editor.FocusBlockAtCaret(), "the editor has the diagram to give the keys to");

            test(editor, rtb, venn!);
        });

    /// <summary>Presses just inside the end of what a region or an item says, where it is drawn.</summary>
    private static void PressPast(ContentElement venn, string words)
    {
        var piece = venn.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Kind is VennPiece.Label or VennPiece.Text
                            && piece.Sits().Start == venn.Source.IndexOf(words, StringComparison.Ordinal));

        venn.BeginPointerSelect(new Point(piece.Bounds.Right - 1, piece.Bounds.Y + (piece.Bounds.Height / 2)));
        venn.EndPointerSelect();
    }

    private static void Press(RichTextBox rtb, Key key)
    {
        MarkdownEditorHarness.RaiseKey(rtb, key);
        MarkdownEditorHarness.Pump();
    }

    private static void Write(RichTextBox rtb, string text)
    {
        MarkdownEditorHarness.RaiseTextInput(rtb, text);
        MarkdownEditorHarness.Pump();
    }

    [TestMethod]
    public void TypingInALabelChangesTheLabel() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            Assert.IsFalse(venn.IsReadOnly, "a Venn diagram's labels are written in");

            PressPast(venn, "Frontend");
            Assert.IsTrue(venn.HasCaret, "a press on a label takes the caret");

            Write(rtb, "s");

            StringAssert.Contains(venn.Source, "set A[\"Frontends\"]", venn.Source);
            StringAssert.Contains(editor.Markdown, "set A[\"Frontends\"]", "and so does the document");
        }));

    [TestMethod]
    public void ABackslashInALabelIsOnlyACharacter() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            PressPast(venn, "Frontend");
            Write(rtb, "\\");

            var drawn = Find<ContentElement>(editor);
            Assert.IsNotNull(drawn, $"the diagram is still drawn: {editor.Markdown}");
            StringAssert.Contains(drawn!.Source, "set A[\"Frontend\\\"]", $"the block reads {drawn.Source}");
            StringAssert.Contains(editor.Markdown, "set A[\"Frontend\\\"]", $"and so does the document: {editor.Markdown}");
            Assert.AreEqual(0, drawn.Diagnostics.Count, string.Join(" | ", drawn.Diagnostics.Select(d => d.Message)));

            Write(rtb, "n");
            StringAssert.Contains(Find<ContentElement>(editor)!.Source, "set A[\"Frontend\\n\"]", "and typing goes on after it");
        }));

    /// <summary>Sets with nothing but their names, and a label in brackets without quotes.</summary>
    private const string Bare = "venn-beta\n  set Frontend\n    text A1[\"React\"]\n  set Backend[Server]";

    [TestMethod]
    public void WhatABareNameCannotHoldPutsTheNameInQuotes() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            PressPast(venn, "Frontend");
            Write(rtb, "\\");

            StringAssert.Contains(venn.Source, "set \"Frontend\\\"\n", $"the name, quoted to hold it: {venn.Source}");
            Assert.AreEqual(0, venn.Diagnostics.Count, string.Join(" | ", venn.Diagnostics.Select(d => d.Message)));

            Press(rtb, Key.Space);
            Write(rtb, "x");

            StringAssert.Contains(venn.Source, "set \"Frontend\\ x\"\n", $"and typing goes on inside the quotes: {venn.Source}");
            Assert.IsTrue(Drawn(venn, "React"), "with the item still in its set");
            StringAssert.Contains(editor.Markdown, "set \"Frontend\\ x\"", "and the document says so too");
        }, Bare));

    [TestMethod]
    public void AQuoteTypedIntoALabelIsWrittenAsItsEntityCode_AndReadsAsAQuote() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            PressPast(venn, "Frontend");
            Write(rtb, "\"");
            Write(rtb, "s");

            StringAssert.Contains(venn.Source, "set A[\"Frontend#quot;s\"]", venn.Source);
            Assert.AreEqual(0, venn.Diagnostics.Count, string.Join(" | ", venn.Diagnostics.Select(d => d.Message)));
            Assert.IsTrue(Drawn(venn, "Frontend#quot;s"), "shown as written while the caret is in it");

            PressPast(venn, "React");
            Assert.IsTrue(Drawn(venn, "Frontend\"s"), "and as what it says once the caret has left");
        }));

    [TestMethod]
    public void ALabelInBracketsGivenABracketIsPutInQuotes() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            PressPast(venn, "Server");
            Write(rtb, "]");

            StringAssert.Contains(venn.Source, "set Backend[\"Server]\"]", venn.Source);
            Assert.AreEqual(0, venn.Diagnostics.Count, string.Join(" | ", venn.Diagnostics.Select(d => d.Message)));
        }, Bare));

    /// <summary>Names that other lines use: a set in a union and a style, an item in a style.</summary>
    private const string Named =
        "venn-beta\n  set Frontend\n    text A1\n  set Backend\n  union Frontend,Backend[\"APIs\"]\n"
        + "  style Frontend fill:#4e79a7\n  style A1 color:red";

    [TestMethod]
    public void RenamingASetWhereItIsDeclaredRenamesItWhereverItIsUsed() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            PressPast(venn, "Frontend");
            Write(rtb, "s");

            StringAssert.Contains(venn.Source, "set Frontends\n", venn.Source);
            StringAssert.Contains(venn.Source, "union Frontends,Backend[\"APIs\"]", $"the union still overlaps it: {venn.Source}");
            StringAssert.Contains(venn.Source, "style Frontends fill", $"and the style still styles it: {venn.Source}");
            Assert.AreEqual(0, venn.Diagnostics.Count, string.Join(" | ", venn.Diagnostics.Select(d => d.Message)));
            Assert.AreEqual(venn.Source.IndexOf("Frontends", StringComparison.Ordinal) + "Frontends".Length, venn.Caret,
                            "with the caret after what was typed");

            Press(rtb, Key.Space);
            Write(rtb, "2");

            StringAssert.Contains(venn.Source, "union \"Frontends 2\",Backend", $"quoted where it is used, as where it is declared: {venn.Source}");
            Assert.AreEqual(0, venn.Diagnostics.Count, string.Join(" | ", venn.Diagnostics.Select(d => d.Message)));
            Assert.AreEqual(1, venn.Laid.Root.SelfAndDescendants().Count(piece => piece.Kind == VennPiece.Overlap), "and the overlap is still drawn");

            Press(rtb, Key.Back);
            Press(rtb, Key.Back);

            StringAssert.Contains(venn.Source, "union Frontends,Backend", $"and taken back, bare again where it is used: {venn.Source}");
            StringAssert.Contains(editor.Markdown, "style Frontends fill", "which the document says too");
        }, Named));

    [TestMethod]
    public void RenamingAnItemRenamesTheStyleThatNamesIt() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            PressPast(venn, "A1");
            Write(rtb, "x");

            StringAssert.Contains(venn.Source, "text A1x\n", venn.Source);
            StringAssert.Contains(venn.Source, "style A1x color:red", venn.Source);
            Assert.AreEqual(0, venn.Diagnostics.Count, string.Join(" | ", venn.Diagnostics.Select(d => d.Message)));
        }, Named));

    [TestMethod]
    public void ARenameOntoAnotherNameIsNotCarriedToWhereTheOldOneIsUsed() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            PressPast(venn, "A");
            Write(rtb, "B");
            Write(rtb, "C");

            StringAssert.Contains(venn.Source, "set ABC\n", venn.Source);
            StringAssert.Contains(venn.Source, "union A,AB", $"neither name's uses are guessed at: {venn.Source}");
        }, "venn-beta\n  set A\n  set AB\n  union A,AB"));

    [TestMethod]
    public void CtrlAddsEachThingPressedToWhatIsChosen_AndTakesItBackOut() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            foreach (var words in new[] { "Frontend", "Shared" })
            {
                venn.BeginPointerSelect(Middle(venn, words), ModifierKeys.Control);
                venn.EndPointerSelect();
            }

            CollectionAssert.AreEqual(new[] { "Frontend", "Shared" }, Chosen(venn), "both, and nothing between them");

            venn.BeginPointerSelect(Middle(venn, "Frontend"), ModifierKeys.Control);
            venn.EndPointerSelect();

            CollectionAssert.AreEqual(new[] { "Shared" }, Chosen(venn), "and pressed again, the first is let go");
        }));

    [TestMethod]
    public void ShiftChoosesFromTheCaretToThePress() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            PressPast(venn, "Frontend");

            var label = Words(venn, "Frontend");
            venn.BeginPointerSelect(new Point(label.Bounds.X + 1, label.Bounds.Y + (label.Bounds.Height / 2)), ModifierKeys.Shift);
            venn.EndPointerSelect();

            CollectionAssert.AreEqual(new[] { "Frontend" }, Chosen(venn), "back along the label to where it was pressed");

            venn.BeginPointerSelect(Middle(venn, "Backend"), ModifierKeys.Shift);
            venn.EndPointerSelect();

            var chosen = string.Concat(Chosen(venn));
            Assert.IsTrue(chosen.Contains("Frontend") && chosen.Contains("Backend"), $"and on to a label further on: {chosen}");
        }));

    /// <summary>The piece drawing a run of words that starts where <paramref name="words"/> is written.</summary>
    private static Piece Words(ContentElement venn, string words) =>
        venn.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Words is not null && piece.Sits().Start == venn.Source.IndexOf(words, StringComparison.Ordinal));

    private static Point Middle(ContentElement venn, string words)
    {
        var bounds = Words(venn, words).Bounds;
        return new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    /// <summary>What is chosen, a stretch at a time.</summary>
    private static string[] Chosen(ContentElement venn) =>
        [.. venn.Selection.Select(range => venn.Source.Substring(range.Start, range.Length))];

    /// <summary>Whether a run of words reads <paramref name="text"/>.</summary>
    private static bool Drawn(ContentElement venn, string text) =>
        venn.Laid.Root.SelfAndDescendants().Any(piece => piece.Words?.Glyphs.Text == text);

    [TestMethod]
    public void EnterInASetsLabelStartsAnItemInItToName() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            PressPast(venn, "Backend");
            Press(rtb, Key.Enter);

            StringAssert.Contains(venn.Source, "set B[\"Backend\"]\n    text \"\"\n  union", $"an item under the set: {venn.Source}");
            Assert.AreEqual(0, venn.Diagnostics.Count, "with nothing wrong with it — only nothing in it yet");
            Assert.AreEqual(1, venn.Laid.Holes.Count, "a hole for its name");
            Assert.AreEqual(venn.Laid.Holes[0].Sits().Start, venn.Caret, "with the caret in it");

            Write(rtb, "Go");

            StringAssert.Contains(venn.Source, "text \"Go\"", venn.Source);
            Assert.IsTrue(venn.Laid.Root.SelfAndDescendants().Any(piece => piece.Kind == VennPiece.Text && piece.Words?.Glyphs.Text == "Go"),
                          "and it is written in the circle");
        }));

    [TestMethod]
    public void EnterAfterAnItemStartsAnotherInTheSameRegion() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            PressPast(venn, "React");
            Press(rtb, Key.Enter);
            Write(rtb, "Vue");

            StringAssert.Contains(venn.Source, "text A1[\"React\"]\n    text \"Vue\"\n  set B", venn.Source);
            Assert.AreEqual(0, venn.Diagnostics.Count);
        }));

    [TestMethod]
    public void BackspaceInAnItemNothingIsWrittenInTakesItBack() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            var before = venn.Source;

            PressPast(venn, "React");
            Press(rtb, Key.Enter);
            Press(rtb, Key.Back);

            Assert.AreEqual(before, venn.Source, "Enter pressed once too often, and taken back");
            var line = before.IndexOf("React\"]", StringComparison.Ordinal);
            Assert.IsTrue(venn.Caret >= line + "React".Length && venn.Caret <= line + "React\"]".Length, $"with the caret back on the item, but it is at {venn.Caret}");
        }));

    [TestMethod]
    public void DeletingAWholeLabelLeavesAHoleToWriteANewOneIn() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            PressPast(venn, "Shared");
            for (var letter = 0; letter < "Shared".Length; letter++) Press(rtb, Key.Back);

            StringAssert.Contains(venn.Source, "union A,B[\"\"]", $"the label is gone: {venn.Source}");
            Assert.AreEqual(0, venn.Diagnostics.Count);
            Assert.AreEqual(1, venn.Laid.Holes.Count, "a hole stands where it goes");
            Assert.AreEqual(venn.Laid.Holes[0].Sits().Start, venn.Caret);

            Write(rtb, "Both");
            StringAssert.Contains(venn.Source, "union A,B[\"Both\"]", venn.Source);
        }));

    [TestMethod]
    public void DeleteAtTheEndOfALabelTakesNothingPastIt() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            var before = venn.Source;

            PressPast(venn, "Frontend");
            Press(rtb, Key.Delete);

            Assert.AreEqual(before, venn.Source, "past a label is its closing quote");
        }));

    [TestMethod]
    public void UndoTakesAnEditBackWithTheDiagramStillDrawn() => UiThread.Run(() =>
        InADocument((editor, rtb, venn) =>
        {
            PressPast(venn, "Frontend");
            Write(rtb, "s");
            StringAssert.Contains(editor.Markdown, "Frontends", "precondition: the edit landed");

            editor.Undo();
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(editor.Markdown, "set A[\"Frontend\"]", "the edit is taken back");
            Assert.IsNotNull(Find<ContentElement>(editor), "and the diagram is still drawn, rather than opened as its source");
        }));

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            if (Find<T>(VisualTreeHelper.GetChild(root, at)) is { } found) return found;

        return null;
    }
}

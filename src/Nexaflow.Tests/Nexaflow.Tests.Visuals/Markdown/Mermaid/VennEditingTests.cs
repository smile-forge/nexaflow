using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Venn;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// Writing in a Venn diagram through the editor that hosts it: a label drawn in a circle is the characters of the label,
/// so a press puts the caret between its letters and a keystroke changes the diagram.
///
/// <para>
/// Only what is written in a label. Adding a set, a region or an overlap is not a keystroke: which circles a new region
/// covers is the whole of what makes it one, so it has to be said by choosing them, and that is the ribbon's to offer
/// rather than Enter's to guess. Enter is not answered here and writes nothing.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("venn")]
public class VennEditingTests
{
    private const string Teams = "venn-beta\n  set A[\"Frontend\"]\n    text A1[\"React\"]\n  set B[\"Backend\"]\n  union A,B[\"Shared\"]";

    /// <summary>The diagram inside a document, fenced, with the editor handing its keys to it.</summary>
    private static void InADocument(Action<MarkdownSurface, DocumentBlock> test, string diagram = Teams) =>
        MarkdownEditorHarness.Run("Teams:\n\n```mermaid\n" + diagram + "\n```\n", editor =>
        {
            var venn = MarkdownEditorHarness.Block(editor);
            Assert.IsNotNull(venn, "the diagram did not render as content");

            test(editor, venn!);
        });

    /// <summary>Presses just inside the end of what a region or an item says, where it is drawn.</summary>
    private static void PressPast(DocumentBlock venn, string words)
    {
        var piece = venn.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Kind is VennPiece.Label or VennPiece.Text
                            && piece.Sits().Start == venn.Source.IndexOf(words, venn.Start, System.StringComparison.Ordinal));

        venn.BeginPointerSelect(new Point(piece.Bounds.Right - 1, piece.Bounds.Y + (piece.Bounds.Height / 2)));
        venn.EndPointerSelect();
    }

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

    [TestMethod]
    public void TypingInALabelChangesTheLabel() => UiThread.Run(() =>
        InADocument((editor, venn) =>
        {
            Assert.IsFalse(venn.IsReadOnly, "a Venn diagram's labels are written in");

            PressPast(venn, "Frontend");
            Assert.IsTrue(venn.HasCaret, "a press on a label takes the caret");

            Write(editor, "s");

            StringAssert.Contains(venn.Source, "set A[\"Frontends\"]", venn.Source);
            StringAssert.Contains(editor.Markdown, "set A[\"Frontends\"]", "and so does the document");
        }));

    [TestMethod]
    public void ABackslashInALabelIsOnlyACharacter() => UiThread.Run(() =>
        InADocument((editor, venn) =>
        {
            PressPast(venn, "Frontend");
            Write(editor, "\\");

            var drawn = MarkdownEditorHarness.Block(editor);
            Assert.IsNotNull(drawn, $"the diagram is still drawn: {editor.Markdown}");
            StringAssert.Contains(drawn!.Source, "set A[\"Frontend\\\"]", $"the block reads {drawn.Source}");
            StringAssert.Contains(editor.Markdown, "set A[\"Frontend\\\"]", $"and so does the document: {editor.Markdown}");
            Assert.AreEqual(0, drawn.Diagnostics.Count, string.Join(" | ", drawn.Diagnostics.Select(d => d.Message)));

            Write(editor, "n");
            StringAssert.Contains(MarkdownEditorHarness.Block(editor)!.Source, "set A[\"Frontend\\n\"]", "and typing goes on after it");
        }));

    /// <summary>Sets with nothing but their names, and a label in brackets without quotes.</summary>
    private const string Bare = "venn-beta\n  set Frontend\n    text A1[\"React\"]\n  set Backend[Server]";

    [TestMethod]
    public void WhatABareNameCannotHoldPutsTheNameInQuotes() => UiThread.Run(() =>
        InADocument((editor, venn) =>
        {
            PressPast(venn, "Frontend");
            Write(editor, "\\");

            StringAssert.Contains(venn.Source, "set \"Frontend\\\"\n", $"the name, quoted to hold it: {venn.Source}");
            Assert.AreEqual(0, venn.Diagnostics.Count, string.Join(" | ", venn.Diagnostics.Select(d => d.Message)));

            Press(editor, Key.Space);
            Write(editor, "x");

            StringAssert.Contains(venn.Source, "set \"Frontend\\ x\"\n", $"and typing goes on inside the quotes: {venn.Source}");
            Assert.IsTrue(Drawn(venn, "React"), "with the item still in its set");
            StringAssert.Contains(editor.Markdown, "set \"Frontend\\ x\"", "and the document says so too");
        }, Bare));

    [TestMethod]
    public void AQuoteTypedIntoALabelIsWrittenAsItsEntityCode_AndReadsAsAQuote() => UiThread.Run(() =>
        InADocument((editor, venn) =>
        {
            PressPast(venn, "Frontend");
            Write(editor, "\"");
            Write(editor, "s");

            StringAssert.Contains(venn.Source, "set A[\"Frontend#quot;s\"]", venn.Source);
            Assert.AreEqual(0, venn.Diagnostics.Count, string.Join(" | ", venn.Diagnostics.Select(d => d.Message)));
            Assert.IsTrue(Drawn(venn, "Frontend#quot;s"), "shown as written while the caret is in it");

            PressPast(venn, "React");
            Assert.IsTrue(Drawn(venn, "Frontend\"s"), "and as what it says once the caret has left");
        }));

    [TestMethod]
    public void ALabelInBracketsGivenABracketIsPutInQuotes() => UiThread.Run(() =>
        InADocument((editor, venn) =>
        {
            PressPast(venn, "Server");
            Write(editor, "]");

            StringAssert.Contains(venn.Source, "set Backend[\"Server]\"]", venn.Source);
            Assert.AreEqual(0, venn.Diagnostics.Count, string.Join(" | ", venn.Diagnostics.Select(d => d.Message)));
        }, Bare));

    /// <summary>Names that other lines use: a set in a union and a style, an item in a style.</summary>
    private const string Named =
        "venn-beta\n  set Frontend\n    text A1\n  set Backend\n  union Frontend,Backend[\"APIs\"]\n"
        + "  style Frontend fill:#4e79a7\n  style A1 color:red";

    [TestMethod]
    public void CtrlAddsEachThingPressedToWhatIsChosen_AndTakesItBackOut() => UiThread.Run(() =>
        InADocument((editor, venn) =>
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
        InADocument((editor, venn) =>
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
    private static Piece Words(DocumentBlock venn, string words) =>
        venn.Laid.Root.SelfAndDescendants()
            .First(piece => piece.Words is not null && piece.Sits().Start == venn.Source.IndexOf(words, venn.Start, System.StringComparison.Ordinal));

    private static Point Middle(DocumentBlock venn, string words)
    {
        var bounds = Words(venn, words).Bounds;
        return new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    /// <summary>What is chosen, a stretch at a time.</summary>
    private static string[] Chosen(DocumentBlock venn) =>
        [.. venn.Selection.Select(range => venn.Source.Substring(range.Start, range.Length))];

    /// <summary>Whether a run of words reads <paramref name="text"/>.</summary>
    private static bool Drawn(DocumentBlock venn, string text) =>
        venn.Laid.Root.SelfAndDescendants().Any(piece => piece.Words?.Glyphs.Text == text);

    [TestMethod]
    public void DeletingAWholeLabelLeavesAHoleToWriteANewOneIn() => UiThread.Run(() =>
        InADocument((editor, venn) =>
        {
            PressPast(venn, "Shared");
            for (var letter = 0; letter < "Shared".Length; letter++) Press(editor, Key.Back);

            StringAssert.Contains(venn.Source, "union A,B[\"\"]", $"the label is gone: {venn.Source}");
            Assert.AreEqual(0, venn.Diagnostics.Count);
            Assert.AreEqual(1, venn.Laid.Holes.Count, "a hole stands where it goes");
            Assert.AreEqual(venn.Laid.Holes[0].Sits().Start, venn.Caret);

            Write(editor, "Both");
            StringAssert.Contains(venn.Source, "union A,B[\"Both\"]", venn.Source);
        }));

    [TestMethod]
    public void UndoTakesAnEditBackWithTheDiagramStillDrawn() => UiThread.Run(() =>
        InADocument((editor, venn) =>
        {
            PressPast(venn, "Frontend");
            Write(editor, "s");
            StringAssert.Contains(editor.Markdown, "Frontends", "precondition: the edit landed");

            editor.Undo();
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(editor.Markdown, "set A[\"Frontend\"]", "the edit is taken back");
            Assert.IsNotNull(MarkdownEditorHarness.Block(editor), "and the diagram is still drawn, rather than opened as its source");
        }));

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T hit) return hit;

        for (var at = 0; at < VisualTreeHelper.GetChildrenCount(root); at++)
            if (Find<T>(VisualTreeHelper.GetChild(root, at)) is { } found) return found;

        return null;
    }

    [TestMethod]
    public void WhatAPlaceCannotHoldIsEscaped_SoTheLineStillReads()
    {
        foreach (var (source, typedAfter, typed, becomes) in new[]
                 {
                     ("venn-beta\n  set Frontend", "Frontend", "\\", "venn-beta\n  set \"Frontend\\\""),
                     ("venn-beta\n  set Frontend", "Frontend", " ", "venn-beta\n  set \"Frontend \""),
                     ("venn-beta\n  set Frontend", "Frontend", "\"", "venn-beta\n  set \"Frontend#quot;\""),
                     ("venn-beta\n  set A[\"Alpha\"]", "Alpha", "\"", "venn-beta\n  set A[\"Alpha#quot;\"]"),
                     ("venn-beta\n  set A[ Alpha ]", "Alpha", "]", "venn-beta\n  set A[\"Alpha]\"]"),
                     ("venn-beta\n  set A\n    text \"Vue\"", "Vue", "\"", "venn-beta\n  set A\n    text \"Vue#quot;\""),
                     ("venn-beta\n  set A\n    text A1", "A1", "!", "venn-beta\n  set A\n    text \"A1!\""),
                 })
        {
            var part = MermaidStaged.Read(source).SelfAndDescendants().First(node => node.Kind == Kinds.Words && node.Text == typedAfter);
            var writing = VennEdits.Escaping(part, part.End, typed);

            Assert.IsNotNull(writing, $"{source}: typing {typed}");
            var written = MermaidStaged.Written(source, writing.Value);

            Assert.AreEqual(becomes, written, $"{source}: typing {typed}");
            Assert.AreEqual(typed, MermaidText.Decode(written[(written.IndexOf(typedAfter, StringComparison.Ordinal) + typedAfter.Length)..writing.Value.Caret]),
                            $"{source}: the caret goes after what was typed");
            Assert.IsFalse(MermaidStaged.Read(written).SelfAndDescendants().Any(node => node.Node.Trouble is not null), $"{written} still reads");
        }
    }

    [TestMethod]
    public void WhatAPlaceCanHoldGoesInAsItIs()
    {
        foreach (var (source, typedAfter, typed) in new[]
                 {
                     ("venn-beta\n  set Frontend", "Frontend", "s_2-x"),
                     ("venn-beta\n  set A[\"Alpha\"]", "Alpha", "\\ ]%"),
                     ("venn-beta\n  set A[Alpha]", "Alpha", " beta\\"),
                 })
        {
            var part = MermaidStaged.Read(source).SelfAndDescendants().First(node => node.Kind == Kinds.Words && node.Text == typedAfter);
            Assert.IsNull(VennEdits.Escaping(part, part.End, typed), $"{source}: typing {typed}");
        }
    }

    [TestMethod]
    public void AQuoteTypedIntoANameStillToBeWrittenIsEscapedToo()
    {
        const string source = "venn-beta\n  set A\n    text \"\"";
        var hole = MermaidStaged.Read(source, holes: true).SelfAndDescendants().Single(node => node.Kind == Kinds.Hole);

        var writing = VennEdits.Escaping(hole, hole.Start, "\"")!.Value;
        Assert.AreEqual("venn-beta\n  set A\n    text \"#quot;\"", MermaidStaged.Written(source, writing));
    }
}

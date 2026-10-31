using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.TreeView;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.TreeView;

/// <summary>
/// Working with a tree view through the editor that hosts it, as with a folder tree: labels written where they are drawn — bare for
/// as long as they can be — Enter putting a node in, Tab taking it into the node before it, the right-click and a drag putting nodes
/// where they go, and a tree drawn with a box drawing drawn again round every change.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("treeview-writing")]
public class TreeViewEditingTests
{
    private const string Tree = "src/\n    a.ts\n    b.ts\nREADME.md";

    private const string Drawn = "├── src/\n│   ├── a.ts\n│   └── b.ts\n└── README.md";

    /// <summary>The tree inside a document, fenced as itself, with the editor handing its keys to it.</summary>
    private static void InADocument(Action<MarkdownSurface, DocumentBlock> test, string tree = Tree) =>
        MarkdownEditorHarness.Run("Files:\n\n```treeview\n" + tree + "\n```\n", editor =>
        {
            var block = MarkdownEditorHarness.Block(editor);
            Assert.IsNotNull(block, "the tree view did not render as content");

            test(editor, block!);
        });

    [TestMethod]
    public void TypingInALabelChangesTheLabel_AndASpaceKeepsItBare() => UiThread.Run(() =>
        InADocument((editor, tree) =>
        {
            Assert.IsFalse(tree.IsReadOnly, "a tree view's labels are written in");

            PressPast(tree, "a.ts");
            Write(editor, " ");
            Write(editor, "x");

            StringAssert.Contains(tree.Source, "    a.ts x\n", tree.Source);
            Assert.AreEqual(0, tree.Diagnostics.Count, Trouble(tree));
        }));

    [TestMethod]
    public void WhatWouldReadAsSomethingElseInABareLabelPutsItInQuotes() => UiThread.Run(() =>
        InADocument((editor, tree) =>
        {
            PressPast(tree, "a.ts");
            MarkdownEditorHarness.Type(editor, " ## x");
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(tree.Source, "    \"a.ts ## x\"\n", tree.Source);
            Assert.AreEqual(0, tree.Diagnostics.Count, Trouble(tree));
        }));

    [TestMethod]
    public void EnterPutsANodeInAfterIt_AndWhatIsTypedNamesItBare() => UiThread.Run(() =>
        InADocument((editor, tree) =>
        {
            PressPast(tree, "a.ts");
            Press(editor, Key.Enter);
            MarkdownEditorHarness.Type(editor, "c.ts");
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(tree.Source, "    a.ts\n    c.ts\n    b.ts", tree.Source);
            Assert.AreEqual(0, tree.Diagnostics.Count, Trouble(tree));
        }));

    [TestMethod]
    public void InADrawnTreeEnterDrawsTheTreeAgainRoundTheNewNode() => UiThread.Run(() =>
        InADocument((editor, tree) =>
        {
            PressPast(tree, "b.ts");
            Press(editor, Key.Enter);
            MarkdownEditorHarness.Type(editor, "c.ts");
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(tree.Source, "├── src/\n│   ├── a.ts\n│   ├── b.ts\n│   └── c.ts\n└── README.md", tree.Source);
        }, Drawn));

    [TestMethod]
    public void TabTakesANodeIntoTheOneBeforeIt() => UiThread.Run(() =>
        InADocument((editor, tree) =>
        {
            PressPast(tree, "README.md");
            Press(editor, Key.Tab);

            StringAssert.Contains(tree.Source, "src/\n    a.ts\n    b.ts\n    README.md", tree.Source);
            Assert.IsTrue(tree.HasCaret, "the caret stays in the label it was in");
        }));

    [TestMethod]
    public void BackspaceInALabelWithNothingInItTakesTheNodeOut() => UiThread.Run(() =>
        InADocument((editor, tree) =>
        {
            PressPast(tree, "a.ts");
            Press(editor, Key.Enter);
            Press(editor, Key.Back);

            StringAssert.Contains(tree.Source, "```treeview\n" + Tree + "\n```", tree.Source);
        }));

    [TestMethod]
    public void BackspaceAtTheStartOfABareLabelTakesNothingOfWhatSaysWhereItStands() => UiThread.Run(() =>
        InADocument((editor, tree) =>
        {
            var written = tree.Source;
            tree.TakeCaret(tree.Source.IndexOf("a.ts", tree.Start, StringComparison.Ordinal));
            Press(editor, Key.Back);

            Assert.AreEqual(written, tree.Source);
        }));

    [TestMethod]
    public void ARightClickOnANodeOffersWhatCanBeDoneToIt_AndWhatItIs() => UiThread.Run(() =>
        InADocument((editor, tree) =>
        {
            var ribbon = Ribbon(editor, Row(tree, "a.ts"));
            var ids = ribbon.Controls.Select(control => control.Id).ToArray();

            CollectionAssert.IsSubsetOf(new[] { "treeview.kind", "treeview.insert.before", "treeview.insert.after", "treeview.insert.inside", "treeview.level.in",
                                                "treeview.level.out", "treeview.describe", "treeview.delete" }, ids);
            Assert.AreEqual("file", ribbon.Controls.OfType<MiniRibbonChoice>().Single().Current);
        }));

    [TestMethod]
    public void ChoosingFolderMakesANodeAFolder() => UiThread.Run(() =>
        InADocument((editor, tree) =>
        {
            Click(Ribbon(editor, Row(tree, "README.md")), "treeview.kind.folder");

            StringAssert.Contains(tree.Source, "\nREADME.md/\n", tree.Source);
        }));

    [TestMethod]
    public void AddingADescriptionPutsTheCaretWhereItIsWritten() => UiThread.Run(() =>
        InADocument((editor, tree) =>
        {
            Click(Ribbon(editor, Row(tree, "a.ts")), "treeview.describe");
            MarkdownEditorHarness.Type(editor, "the a");
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(tree.Source, "    a.ts ## the a\n", tree.Source);
        }));

    [TestMethod]
    public void ANodeDraggedOntoTheMiddleOfAnotherGoesInsideIt_AndOntoItsTopInFrontOfIt() => UiThread.Run(() =>
        InADocument((editor, tree) =>
        {
            Drag(tree, Row(tree, "README.md"), Row(tree, "b.ts"), 0.5);
            StringAssert.Contains(tree.Source, "    b.ts\n        README.md", tree.Source);
        }));

    [TestMethod]
    public void ANodeDraggedOntoTheTopOfAnotherGoesInFrontOfIt() => UiThread.Run(() =>
        InADocument((editor, tree) =>
        {
            Drag(tree, Row(tree, "b.ts"), Row(tree, "a.ts"), 0.1);
            StringAssert.Contains(tree.Source, "src/\n    b.ts\n    a.ts\nREADME.md", tree.Source);
        }));

    [TestMethod]
    public void DeleteTakesAChosenFolderOutWithEverythingInIt() => UiThread.Run(() =>
        InADocument((editor, tree) =>
        {
            tree.BeginPointerSelect(Clear(tree, Row(tree, "src"), 0.5));
            tree.EndPointerSelect();
            Assert.IsTrue(tree.SelectionLength > 0, "a press on a row chooses its node");

            Press(editor, Key.Delete);

            StringAssert.Contains(tree.Source, "```treeview\nREADME.md\n```", tree.Source);
        }));

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Presses just inside the end of the label <paramref name="words"/>.</summary>
    private static void PressPast(DocumentBlock tree, string words)
    {
        var at = tree.Source.IndexOf(words, tree.Start, StringComparison.Ordinal);
        var piece = tree.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == TreeViewPiece.Label && piece.Words is not null && piece.Sits().Start == at);

        tree.BeginPointerSelect(new Point(piece.Bounds.Right - 1, piece.Bounds.Y + (piece.Bounds.Height / 2)));
        tree.EndPointerSelect();
        Assert.IsTrue(tree.HasCaret, $"a press on {words} takes the caret");
    }

    /// <summary>Where the row of the node labelled <paramref name="label"/> is drawn.</summary>
    private static Rect Row(DocumentBlock tree, string label) =>
        tree.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == TreeViewPiece.Row
                                                           && piece.SelfAndDescendants().Any(inner => inner.Kind == TreeViewPiece.Label && inner.Words?.Glyphs.Text == label))
            .Bounds.ToWpf();

    /// <summary>
    /// A point on a row, clear of its words and its ends — halfway between where its words end and where the row does —
    /// <paramref name="down"/> of the way down it.
    /// </summary>
    private static Point Clear(DocumentBlock tree, Rect row, double down)
    {
        var words = tree.Laid.Root.SelfAndDescendants().Where(piece => piece.Words is not null && piece.Bounds.ToWpf() is var at && at.Top >= row.Top - 0.5 && at.Bottom <= row.Bottom + 0.5);
        var end = words.Max(piece => piece.Bounds.Right);

        return new(end + ((row.Right - end) / 2), row.Top + (row.Height * down));
    }

    private static void Drag(DocumentBlock tree, Rect from, Rect to, double down)
    {
        var start = Clear(tree, from, 0.5);
        var end = Clear(tree, to, down);

        tree.BeginPointerSelect(start);
        tree.ExtendPointerSelect(new Point(start.X, (start.Y + end.Y) / 2));
        tree.ExtendPointerSelect(end);
        tree.EndPointerSelect();
        MarkdownEditorHarness.Pump();
    }

    private static void Write(MarkdownSurface editor, string text)
    {
        MarkdownEditorHarness.RaiseTextInput(editor, text);
        MarkdownEditorHarness.Pump();
    }

    private static void Press(MarkdownSurface editor, Key key)
    {
        MarkdownEditorHarness.RaiseKey(editor, key);
        MarkdownEditorHarness.Pump();
    }

    private static string Trouble(DocumentBlock tree) => string.Join(" | ", tree.Diagnostics.Select(diagnostic => diagnostic.Message));

    private static DiagramRibbon Ribbon(MarkdownSurface editor, Rect over)
    {
        var ribbon = editor.Shown.BuildRibbon(new Point(over.Right - 3, over.Top + (over.Height / 2))) as DiagramRibbon;
        Assert.IsNotNull(ribbon, "a right-click opens the ribbon");
        return ribbon!;
    }

    private static void Click(DiagramRibbon ribbon, string named)
    {
        var button = Logical(ribbon).OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "Diagram_Ribbon_" + named);
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
}

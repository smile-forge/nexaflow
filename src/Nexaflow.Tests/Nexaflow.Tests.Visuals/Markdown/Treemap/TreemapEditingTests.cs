using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Treemap;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.Treemap;

/// <summary>
/// Working with a treemap through the editor that hosts it, as an outline: its labels and values written where they are drawn, Enter
/// putting a leaf in, Tab taking one into the section before it, the right-click and a drag putting items where they go.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("treemap-writing")]
public class TreemapEditingTests
{
    private const string Map = "\"S\"\n    \"A\": 10\n    \"B\": 20\n\"T\"\n    \"C\": 30";

    /// <summary>The treemap inside a document, fenced as itself, with the editor handing its keys to it.</summary>
    private static void InADocument(Action<MarkdownSurface, DocumentBlock> test, string treemap = Map) =>
        MarkdownEditorHarness.Run("Spending:\n\n```treemap\n" + treemap + "\n```\n", editor =>
        {
            var block = MarkdownEditorHarness.Block(editor);
            Assert.IsNotNull(block, "the treemap did not render as content");

            test(editor, block!);
        });

    [TestMethod]
    public void TypingInALabelChangesTheLabel() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            Assert.IsFalse(treemap.IsReadOnly, "a treemap's labels are written in");

            PressPast(treemap, "A");
            Write(editor, "X");

            StringAssert.Contains(treemap.Source, "\"AX\": 10", treemap.Source);
            Assert.AreEqual(0, treemap.Diagnostics.Count, Trouble(treemap));
        }));

    [TestMethod]
    public void AQuoteTypedIntoALabelPutsItInTheOtherQuote() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            PressPast(treemap, "A");
            Write(editor, "\"");

            StringAssert.Contains(treemap.Source, "'A\"': 10", treemap.Source);
            Assert.AreEqual(0, treemap.Diagnostics.Count, Trouble(treemap));
        }));

    [TestMethod]
    public void AValueIsTypedIntoAsWritten_AndTakesOnlyANumbersCharacters() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            PressPast(treemap, "10", TreemapPiece.Value);
            Write(editor, "x");
            Write(editor, "5");

            StringAssert.Contains(treemap.Source, "\"A\": 105", treemap.Source);
            Assert.AreEqual(0, treemap.Diagnostics.Count, Trouble(treemap));
        }));

    [TestMethod]
    public void EnterPutsALeafInAfterTheItem_AndWhatIsTypedNamesIt() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            PressPast(treemap, "A");
            Press(editor, Key.Enter);
            Write(editor, "N");

            StringAssert.Contains(treemap.Source, "    \"A\": 10\n    \"N\": 10\n    \"B\": 20", treemap.Source);
            Assert.AreEqual(0, treemap.Diagnostics.Count, Trouble(treemap));
        }));

    [TestMethod]
    public void TabTakesAnItemIntoTheSectionBeforeIt() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            PressPast(treemap, "T");
            Press(editor, Key.Tab);

            StringAssert.Contains(treemap.Source, "    \"B\": 20\n    \"T\"\n        \"C\": 30", treemap.Source);
            Assert.IsTrue(treemap.HasCaret, "the caret stays in the label it was in");
        }));

    [TestMethod]
    public void BackspaceInALabelWithNothingInItTakesTheItemOut() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            PressPast(treemap, "A");
            Press(editor, Key.Enter);
            Press(editor, Key.Back);

            StringAssert.Contains(treemap.Source, "```treemap\n" + Map + "\n```", treemap.Source);
        }));

    [TestMethod]
    public void ARightClickOnAnItemOffersWhatCanBeDoneToIt_EachAsItsIcon() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            var ribbon = Ribbon(editor, Tile(treemap, "A"));

            CollectionAssert.IsSubsetOf(new[] { "treemap.insert.before", "treemap.insert.after", "treemap.insert.inside", "treemap.level.in", "treemap.level.out", "treemap.delete" },
                                        ribbon.Controls.Select(control => control.Id).ToArray());
            Assert.IsTrue(ribbon.Controls.OfType<MiniRibbonButton>().Where(button => button.Id.StartsWith("treemap.", StringComparison.Ordinal)).All(button => button.Shape is not null));
            Assert.IsTrue(ribbon.Controls.OfType<MiniRibbonButton>().Single(button => button.Id == "treemap.delete").Warns, "taking it out is drawn as a warning");
        }));

    [TestMethod]
    public void PuttingALeafInsideALeafMakesItASection_HandingItItsValue() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            Click(Ribbon(editor, Tile(treemap, "A")), "treemap.insert.inside");
            Write(editor, "A1");

            StringAssert.Contains(treemap.Source, "    \"A\"\n        \"A1\": 10\n    \"B\": 20", treemap.Source);
            Assert.AreEqual(0, treemap.Diagnostics.Count, Trouble(treemap));
        }));

    [TestMethod]
    public void MovingOutALevelPutsAnItemAfterTheSectionItWasIn() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            Click(Ribbon(editor, Tile(treemap, "A")), "treemap.level.out");

            StringAssert.Contains(treemap.Source, "```treemap\n\"S\"\n    \"B\": 20\n\"A\": 10\n\"T\"", treemap.Source);
        }));

    [TestMethod]
    public void DeleteTakesAChosenItemOutWithEverythingInIt() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            Choose(treemap, "S");
            Press(editor, Key.Delete);

            StringAssert.Contains(treemap.Source, "```treemap\n\"T\"\n    \"C\": 30\n```", treemap.Source);
        }));

    [TestMethod]
    public void AnItemDraggedByItsTileOntoASectionGoesIntoIt() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            Assert.AreEqual(0, treemap.SelectionLength, "nothing is picked out first");

            var from = Inside(Tile(treemap, "A"));
            var to = Inside(Tile(treemap, "C"));

            treemap.BeginPointerSelect(from);
            treemap.ExtendPointerSelect(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
            treemap.ExtendPointerSelect(to);
            treemap.EndPointerSelect();
            MarkdownEditorHarness.Pump();

            StringAssert.Contains(treemap.Source, "```treemap\n\"S\"\n    \"B\": 20\n\"T\"\n    \"C\": 30\n    \"A\": 10\n```", treemap.Source);
        }));

    [TestMethod]
    public void OverAWholeItemPickedOutOnlyACopyIsOffered() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            Choose(treemap, "A");

            var verbs = Ribbon(editor, Tile(treemap, "A")).Controls.Select(control => control.Id).ToArray();

            CollectionAssert.Contains(verbs, LayoutVerbs.Copy);
            CollectionAssert.DoesNotContain(verbs, LayoutVerbs.Cut);
        }));

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Presses just inside the end of <paramref name="words"/> where the treemap draws them.</summary>
    private static void PressPast(DocumentBlock treemap, string words, string kind = TreemapPiece.Label)
    {
        var at = treemap.Source.IndexOf(kind == TreemapPiece.Label ? $"\"{words}\"" : words, treemap.Start, StringComparison.Ordinal) + (kind == TreemapPiece.Label ? 1 : 0);
        var piece = treemap.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == kind && piece.Words is not null && piece.Sits().Start == at);

        treemap.BeginPointerSelect(new Point(piece.Bounds.Right - 1, piece.Bounds.Y + (piece.Bounds.Height / 2)));
        treemap.EndPointerSelect();
        Assert.IsTrue(treemap.HasCaret, $"a press on {words} takes the caret");
    }

    /// <summary>Where the tile of the item labelled <paramref name="label"/> is drawn.</summary>
    private static Rect Tile(DocumentBlock treemap, string label)
    {
        var item = treemap.Laid.Root.SelfAndDescendants().First(piece => piece.Kind is TreemapPiece.Leaf or TreemapPiece.Section && piece.Part is { } part
                                                                         && treemap.Source.Substring(part.Start, part.Length).TrimStart().StartsWith($"\"{label}\"", StringComparison.Ordinal));

        return item.Children.First(piece => piece.Kind == MermaidPiece.Shape).Bounds.ToWpf();
    }

    /// <summary>A point inside a tile, clear of its words and its edges, where a press means the tile.</summary>
    private static Point Inside(Rect tile) => new(tile.X + 4, tile.Bottom - 4);

    private static void Choose(DocumentBlock treemap, string label)
    {
        treemap.BeginPointerSelect(Inside(Tile(treemap, label)));
        treemap.EndPointerSelect();

        Assert.IsTrue(treemap.SelectionLength > 0, $"pressing {label}'s tile chooses it");
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

    private static string Trouble(DocumentBlock treemap) => string.Join(" | ", treemap.Diagnostics.Select(diagnostic => diagnostic.Message));

    private static DiagramRibbon Ribbon(MarkdownSurface editor, Rect over)
    {
        var ribbon = editor.Shown.BuildRibbon(new Point(over.X + 4, over.Bottom - 4)) as DiagramRibbon;
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

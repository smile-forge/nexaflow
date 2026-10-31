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
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.Treemap;

/// <summary>
/// Working with a treemap through the editor that hosts it, as boxes: its labels and values written where they are drawn, Enter
/// splitting a box and Backspace taking the split back, the right-click splitting, merging and grouping, and a drag putting a box into
/// a group with the treemap standing still while it is carried.
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

    /// <summary>The treemap as it is written in the document, between its fences.</summary>
    private static string Written(DocumentBlock treemap) => treemap.Source[treemap.Start..treemap.End].TrimEnd('\n');

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
    public void DownFromALabelGoesToItsValue_AndUpGoesBack() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            PressPast(treemap, "B");
            Press(editor, Key.Down);

            var value = treemap.Source.IndexOf("20", StringComparison.Ordinal);
            Assert.IsTrue(treemap.Caret >= value && treemap.Caret <= value + 2, $"the caret in B's value, not at {treemap.Caret}");

            Press(editor, Key.Up);

            var label = treemap.Source.IndexOf("\"B\"", StringComparison.Ordinal) + 1;
            Assert.IsTrue(treemap.Caret >= label && treemap.Caret <= label + 1, $"the caret back in B's label, not at {treemap.Caret}");
        }));

    [TestMethod]
    public void AValueDrawnAsItsFormatSaysIsShownAsWrittenOnceAKeyBringsTheCaretIntoIt() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            Assert.AreEqual("$2,000", Value(treemap, "2000"), "drawn as its format says");

            PressPast(treemap, "B");
            Press(editor, Key.Down);

            Assert.AreEqual("2000", Value(treemap, "2000"), "shown as written, so what is typed goes into what is seen");

            Press(editor, Key.Up);
            Assert.AreEqual("$2,000", Value(treemap, "2000"), "and drawn as its format says again once the caret has gone");
        }, "---\nconfig:\n  treemap:\n    valueFormat: '$0,0'\n---\n\"S\"\n    \"A\": 1000\n    \"B\": 2000"));

    [TestMethod]
    public void EnterSplitsABoxInTwo_AndWhatIsTypedNamesTheNewHalf() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            PressPast(treemap, "A");
            Press(editor, Key.Enter);
            Write(editor, "N");

            StringAssert.Contains(treemap.Source, "    \"A\": 5\n    \"N\": 5\n    \"B\": 20", treemap.Source);
            Assert.AreEqual(0, treemap.Diagnostics.Count, Trouble(treemap));
        }));

    [TestMethod]
    public void BackspaceInTheNewHalfTakesTheSplitBack() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            PressPast(treemap, "A");
            Press(editor, Key.Enter);
            Press(editor, Key.Back);

            Assert.AreEqual(Map, Written(treemap), "the box it was, worth what it was");
        }));

    [TestMethod]
    public void ABoxGroupedOnItsOwnAndTakenBackIsTheBoxItWas() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            Click(Ribbon(editor, Tile(treemap, "C")), "treemap.box.group");
            StringAssert.Contains(treemap.Source, "\"T\"\n    \"C\"\n        \"\": 30", treemap.Source);

            Press(editor, Key.Back);
            Assert.AreEqual(Map, Written(treemap), "the group given its value back");
        }));

    [TestMethod]
    public void TabAndPageDownInABoxAreTaken_NotWrittenAndNotMovingLines() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            PressPast(treemap, "B");
            Press(editor, Key.Tab);
            Press(editor, Key.PageUp);

            Assert.AreEqual(Map, Written(treemap));
            Assert.IsTrue(treemap.HasCaret, "the caret stays in the label it was in");
        }));

    [TestMethod]
    public void ARightClickOffersWhatCanBeDoneToABox_AndToAGroup_EachAsItsIcon() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            var box = Ribbon(editor, Tile(treemap, "A"));
            CollectionAssert.IsSubsetOf(new[] { "treemap.box.split", "treemap.box.merge", "treemap.box.group", "treemap.delete" }, Ids(box));

            var group = Ribbon(editor, Band(Tile(treemap, "S")));
            CollectionAssert.IsSubsetOf(new[] { "treemap.group.add", "treemap.group.ungroup", "treemap.delete" }, Ids(group));
            CollectionAssert.DoesNotContain(Ids(group), "treemap.box.split", "a group is not split");

            foreach (var ribbon in new[] { box, group })
            {
                Assert.IsTrue(ribbon.Controls.OfType<MiniRibbonButton>().Where(button => button.Id.StartsWith("treemap.", StringComparison.Ordinal)).All(button => button.Shape is not null));
                Assert.IsTrue(ribbon.Controls.OfType<MiniRibbonButton>().Single(button => button.Id == "treemap.delete").Warns, "taking it out is drawn as a warning");
            }
        }));

    [TestMethod]
    public void SplitFromTheRightClickPutsTheOtherHalfBesideIt() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            Click(Ribbon(editor, Tile(treemap, "C")), "treemap.box.split");
            Write(editor, "D");

            StringAssert.Contains(treemap.Source, "\"T\"\n    \"C\": 15\n    \"D\": 15", treemap.Source);
        }));

    [TestMethod]
    public void MergeIsGreyedOutUntilBoxesArePickedOut_ThenMergesThemIntoTheOneRightClicked() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            var alone = Ribbon(editor, Tile(treemap, "A"));
            Assert.IsFalse(Pressable(alone, "treemap.box.merge"), "one box is nothing to merge");
            Assert.AreEqual(Str.Get("Text.Treemap.Box.MergeHow"), Offered(alone, "treemap.box.merge").Name, "and it says how to pick boxes out");

            Pick(treemap, "A");
            Pick(treemap, "B");

            var picked = Ribbon(editor, Tile(treemap, "B"));
            Assert.IsTrue(Pressable(picked, "treemap.box.merge"));

            Click(picked, "treemap.box.merge");
            Assert.AreEqual("\"S\"\n    \"B\": 30\n\"T\"\n    \"C\": 30", Written(treemap));
        }));

    [TestMethod]
    public void BoxesPickedOutGoInANewGroup_AndWhatIsTypedNamesIt() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            Pick(treemap, "A");
            Pick(treemap, "B");
            Click(Ribbon(editor, Tile(treemap, "A")), "treemap.box.group");
            Write(editor, "G");

            Assert.AreEqual("\"S\"\n    \"G\"\n        \"A\": 10\n        \"B\": 20\n\"T\"\n    \"C\": 30", Written(treemap));
        }));

    [TestMethod]
    public void AGroupsBandOffersABoxInIt_AndToLetItsBoxesOut() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            Click(Ribbon(editor, Band(Tile(treemap, "S"))), "treemap.group.add");
            Write(editor, "N");
            StringAssert.Contains(treemap.Source, "    \"B\": 20\n    \"N\": 10\n\"T\"", treemap.Source);

            Click(Ribbon(editor, Band(Tile(treemap, "T"))), "treemap.group.ungroup");
            StringAssert.Contains(treemap.Source, "    \"N\": 10\n\"C\": 30\n```", treemap.Source);
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
    public void ABoxDraggedOntoAnotherGroupGoesIntoIt_WithTheTreemapStandingStillAndThatGroupOutlined() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            Assert.AreEqual(0, treemap.SelectionLength, "nothing is picked out first");

            var from = Inside(Tile(treemap, "A"));
            var to = Inside(Tile(treemap, "C"));
            var group = Section(treemap, "T");

            treemap.BeginPointerSelect(from);
            treemap.ExtendPointerSelect(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
            treemap.ExtendPointerSelect(to);

            Assert.IsFalse(treemap.ShowsCarriedResult, "nothing is laid out again while it is carried, so no tile moves under the pointer");
            Assert.IsNotNull(treemap.Marking, "the group it would go into is outlined");
            Assert.AreEqual(group, treemap.Marking!.Value.ToWpf(), "T, which C is in");

            treemap.EndPointerSelect();
            MarkdownEditorHarness.Pump();

            Assert.AreEqual("\"S\"\n    \"B\": 20\n\"T\"\n    \"C\": 30\n    \"A\": 10", Written(treemap));
        }));

    [TestMethod]
    public void ABoxDraggedOntoItsOwnGroupGoesNowhere_AndNothingIsOutlined() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            var from = Inside(Tile(treemap, "A"));
            var to = Inside(Tile(treemap, "B"));

            treemap.BeginPointerSelect(from);
            treemap.ExtendPointerSelect(to);

            Assert.IsNull(treemap.Marking, "where it already is is nowhere to go");

            treemap.EndPointerSelect();
            MarkdownEditorHarness.Pump();

            Assert.AreEqual(Map, Written(treemap));
        }));

    [TestMethod]
    public void OverAWholeItemPickedOutOnlyACopyIsOffered() => UiThread.Run(() =>
        InADocument((editor, treemap) =>
        {
            Choose(treemap, "A");

            var verbs = Ids(Ribbon(editor, Tile(treemap, "A")));

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

    /// <summary>What the value written as <paramref name="number"/> is drawn as.</summary>
    private static string Value(DocumentBlock treemap, string number)
    {
        var at = treemap.Source.IndexOf(number, treemap.Start, StringComparison.Ordinal);
        return treemap.Laid.Root.SelfAndDescendants().First(piece => piece.Kind == TreemapPiece.Value && piece.Words is not null && piece.Sits().Start == at).Words!.Glyphs.Text;
    }

    /// <summary>The item labelled <paramref name="label"/>, as it is drawn — its leaf or its section.</summary>
    private static LayoutPiece Drawn(DocumentBlock treemap, string label) =>
        treemap.Laid.Root.SelfAndDescendants().First(piece => piece.Kind is TreemapPiece.Leaf or TreemapPiece.Section && piece.Part is { } part
                                                          && treemap.Source.Substring(part.Start, part.Length).TrimStart().StartsWith($"\"{label}\"", StringComparison.Ordinal));

    /// <summary>Where the tile of the item labelled <paramref name="label"/> is drawn.</summary>
    private static Rect Tile(DocumentBlock treemap, string label) => Drawn(treemap, label).Children.First(piece => piece.Kind == MermaidPiece.Shape).Bounds.ToWpf();

    /// <summary>Where the whole of the section labelled <paramref name="label"/> stands.</summary>
    private static Rect Section(DocumentBlock treemap, string label) => Drawn(treemap, label).Bounds.ToWpf();

    /// <summary>A point inside a tile, clear of its words and its edges, where a press means the tile.</summary>
    private static Point Inside(Rect tile) => new(tile.X + 4, tile.Bottom - 4);

    /// <summary>A point on a section's band, the strip across its top that its label is written in.</summary>
    private static Point Band(Rect tile) => new(tile.X + (tile.Width / 2), tile.Y + 3);

    private static void Choose(DocumentBlock treemap, string label)
    {
        treemap.BeginPointerSelect(Inside(Tile(treemap, label)));
        treemap.EndPointerSelect();

        Assert.IsTrue(treemap.SelectionLength > 0, $"pressing {label}'s tile chooses it");
    }

    /// <summary>Ctrl+click on a tile: the item added to what is picked out.</summary>
    private static void Pick(DocumentBlock treemap, string label)
    {
        treemap.BeginPointerSelect(Inside(Tile(treemap, label)), ModifierKeys.Control);
        treemap.EndPointerSelect();
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

    private static string Trouble(DocumentBlock treemap) => string.Join(" | ", treemap.Diagnostics.Select(diagnostic => diagnostic.Message));

    private static DiagramRibbon Ribbon(MarkdownSurface editor, Rect over) => Ribbon(editor, new Point(over.X + 4, over.Bottom - 4));

    private static DiagramRibbon Ribbon(MarkdownSurface editor, Point at)
    {
        var ribbon = editor.Shown.BuildRibbon(at) as DiagramRibbon;
        Assert.IsNotNull(ribbon, "a right-click opens the ribbon");
        return ribbon!;
    }

    private static string[] Ids(DiagramRibbon ribbon) => [.. ribbon.Controls.Select(control => control.Id)];

    private static MiniRibbonButton Offered(DiagramRibbon ribbon, string id) => ribbon.Controls.OfType<MiniRibbonButton>().Single(button => button.Id == id);

    private static void Click(DiagramRibbon ribbon, string named)
    {
        var button = Logical(ribbon).OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "Diagram_Ribbon_" + named);
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        MarkdownEditorHarness.Pump();
    }

    /// <summary>Whether the button <paramref name="named"/> is drawn so it can be pressed.</summary>
    private static bool Pressable(DiagramRibbon ribbon, string named) =>
        Logical(ribbon).OfType<Button>().Single(button => System.Windows.Automation.AutomationProperties.GetAutomationId(button) == "Diagram_Ribbon_" + named).IsEnabled;

    private static IEnumerable<DependencyObject> Logical(DependencyObject from)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(from).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var further in Logical(child)) yield return further;
        }
    }
}

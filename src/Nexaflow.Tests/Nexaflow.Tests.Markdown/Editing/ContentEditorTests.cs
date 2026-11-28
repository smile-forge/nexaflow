using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Markdown.Editing;

/// <summary>
/// A view of a document a page holds — its caret, what is picked out, which lines are on its page — and the places it and anything else
/// hold in a document, carried through every change: all of it with nothing of a control in it.
/// </summary>
[TestClass]
[CoversNode("content-editor")]
public class ContentEditorTests
{
    // ── One rule for carrying a place ───────────────────────────────────────

    [TestMethod]
    public void APlace_IsPushedAlongByWhatIsWrittenInFrontOfIt_AndNotByWhatIsWrittenAfter()
    {
        var change = ContentChange.Write(2, 0, "xyz");

        Assert.AreEqual(8, change.Carried(5));
        Assert.AreEqual(1, change.Carried(1));
    }

    [TestMethod]
    public void WhatIsWrittenExactlyAtAPlace_GoesInFrontOfItOnlyWhereItLeansAfter()
    {
        var change = ContentChange.Write(4, 0, "ab");

        Assert.AreEqual(4, change.Carried(4), "a place leaning before stays in front of what was written there");
        Assert.AreEqual(6, change.Carried(4, leansAfter: true), "a place leaning after ends up after it");
    }

    [TestMethod]
    public void APlaceInsideAStretchWrittenOver_GoesToTheStartOfWhatTookItsPlace_OrItsEnd()
    {
        var change = ContentChange.Write(2, 6, "XY");

        Assert.AreEqual(2, change.Carried(5));
        Assert.AreEqual(4, change.Carried(5, leansAfter: true));
        Assert.AreEqual(6, change.Carried(10), "past the stretch it moves back by what was taken out");
    }

    // ── Anchors ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void AnAnchor_StaysWithTheCharactersRoundIt()
    {
        var document = new EditedDocument("one two three");
        using var two = document.Anchor(4);

        document.Write(ContentChange.Write(0, 0, "zero "));
        Assert.AreEqual("zero one ".Length, two.Offset);

        document.Write(ContentChange.Write(0, 5, string.Empty));
        Assert.AreEqual(4, two.Offset);
    }

    [TestMethod]
    public void AnAnchor_IsCarriedThroughATakingBackAsThroughAWrite()
    {
        var document = new EditedDocument("abcdef");
        using var anchor = document.Anchor(4);

        document.Write(ContentChange.Write(1, 0, "XX"));
        Assert.AreEqual(6, anchor.Offset);

        document.Undo(new EditState(document.Current, 0));
        Assert.AreEqual(4, anchor.Offset, "taking the write back took the anchor back with it");
    }

    [TestMethod]
    public void ManyAnchors_StayInOrderAndEachWhereItBelongs_ThroughWritesAcrossThem()
    {
        var text = string.Concat(Enumerable.Repeat("0123456789", 50));
        var document = new EditedDocument(text);
        var anchors = Enumerable.Range(0, 500).Select(at => document.Anchor(at, leansAfter: at % 2 == 1)).ToList();
        var expected = Enumerable.Range(0, 500).ToList();

        void Write(int start, int length, string written)
        {
            var change = ContentChange.Write(start, length, written);
            for (var index = 0; index < expected.Count; index++) expected[index] = change.Carried(expected[index], anchors[index].LeansAfter);
            document.Write(change);
        }

        Write(10, 0, "abc");
        Write(100, 40, "x");
        Write(0, 3, string.Empty);
        Write(450, 0, new string('y', 20));
        Write(200, 0, string.Empty.PadLeft(7));

        CollectionAssert.AreEqual(expected, anchors.Select(anchor => anchor.Offset).ToList());

        foreach (var anchor in anchors) anchor.Dispose();
    }

    [TestMethod]
    public void AnAnchorLetGo_IsNoLongerCarried_AndSaysWhereItLastStood()
    {
        var document = new EditedDocument("abcdef");
        var anchor = document.Anchor(3);

        anchor.Dispose();
        document.Write(ContentChange.Write(0, 0, "XX"));

        Assert.IsTrue(anchor.IsReleased);
        Assert.AreEqual(3, anchor.Offset);
    }

    [TestMethod]
    public void AnythingLoadedInItsPlace_KeepsEveryAnchorInsideIt()
    {
        var document = new EditedDocument("a long document");
        using var anchor = document.Anchor(12);

        document.Load("short");

        Assert.AreEqual(5, anchor.Offset);
    }

    // ── Words ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void AStepByWords_GoesToTheNextStartOfAWord_OverTheSpaceAfterOne()
    {
        var document = ContentDocument.Of("int count = total;");

        Assert.AreEqual(4, ContentWordEdges.Next(document, 0, forward: true));
        Assert.AreEqual(10, ContentWordEdges.Next(document, 4, forward: true));
        Assert.AreEqual(12, ContentWordEdges.Next(document, 10, forward: true), "punctuation is a run of its own");
        Assert.AreEqual(12, ContentWordEdges.Next(document, 17, forward: false), "back over the word to its start");
        Assert.AreEqual(4, ContentWordEdges.Next(document, 9, forward: false));
    }

    [TestMethod]
    public void AStepByWords_StopsAtALinesEnd_AndThenCrossesTheLineBreak()
    {
        var document = ContentDocument.Of("one two\r\nthree");

        Assert.AreEqual(7, ContentWordEdges.Next(document, 4, forward: true));
        Assert.AreEqual(9, ContentWordEdges.Next(document, 7, forward: true), "a \\r\\n is crossed whole");
        Assert.AreEqual(7, ContentWordEdges.Next(document, 9, forward: false));
    }

    // ── A view of the document ──────────────────────────────────────────────

    [TestMethod]
    public void WithNothingShowingIt_WhatAHostAsksIsMadeHere()
    {
        using var editor = new ContentEditor(new EditedDocument("Hello world"));
        var moved = 0;
        var picked = 0;
        editor.CaretMoved += (_, _) => moved++;
        editor.SelectionChanged += (_, _) => picked++;

        editor.Select(6, 5);

        Assert.AreEqual("world", editor.SelectedText());
        Assert.AreEqual(11, editor.Caret);
        Assert.AreEqual((1, 1), (moved, picked));
    }

    [TestMethod]
    public void WritingOverWhatIsPickedOut_IsOneStepThatCanBeTakenBack()
    {
        var document = new EditedDocument("Hello world");
        using var editor = new ContentEditor(document);

        editor.Select(6, 5);
        editor.ReplaceSelection("there");

        Assert.AreEqual("Hello there", document.ToString());
        Assert.AreEqual(11, editor.Caret);
        Assert.IsTrue(document.CanUndo);

        document.Undo(editor.State);
        Assert.AreEqual("Hello world", document.ToString());
    }

    [TestMethod]
    public void APageWritingInTheDocument_CarriesTheCaretAndWhatIsPickedOutThroughIt()
    {
        var document = new EditedDocument("abc def ghi");
        using var editor = new ContentEditor(document);
        editor.Select(4, 3);

        document.Write(ContentChange.Write(0, 0, ">> "));

        Assert.AreEqual("def", editor.SelectedText());
        Assert.AreEqual(10, editor.Caret);
    }

    [TestMethod]
    public void StretchingWhatIsPickedOut_RunsFromWhereItWasBegun()
    {
        using var editor = new ContentEditor(new EditedDocument("0123456789"));
        editor.MoveCaretTo(5);

        editor.MoveCaretTo(8, extend: true);
        Assert.AreEqual((5, 3), (editor.Selection.Single().Start, editor.Selection.Single().Length));

        editor.MoveCaretTo(2, extend: true);
        Assert.AreEqual((2, 3), (editor.Selection.Single().Start, editor.Selection.Single().Length), "back past where it was begun, it turns round there");
        Assert.AreEqual(2, editor.Caret);
    }

    [TestMethod]
    public void TakingOutAWord_TakesTheWordBeforeOrAfterTheCaret()
    {
        var document = new EditedDocument("alpha beta gamma");
        using var editor = new ContentEditor(document);
        editor.MoveCaretTo(11);

        editor.DeleteWord(forward: false);
        Assert.AreEqual("alpha gamma", document.ToString());

        editor.DeleteWord(forward: true);
        Assert.AreEqual("alpha ", document.ToString());
    }

    [TestMethod]
    public void PickingOutLines_TakesTheirLineBreaksWithThem()
    {
        using var editor = new ContentEditor(new EditedDocument("one\ntwo\nthree\nfour"));

        editor.SelectLines(1, 2);

        Assert.AreEqual("two\nthree\n", editor.SelectedText());
    }

    [TestMethod]
    public void ALineAskedForBeforeAnythingShowsIt_IsShownOnceSomethingDoes()
    {
        using var editor = new ContentEditor(new EditedDocument("a\nb\nc"));
        editor.ScrollToLine(2, ShownPlace.Centre);

        var view = new View();
        editor.Attach(view);

        CollectionAssert.AreEqual(new[] { (2, ShownPlace.Centre) }, view.Shown);
    }

    [TestMethod]
    public void WhatAHostAsks_GoesToWhatShowsIt_WhichSaysWhereItCameTo()
    {
        using var editor = new ContentEditor(new EditedDocument("Hello"));
        var view = new View { Editor = editor };
        editor.Attach(view);

        editor.Select(0, 5);

        Assert.AreEqual(1, view.Taken.Count);
        Assert.AreEqual("Hello", editor.SelectedText(), "what the view made of it is where the editor stands");
    }

    /// <summary>What shows an editor, as far as the editor can tell: it makes what it is handed and says so.</summary>
    private sealed class View : IContentEditorView
    {
        public ContentEditor? Editor { get; init; }

        public List<EditState> Taken { get; } = [];

        public List<(int Line, ShownPlace Place)> Shown { get; } = [];

        public void Take(EditState next, bool written)
        {
            this.Taken.Add(next);
            this.Editor?.Stood(next);
        }

        public void ShowLine(int line, ShownPlace place) => this.Shown.Add((line, place));

        public IReadOnlyList<(int Start, int End)> Foldable() => [];
    }
}

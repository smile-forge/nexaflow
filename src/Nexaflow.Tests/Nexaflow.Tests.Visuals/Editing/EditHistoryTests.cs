using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// What can be taken back: the change each step wrote and the change that takes it back, a step being a stretch of writing in
/// one place, and whether what is written is what was saved.
/// </summary>
[TestClass]
[CoversNode("markdown-block-undo")]
public class EditHistoryTests
{
    [TestMethod]
    public void TypingASentenceIsOneThingDone()
    {
        var history = new EditHistory();
        var state = new EditState("A paragraph.", 0);
        var first = state;

        foreach (var character in "Several words typed ")
        {
            var next = state.Write(character.ToString());
            history.Record(state, next);
            state = next;
        }

        Assert.AreEqual(first, history.Undo(state), "one step back is before the sentence, not before its last letter");
        Assert.IsFalse(history.CanUndo);
    }

    [TestMethod]
    public void WritingSomewhereElseIsTheNextStep()
    {
        var history = new EditHistory();
        var start = new EditState("First.\n\nSecond.", 0);

        var one = start.Write("A");
        history.Record(start, one);

        var elsewhere = one.MoveCaretTo(9);
        var two = elsewhere.Write("B");
        history.Record(one, two);

        Assert.AreEqual("AFirst.\n\nSecond.", history.Undo(two)!.Source, "only the second place written in is taken back");
        Assert.AreEqual("First.\n\nSecond.", history.Undo(one)!.Source);
    }

    [TestMethod]
    public void BackspaceCarriesOnFromTypingInTheSamePlace()
    {
        var history = new EditHistory();
        var start = new EditState("x", 1);

        var typed = start.Write("yz");
        history.Record(start, typed);

        var erased = typed.Backspace();
        history.Record(typed, erased);

        Assert.AreEqual(start, history.Undo(erased), "a slip put right is part of the same writing");
    }

    [TestMethod]
    public void WhatWasTakenBackCanBeWrittenAgain_UntilSomethingElseIsWritten()
    {
        var history = new EditHistory();
        var start = new EditState("x", 1);
        var typed = start.Write("y");
        history.Record(start, typed);

        var back = history.Undo(typed)!;
        Assert.IsTrue(history.CanRedo);
        Assert.AreEqual(typed, history.Redo(back));

        history.Undo(typed);
        history.Record(start, start.Write("z"));
        Assert.IsFalse(history.CanRedo, "writing something new forgets what could have been written again");
    }

    [TestMethod]
    public void AfterAnUndoTheNextEditIsAStepOfItsOwn()
    {
        var history = new EditHistory();
        var start = new EditState("x", 1);
        var typed = start.Write("y");
        history.Record(start, typed);
        history.Record(typed, typed.Write("z"));

        var back = history.Undo(typed.Write("z"))!;
        var again = back.Write("q");
        history.Record(back, again);

        Assert.AreEqual(back, history.Undo(again));
    }

    [TestMethod]
    public void NothingToTakeBackIsNothing()
    {
        var history = new EditHistory();

        Assert.IsNull(history.Undo(new EditState("x", 0)));
        Assert.IsNull(history.Redo(new EditState("x", 0)));
    }

    [TestMethod]
    public void OnlySoManyStepsAreKept()
    {
        var history = new EditHistory();
        var state = new EditState(string.Empty, 0);

        for (var step = 0; step < EditHistory.Limit + 20; step++)
        {
            history.Break();
            var next = state.Write("a");
            history.Record(state, next);
            state = next;
        }

        var undone = 0;
        while (history.Undo(state) is { } back) { state = back; undone++; }

        Assert.AreEqual(EditHistory.Limit, undone);
    }

    [TestMethod]
    public void BackspacingPastWhatWasTypedTakesBackToBeforeIt()
    {
        var history = new EditHistory();
        var start = new EditState("abc", 3);
        var state = start;

        foreach (var next in new Func<EditState, EditState>[] { s => s.Write("d"), s => s.Backspace(), s => s.Backspace() })
        {
            var after = next(state);
            history.Record(state, after);
            state = after;
        }

        Assert.AreEqual("ab", state.Source);
        Assert.AreEqual(start, history.Undo(state), "typing and putting it right, and a letter more, are one stretch of writing");
        Assert.IsFalse(history.CanUndo);
    }

    [TestMethod]
    public void WhatWasSavedIsKnown_UntilSomethingElseIsWritten()
    {
        var history = new EditHistory();
        var start = new EditState("x", 1);
        Assert.IsTrue(history.IsAtSaved, "what is shown afresh is what was saved");

        var typed = start.Write("y");
        history.Record(start, typed);
        Assert.IsFalse(history.IsAtSaved);

        var back = history.Undo(typed)!;
        Assert.IsTrue(history.IsAtSaved, "every step since taken back");

        var again = history.Redo(back)!;
        Assert.IsFalse(history.IsAtSaved);

        history.MarkSaved();
        Assert.IsTrue(history.IsAtSaved);

        var more = again.Write("z");
        history.Record(again, more);
        Assert.IsFalse(history.IsAtSaved, "writing after a save is a step of its own");
        Assert.IsTrue(history.Undo(more) is not null && history.IsAtSaved, "and taking it back is what was saved");
    }

    [TestMethod]
    public void WritingAfterTakingBackPastWhatWasSaved_LosesTheWayBackToIt()
    {
        var history = new EditHistory();
        var start = new EditState("x", 1);
        var typed = start.Write("y");
        history.Record(start, typed);
        history.MarkSaved();

        var back = history.Undo(typed)!;
        history.Record(back, back.Write("q"));

        Assert.IsFalse(history.IsAtSaved);
        Assert.IsTrue(history.Undo(back.Write("q")) is not null);
        Assert.IsFalse(history.IsAtSaved, "what was saved was only ever forward of here, and writing forgot it");
    }

    [TestMethod]
    public void ADocumentChangedBehindTheHistory_IsNotWrittenInto()
    {
        var history = new EditHistory();
        var start = new EditState("one", 3);
        history.Record(start, start.Write(" two"));

        Assert.IsNull(history.Undo(new EditState("something else entirely", 0)), "its changes are true of the document it left, not this one");
        Assert.IsFalse(history.CanUndo, "and it is let go rather than kept to be written into the wrong place");
    }

    [TestMethod]
    public void AStepHoldsWhatItWrote_SoAFewHugeOnesAreAllThatIsKept()
    {
        var history = new EditHistory();
        var state = new EditState(string.Empty, 0);
        var huge = new string('w', EditHistory.TextLimit / 3);

        for (var step = 0; step < 4; step++)
        {
            history.Break();
            var next = state.Write(huge);
            history.Record(state, next);
            state = next;
        }

        var undone = 0;
        while (history.Undo(state) is { } back) { state = back; undone++; }

        Assert.AreEqual(3, undone, "four steps of a third of the limit each hold more than it, so the oldest goes");
    }
}

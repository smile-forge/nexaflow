using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// What can be taken back: whole states rather than operations, and a step being a stretch of writing in one place.
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
}

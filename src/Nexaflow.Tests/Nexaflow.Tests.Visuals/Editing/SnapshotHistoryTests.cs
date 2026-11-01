using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Editing;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// <see cref="SnapshotHistory{T}"/>: undo and redo over whole snapshots, a gesture that is one step however many
/// commits it makes, the bound on how far back it goes, and the comparer that decides what counts as an edit.
/// </summary>
[TestClass]
[CoversNode("vcommon-snapshot-history")]
public class SnapshotHistoryTests
{
    [TestMethod]
    public void UndoAndRedo_WalkTheSnapshots_AndANewEditDropsTheRedos()
    {
        var history = new SnapshotHistory<int>(0);
        history.Commit(1);
        history.Commit(2);

        Assert.IsTrue(history.Undo());
        Assert.AreEqual(1, history.Current);
        Assert.IsTrue(history.CanRedo);
        Assert.IsTrue(history.Redo());
        Assert.AreEqual(2, history.Current);

        history.Undo();
        history.Commit(5);
        Assert.IsFalse(history.CanRedo, "a new edit forks the history");
        history.Undo();
        history.Undo();
        Assert.AreEqual(0, history.Current);
        Assert.IsFalse(history.Undo());

        history.Commit(0);
        Assert.IsTrue(history.CanRedo, "committing what is already current is no edit");
    }

    [TestMethod]
    [CoversNode("images-recipe-history")]
    public void AGesture_IsOneStep_OrNoneIfItEndsWhereItBegan()
    {
        var history = new SnapshotHistory<int>(0);
        history.BeginGesture();
        for (int i = 1; i <= 10; i++) history.Commit(i);
        history.EndGesture();

        Assert.AreEqual(10, history.Current);
        Assert.IsTrue(history.Undo());
        Assert.AreEqual(0, history.Current, "ten slider ticks, one undo");

        history.BeginGesture();
        history.Commit(3);
        history.Commit(0);
        history.EndGesture();
        Assert.IsFalse(history.CanUndo, "a drag that ended where it began adds nothing");

        history.BeginGesture();
        history.Commit(7);
        history.CancelGesture();
        Assert.AreEqual(0, history.Current, "cancel puts it back");
    }

    [TestMethod]
    public void TheHistory_IsBounded_AndAnArrivingSnapshotIsUndoable()
    {
        var history = new SnapshotHistory<int>(0, limit: 3);
        for (int i = 1; i <= 5; i++) history.Commit(i);
        int steps = 0;
        while (history.Undo()) steps++;
        Assert.AreEqual(3, steps);

        history.Adopt(9);
        Assert.IsTrue(history.Undo(), "another window's edit arrives as a step of its own");
    }

    [TestMethod]
    public void TheComparer_DecidesWhatCountsAsAnEdit()
    {
        var history = new SnapshotHistory<string>("page", StringComparer.OrdinalIgnoreCase);
        var changes = 0;
        history.Changed += () => changes++;

        history.Commit("PAGE");
        Assert.AreEqual("page", history.Current, "equal under the comparer, so nothing is committed");
        Assert.IsFalse(history.CanUndo);
        Assert.AreEqual(0, changes);

        history.BeginGesture();
        history.Commit("pages");
        history.Commit("Page");
        history.EndGesture();
        Assert.IsFalse(history.CanUndo, "a gesture back to an equal snapshot adds nothing");
    }

    [TestMethod]
    public void StepsPastTheBudget_FallOffOldestFirst()
    {
        var history = new SnapshotHistory<int>(0, cost: (_, _) => 100, budget: 250);
        for (int i = 1; i <= 5; i++) history.Commit(i);
        Assert.AreEqual(2, history.UndoCount, "two steps of 100 fit a budget of 250");
        Assert.AreEqual(200L, history.Cost);
        history.Undo();
        history.Undo();
        Assert.AreEqual(3, history.Current, "the oldest went");
        Assert.IsFalse(history.Undo());

        history.Budget = 100;
        Assert.AreEqual(100L, history.Cost, "lowering the budget lets the farthest steps go at once");
    }

    [TestMethod]
    public void AnAmendedState_IsCurrent_WithoutAStepOfItsOwn()
    {
        var history = new SnapshotHistory<int>(0);
        history.Commit(1);
        history.Amend(2);
        Assert.AreEqual(2, history.Current);
        Assert.AreEqual(1, history.UndoCount, "no step for the amendment");
        history.Undo();
        Assert.AreEqual(0, history.Current);
        history.Redo();
        Assert.AreEqual(2, history.Current, "redo comes back to the state as amended");
    }
}

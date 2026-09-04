using System;
using System.Linq;
using Nexaflow.Services.Initiatives.Hosting.Ipc;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Hosting;

/// <summary>
/// What the daemon can say about work it has been given, while it is still doing it.
/// <para>
/// The point of the ledger is that a caller waiting on a command can ask after it and be answered at once,
/// whatever the work is doing — so the states it reports have to be right at the awkward moments rather than
/// only at the end. Queued is not running; a command waiting on another has to be able to say which one; and
/// work that has just finished has to stay answerable, because a client's last poll and the answer it was
/// waiting for cross on the wire routinely and "no such command" would send someone hunting a fault that
/// does not exist.
/// </para>
/// </summary>
[TestClass]
[NoCoverage("Transport for the headless CLI — infrastructure, not a product-tree node.")]
public class WorkLedgerTests
{
    private long _now = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc).Ticks;

    private WorkLedger Ledger(TimeSpan? remember = null) => new(remember, () => _now);

    private void Pass(TimeSpan time) => _now += time.Ticks;

    [TestMethod]
    public void AcceptedWork_IsQueuedUntilItGetsItsTurn()
    {
        var ledger = Ledger();
        var work   = ledger.Accept("t1", ["graph", "stats"], "");

        Assert.AreEqual(WorkState.Queued, ledger.StatusOf("t1").State);

        ledger.Running(work);
        Assert.AreEqual(WorkState.Running, ledger.StatusOf("t1").State);

        ledger.Done(work);
        Assert.AreEqual(WorkState.Finished, ledger.StatusOf("t1").State);
    }

    /// <summary>Waiting for a turn and taking a long time are different problems, so they are counted
    /// separately: the wait stops when the work starts, and the run starts from there.</summary>
    [TestMethod]
    public void TheWaitAndTheRun_AreCountedApart()
    {
        var ledger = Ledger();
        var work   = ledger.Accept("t1", ["graph", "build"], "");

        Pass(TimeSpan.FromSeconds(4));
        ledger.Running(work);
        Pass(TimeSpan.FromSeconds(10));

        var status = ledger.StatusOf("t1");
        Assert.AreEqual(4,  status.WaitedSeconds, 0.01);
        Assert.AreEqual(10, status.RanSeconds,    0.01);

        // And once it is done, neither goes on climbing.
        ledger.Done(work);
        Pass(TimeSpan.FromSeconds(30));

        Assert.AreEqual(10, ledger.StatusOf("t1").RanSeconds, 0.01);
    }

    /// <summary>"Behind a graph build that has run ninety seconds" and "taking ninety seconds itself" are
    /// different situations, and a caller told only that it is slow cannot tell which it is in.</summary>
    [TestMethod]
    public void AQueuedCommand_NamesWhatItIsWaitingOn()
    {
        var ledger = Ledger();
        var first  = ledger.Accept("t1", ["graph", "build"], @"D:\repo");
        ledger.Running(first);

        Pass(TimeSpan.FromSeconds(90));
        ledger.Accept("t2", ["graph", "stats"], @"D:\repo");

        var status = ledger.StatusOf("t2");
        Assert.AreEqual(WorkState.Queued, status.State);
        Assert.IsNotNull(status.Behind);
        StringAssert.Contains(status.Behind!, "graph build");
        StringAssert.Contains(status.Behind!, "90s");
    }

    /// <summary>Different working trees do not queue behind each other, so one must never be reported as
    /// waiting on the other — that would send a caller looking at a command that is no concern of theirs.</summary>
    [TestMethod]
    public void WorkOnAnotherTree_IsNotWhatItIsWaitingOn()
    {
        var ledger = Ledger();
        ledger.Running(ledger.Accept("t1", ["graph", "build"], @"D:\repo\.claude\worktrees\a"));

        ledger.Accept("t2", ["graph", "stats"], @"D:\repo");

        Assert.IsNull(ledger.StatusOf("t2").Behind);
    }

    [TestMethod]
    public void FinishedWorkIsForgotten_ButNotBeforeTheAnswerHasLanded()
    {
        var ledger = Ledger(TimeSpan.FromMinutes(1));
        ledger.Done(ledger.Accept("t1", ["graph", "stats"], ""));

        // The client's last poll arrives just after the answer it was about. It must still be answerable.
        Pass(TimeSpan.FromSeconds(2));
        ledger.Accept("t2", ["find", "text"], "");
        Assert.AreEqual(WorkState.Finished, ledger.StatusOf("t1").State);

        // Long afterwards, nobody can still be waiting on it, and it goes.
        Pass(TimeSpan.FromMinutes(5));
        ledger.Accept("t3", ["find", "more"], "");
        Assert.AreEqual(WorkState.Unknown, ledger.StatusOf("t1").State);
    }

    /// <summary>A ticket from a daemon that has since restarted is a question this one cannot answer, and
    /// answering it is still better than throwing at whoever asked.</summary>
    [TestMethod]
    public void ATicketItHasNeverSeen_IsAnswered_NotThrown()
    {
        var status = Ledger().StatusOf("nothing-like-this");

        Assert.AreEqual(WorkState.Unknown, status.State);
        Assert.AreEqual("nothing-like-this", status.Ticket);
    }

    [TestMethod]
    public void EverythingOnTheBooks_ComesBackLongestFirst()
    {
        var ledger = Ledger();
        ledger.Running(ledger.Accept("old", ["graph", "build"], ""));
        Pass(TimeSpan.FromSeconds(60));
        ledger.Running(ledger.Accept("new", ["graph", "stats"], ""));

        var all = ledger.All();

        Assert.AreEqual(2, all.Length);
        Assert.AreEqual("old", all[0].Ticket, "the one that has been going longest is the one being asked about");
    }

    /// <summary>The command line goes into one-line reports, so a `graph edit` carrying a page of replacement
    /// text must not take the report with it.</summary>
    [TestMethod]
    public void ALongCommandLine_IsShortened()
    {
        var described = WorkLedger.Describe(["graph", "edit", "substitute", new string('x', 400)]);

        Assert.IsTrue(described.Length <= 120, $"was {described.Length}");
        StringAssert.StartsWith(described, "graph edit substitute");
        StringAssert.EndsWith(described, "...");
    }
}

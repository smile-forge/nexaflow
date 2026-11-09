using System.IO;
using Nexaflow.Core.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The app sweeping its own drafts as soon as it starts and once a day while it runs, each time off what the tabs name; a sweep
/// still going lets the next pass, and one that fails waits for the next.
/// </summary>
[TestClass]
[CoversNode("made-file-drafts")]
public class DraftSweepingTests
{
    /// <summary>A clock whose timers are only written down, for a test to fire by hand.</summary>
    private sealed class Clock : TimeProvider
    {
        public List<(TimerCallback Callback, TimeSpan Due, TimeSpan Period)> Timers { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Timers.Add((callback, dueTime, period));
            return new Idle();
        }

        private sealed class Idle : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [TestMethod]
    public void ItSweepsAWhileAfterStarting_ThenDaily_StartedOnce()
    {
        var clock = new Clock();
        var sweeping = new DraftSweeping(() => Task.FromResult<IReadOnlyList<string>>([]), _ => { }, clock);

        sweeping.Start();
        sweeping.Start();

        Assert.AreEqual(1, clock.Timers.Count, "started again, it is the same sweeping");
        Assert.AreEqual(DraftSweeping.FirstAfter, clock.Timers[0].Due, "the first a while after it starts, the disk left to the starting app");
        Assert.IsTrue(DraftSweeping.FirstAfter >= TimeSpan.FromMinutes(1));
        Assert.AreEqual(TimeSpan.FromDays(1), clock.Timers[0].Period, "then once a day for as long as the app runs");
    }

    [TestMethod]
    public async Task EachSweep_IsHandedWhatTheTabsNameThen()
    {
        var named = new[] { @"C:\drafts\a1\picture.png" };
        IReadOnlyList<string>? swept = null;

        await new DraftSweeping(() => Task.FromResult<IReadOnlyList<string>>(named), paths => swept = paths, new Clock()).SweepAsync();

        CollectionAssert.AreEqual(named, swept!.ToArray());
    }

    [TestMethod]
    public async Task ASweepStillGoing_LetsTheNextPass()
    {
        var asked = new TaskCompletionSource<IReadOnlyList<string>>();
        var sweeps = 0;
        var sweeping = new DraftSweeping(() => asked.Task, _ => Interlocked.Increment(ref sweeps), new Clock());

        var first = sweeping.SweepAsync();
        await sweeping.SweepAsync();
        asked.SetResult([]);
        await first;
        await sweeping.SweepAsync();

        Assert.AreEqual(2, sweeps, "the one due while the first went on was let pass; the one after it ran");
    }

    [TestMethod]
    public async Task ASweepThatFails_WaitsForTheNext()
    {
        var sweeping = new DraftSweeping(() => Task.FromResult<IReadOnlyList<string>>([]), _ => throw new IOException("in use"), new Clock());

        await sweeping.SweepAsync();
        await sweeping.SweepAsync();
    }
}

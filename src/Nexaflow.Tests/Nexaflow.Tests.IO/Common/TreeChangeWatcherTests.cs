using System;
using System.IO;
using System.Linq;
using System.Threading;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common;

/// <summary>
/// Collecting what changed under a tree, for a holder that redoes only what moved: every path, once, handed over when
/// asked — and the one call a burst deserves, a quiet moment after it.
/// </summary>
[TestClass]
[CoversNode("iocommon-treewatch")]
public class TreeChangeWatcherTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = TestTemp.CreateSubdirectory("tree-watch-").FullName;
        Directory.CreateDirectory(Path.Combine(_root, "src", "deep"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static void Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail($"timed out waiting for {what}");
            Thread.Sleep(20);
        }
    }

    [TestMethod]
    public void AFileWrittenAnywhereBeneath_IsCollected_AndTakenOnce()
    {
        using var watcher = new TreeChangeWatcher(_root, _ => true);
        var file = Path.Combine(_root, "src", "deep", "A.cs");

        File.WriteAllText(file, "class A { }");
        Until(() => watcher.HasChanges, "the write to be collected");
        Thread.Sleep(200);                             // one write is several notifications; the rest of them land too

        CollectionAssert.Contains(watcher.Take().Paths.ToList(), file);
        Assert.IsFalse(watcher.HasChanges, "what was taken is gone, and the next batch starts empty");
    }

    [TestMethod]
    public void ARename_IsCollectedByBothNames()
    {
        var from = Path.Combine(_root, "src", "Old.cs");
        var to   = Path.Combine(_root, "src", "New.cs");
        File.WriteAllText(from, "class A { }");

        using var watcher = new TreeChangeWatcher(_root, _ => true);
        File.Move(from, to);
        Until(() => watcher.HasChanges, "the rename to be collected");
        Thread.Sleep(100);

        var paths = watcher.Take().Paths.ToList();
        CollectionAssert.Contains(paths, from, "the old name, so whatever described it is let go");
        CollectionAssert.Contains(paths, to);
    }

    [TestMethod]
    public void APathItIsToldToIgnore_IsNotCollected()
    {
        using var watcher = new TreeChangeWatcher(_root, path => !path.Contains("deep", StringComparison.Ordinal));

        File.WriteAllText(Path.Combine(_root, "src", "deep", "Noise.cs"), "class N { }");
        File.WriteAllText(Path.Combine(_root, "src", "Signal.cs"), "class S { }");
        Until(() => watcher.HasChanges, "the relevant write to be collected");
        Thread.Sleep(200);

        var paths = watcher.Take().Paths.ToList();
        Assert.IsTrue(paths.Exists(p => p.EndsWith("Signal.cs", StringComparison.Ordinal)));
        Assert.IsFalse(paths.Exists(p => p.Contains("deep", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A checkout is thousands of notifications in a second; what the holder wants is one call once it is over.
    /// <para>
    /// The quiet period runs on a clock the test holds, so the writes are one burst however long the runner takes over
    /// them. A rename the watcher is told to ignore closes the burst: notifications arrive one at a time and in order, so
    /// by the time it is asked about the rename it has noted, and armed for, everything the writes raised — and an ignored
    /// path arms nothing itself.
    /// </para>
    /// </summary>
    [TestMethod]
    public void ABurst_IsOneCall_AQuietMomentAfterItsLastNotification()
    {
        var staged = Path.Combine(_root, "End.tmp");
        var end    = Path.Combine(_root, "End.done");
        File.WriteAllText(staged, string.Empty);

        var clock = new HeldClock();
        using var ended = new ManualResetEventSlim();
        using var watcher = new TreeChangeWatcher(_root, path =>
        {
            if (string.Equals(path, end, StringComparison.OrdinalIgnoreCase)) ended.Set();
            return !path.StartsWith(Path.Combine(_root, "End."), StringComparison.OrdinalIgnoreCase);
        }, quietMs: 200, time: clock);
        var calls = 0;
        watcher.Changed += () => calls++;

        for (var i = 0; i < 20; i++) File.WriteAllText(Path.Combine(_root, "src", $"F{i}.cs"), $"class F{i} {{ }}");
        File.Move(staged, end);
        Until(() => ended.IsSet, "the burst to be delivered");

        Assert.AreEqual(0, calls, "nothing while the burst lasts");
        clock.Advance(TimeSpan.FromMilliseconds(199));
        Assert.AreEqual(0, calls, "nothing until the quiet period has passed since the last notification");
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.AreEqual(1, calls);
        Assert.AreEqual(20, watcher.Take().Paths.Count(p => Path.GetFileName(p).StartsWith('F')));
    }

    /// <summary>A clock that moves only when the test moves it; a timer fires on the test's thread, inside <see cref="Advance"/>.</summary>
    private sealed class HeldClock : TimeProvider
    {
        private readonly List<Timer> _timers = [];
        private DateTimeOffset _now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() { lock (_timers) return _now; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            List<Timer> due;
            lock (_timers)
            {
                _now += by;
                due = [.. _timers.Where(t => t.Due <= _now)];
                foreach (var timer in due) _timers.Remove(timer);
            }
            foreach (var timer in due) timer.Fire();
        }

        private sealed class Timer(HeldClock clock, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset Due { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock._timers)
                {
                    clock._timers.Remove(this);
                    if (dueTime == Timeout.InfiniteTimeSpan) return true;
                    Due = clock._now + dueTime;
                    clock._timers.Add(this);
                }
                return true;
            }

            public void Fire() => callback(state);
            public void Dispose() { lock (clock._timers) clock._timers.Remove(this); }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    /// <summary>
    /// What a holder took and is still folding in stays pending until it says it is done, so a reader looking in between learns
    /// a change is on its way rather than finding nothing and trusting a state the batch is about to replace.
    /// </summary>
    [TestMethod]
    public void AHeldBatch_IsStillPending_UntilItIsSettled()
    {
        using var watcher = new TreeChangeWatcher(_root, _ => true);
        var file = Path.Combine(_root, "src", "deep", "A.cs");

        File.WriteAllText(file, "class A { }");
        Until(() => watcher.HasChanges, "the write to be collected");
        Thread.Sleep(200);                             // one write is several notifications; the rest of them land too

        var held = watcher.Hold();
        Assert.IsFalse(watcher.HasChanges, "held is taken: the next batch starts empty");
        CollectionAssert.Contains(watcher.Pending().Paths.ToList(), file, "but it is still pending");

        watcher.Settle(held);
        Assert.IsFalse(watcher.Pending().Paths.Contains(file), "and once settled it is not");
    }

    /// <summary>A path that came, went or was renamed is told apart from one only written to: for a folder, only the first says
    /// anything its entries do not say themselves.</summary>
    [TestMethod]
    public void AFileCreated_IsMoved_AndAFileRewritten_IsNot()
    {
        var rewritten = Path.Combine(_root, "src", "Old.cs");
        File.WriteAllText(rewritten, "class O { }");
        using var watcher = new TreeChangeWatcher(_root, _ => true);
        var created = Path.Combine(_root, "src", "deep", "New.cs");

        File.WriteAllText(created, "class N { }");
        File.AppendAllText(rewritten, "// more");
        Until(() => watcher.Pending().Paths.Contains(rewritten), "the rewrite to be collected");
        Thread.Sleep(100);

        var batch = watcher.Take();
        Assert.IsTrue(batch.Moved.Contains(created));
        Assert.IsFalse(batch.Moved.Contains(rewritten), "written to, not come or gone");
    }
}

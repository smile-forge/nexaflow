using Nexaflow.Providers.Common;

namespace Nexaflow.Tests.Providers;

/// <summary>When a provider's model is in use and how long it has stood idle — and that letting it go and using it never
/// overlap.</summary>
[TestClass]

public class ModelUseTrackerTests
{
    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }

    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(15);

    [TestMethod]
    public async Task AUse_IsInUseUntilItEnds_AndItsEndsAreSaid()
    {
        var tracker = new ModelUseTracker();
        int began = 0, idle = 0;
        tracker.Began += (_, _) => began++;
        tracker.Idle += (_, _) => idle++;

        var first = await tracker.BeginAsync();
        var second = await tracker.BeginAsync();
        first.Dispose();
        first.Dispose();
        Assert.IsTrue(tracker.InUse, "one use is still going");
        second.Dispose();

        Assert.AreEqual((2, 1, false), (began, idle, tracker.InUse), "idle once, when the last use ended");
    }

    [TestMethod]
    public async Task AModelIsLetGo_OnlyOnceLoadedAndIdleLongEnough_AndOnce()
    {
        var clock = new Clock();
        var tracker = new ModelUseTracker(clock);
        int released = 0;
        Task Release(CancellationToken _) { released++; return Task.CompletedTask; }

        Assert.IsFalse(await tracker.ReleaseIfIdleAsync(Idle, Release), "nothing was loaded");

        using (await tracker.BeginAsync()) { }
        clock.Advance(Idle - TimeSpan.FromSeconds(1));
        Assert.IsFalse(await tracker.ReleaseIfIdleAsync(Idle, Release));

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.IsTrue(await tracker.ReleaseIfIdleAsync(Idle, Release));
        Assert.IsFalse(await tracker.ReleaseIfIdleAsync(Idle, Release), "let go already");
        Assert.AreEqual(1, released);
    }

    [TestMethod]
    public async Task AUseBegunWhileTheModelIsLetGo_WaitsForIt_ThenLoadsItAgain()
    {
        var clock = new Clock();
        var tracker = new ModelUseTracker(clock);
        tracker.Loaded();
        clock.Advance(Idle);
        var letGo = new TaskCompletionSource();

        var releasing = tracker.ReleaseIfIdleAsync(Idle, _ => letGo.Task);
        var use = tracker.BeginAsync().AsTask();
        await Task.Delay(100);
        Assert.IsFalse(use.IsCompleted, "the use waits while the model is being let go");

        letGo.SetResult();
        Assert.IsTrue(await releasing);
        (await use).Dispose();
        clock.Advance(Idle);
        Assert.IsTrue(await tracker.ReleaseIfIdleAsync(Idle, _ => Task.CompletedTask), "that use loaded it again");
    }

    [TestMethod]
    public async Task AStreamRead_IsOneUse_EndedWithIt()
    {
        var tracker = new ModelUseTracker();
        static async IAsyncEnumerable<int> Numbers()
        {
            yield return 1;
            await Task.Yield();
            yield return 2;
        }

        bool inUseWhileRead = false;
        await foreach (var _ in tracker.During(Numbers())) inUseWhileRead |= tracker.InUse;

        Assert.AreEqual((true, false), (inUseWhileRead, tracker.InUse));
    }
}

using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// One step on each card at a time, the next to the most urgent request waiting for that card; and a generation job that
/// gives way to people for its first moments and then runs alongside them.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class GpuSchedulerTests
{
    [TestMethod]
    public async Task TheNextStep_GoesToTheHighestPriorityWaiting_ThenFirstComeFirstServed()
    {
        var gpu = new GpuScheduler();
        var running = await gpu.NextAsync(RequestPriority.Background, Card, default);

        var order = new List<string>();
        Task Wait(string name, RequestPriority priority) => gpu.NextAsync(priority, Card, default).AsTask()
            .ContinueWith(t => { lock (order) order.Add(name); t.Result.Dispose(); }, TaskScheduler.Default);
        var waits = new[]
        {
            Wait("analysis", RequestPriority.Background),
            Wait("reply 1", RequestPriority.Interactive),
            Wait("job", RequestPriority.Job),
            Wait("reply 2", RequestPriority.Interactive),
            Wait("voice", RequestPriority.Voice),
        };

        running.Dispose();
        await Task.WhenAll(waits);

        CollectionAssert.AreEqual(new[] { "voice", "reply 1", "reply 2", "analysis", "job" }, order);
    }

    [TestMethod]
    public async Task ARequestThatStopsWaiting_IsPassedOver()
    {
        var gpu = new GpuScheduler();
        var running = await gpu.NextAsync(RequestPriority.Interactive, Card, default);
        using var gone = new CancellationTokenSource();

        var cancelled = gpu.NextAsync(RequestPriority.Voice, Card, gone.Token).AsTask();
        var next      = gpu.NextAsync(RequestPriority.Background, Card, default).AsTask();
        gone.Cancel();
        running.Dispose();

        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);
        (await next.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [TestMethod]
    public async Task AStepEndsOnce_HoweverOftenItIsDisposed()
    {
        var gpu = new GpuScheduler();
        var step = await gpu.NextAsync(RequestPriority.Interactive, Card, default);
        var second = gpu.NextAsync(RequestPriority.Interactive, Card, default).AsTask();
        var third  = gpu.NextAsync(RequestPriority.Interactive, Card, default).AsTask();

        step.Dispose();
        step.Dispose();

        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(third.IsCompleted, "a second dispose would have handed the GPU to two at once");
        (await second).Dispose();
        (await third.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    private const int Card = 0;

    private static readonly TimeSpan Soon = TimeSpan.FromMilliseconds(200);

    [TestMethod]
    public async Task StepsOnDifferentCards_NeverWaitForEachOther()
    {
        var gpu = new GpuScheduler();
        using var drawing = await gpu.NextAsync(RequestPriority.Job, 1, default);

        var reply = gpu.NextAsync(RequestPriority.Interactive, 0, default).AsTask();
        var cpu   = gpu.NextAsync(RequestPriority.Interactive, null, default).AsTask();

        Assert.IsTrue(reply.IsCompleted && cpu.IsCompleted, "a card, or the CPU, nobody else is using is free");
        (await reply).Dispose();
        (await cpu).Dispose();
    }

    [TestMethod]
    public async Task AJobInItsFirstMoments_TakesNoStepWhileSomeoneIsAttendedOnItsCard_AndOneWhenTheyAreDone()
    {
        var gpu   = new GpuScheduler(new ManualClock());
        var reply = gpu.Engage(RequestPriority.Interactive, Card);

        var job       = gpu.Open(RequestPriority.Job).NextAsync(Card, default).AsTask();
        var elsewhere = gpu.Open(RequestPriority.Job).NextAsync(1, default).AsTask();
        await Task.Delay(Soon);
        Assert.IsFalse(job.IsCompleted, "the reply is written first");
        Assert.IsTrue(elsewhere.IsCompleted, "a job on another card does not give way to it");

        using var token = await gpu.NextAsync(RequestPriority.Interactive, Card, default);
        reply.Dispose();
        await Task.Delay(Soon);
        Assert.IsFalse(job.IsCompleted, "it still takes its turn");

        token.Dispose();
        (await job.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        (await elsewhere).Dispose();
    }

    [TestMethod]
    public async Task AJobGivenBackItsTurn_WhenSomeoneComesWhileItWaited_TakesItAfterThem()
    {
        var gpu   = new GpuScheduler(new ManualClock());
        var token = await gpu.NextAsync(RequestPriority.Interactive, Card, default);

        var job = gpu.Open(RequestPriority.Job).NextAsync(Card, default).AsTask();   // waiting for the card
        var reply = gpu.Engage(RequestPriority.Voice, Card);                        // someone speaks meanwhile
        token.Dispose();
        await Task.Delay(Soon);
        Assert.IsFalse(job.IsCompleted, "the turn that came as they spoke is given back");

        reply.Dispose();
        (await job.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [TestMethod]
    public async Task AJobPastItsFirstMoments_RunsAlongside_TakingNoTurn()
    {
        var clock = new ManualClock();
        var gpu   = new GpuScheduler(clock);
        var lane  = gpu.Open(RequestPriority.Job);
        clock.Advance(GpuScheduler.Deference);

        using var reply = gpu.Engage(RequestPriority.Interactive, Card);
        using var token = await gpu.NextAsync(RequestPriority.Interactive, Card, default);

        var pass = lane.NextAsync(Card, default).AsTask();
        Assert.IsTrue(pass.IsCompleted, "it neither waits for the reply nor for the token on the card");
        var next = gpu.NextAsync(RequestPriority.Background, Card, default).AsTask();
        token.Dispose();
        (await next.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
        (await pass).Dispose();
    }

    [TestMethod]
    public async Task AJobKeptWaitingPastItsFirstMoments_RunsAlongside_FromThen()
    {
        var clock = new ManualClock();
        var gpu   = new GpuScheduler(clock);
        using var reply = gpu.Engage(RequestPriority.Interactive, Card);

        var job = gpu.Open(RequestPriority.Job).NextAsync(Card, default).AsTask();
        await Task.Delay(Soon);
        Assert.IsFalse(job.IsCompleted);

        clock.Advance(GpuScheduler.Deference);
        (await job.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [TestMethod]
    public void WorkNobodyWatches_EngagesNothing()
    {
        var gpu = new GpuScheduler(new ManualClock());
        using var analysis = gpu.Engage(RequestPriority.Background, Card);

        Assert.IsTrue(gpu.Open(RequestPriority.Job).NextAsync(Card, default).AsTask().IsCompleted);
    }

    /// <summary>Time that moves only when told, firing the timers it passes.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private readonly List<Timer> _timers = [];
        private DateTimeOffset _now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

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

        private sealed class Timer(ManualClock clock, TimerCallback callback, object? state) : ITimer
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
}

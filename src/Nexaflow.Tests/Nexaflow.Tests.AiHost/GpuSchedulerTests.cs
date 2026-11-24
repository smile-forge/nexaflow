using Nexaflow.AiHost;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.Tests.Fixtures;
using static Nexaflow.Tests.AiHost.TestClients;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// One step on each card at a time, the next to the most urgent request waiting for that card, the clients waiting at one
/// priority taking turns; and a generation job that gives way to people for its first moments and then runs alongside them.
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
    public async Task WhilePeopleKeepACardBusy_WorkBelowThemHasOneTurnInEight()
    {
        var gpu = new GpuScheduler();
        var running = await gpu.NextAsync(RequestPriority.Interactive, Card, default);

        var order = new List<string>();
        var waits = new List<Task> { TurnAsync(gpu, order, "analysis", RequestPriority.Background) };
        for (int i = 1; i <= 10; i++) waits.Add(TurnAsync(gpu, order, $"reply {i}", RequestPriority.Interactive));

        running.Dispose();
        await Task.WhenAll(waits);

        Assert.AreEqual(GpuScheduler.ShareOfTurns - 1, order.IndexOf("analysis"), "seven replies' turns go by, then the analysis has one");
    }

    [TestMethod]
    public async Task Speech_IsNeverKeptWaiting_ForWorkBelowIt()
    {
        var gpu = new GpuScheduler();
        var running = await gpu.NextAsync(RequestPriority.Background, Card, default);

        var order = new List<string>();
        var waits = new List<Task> { TurnAsync(gpu, order, "analysis", RequestPriority.Background) };
        for (int i = 1; i <= 10; i++) waits.Add(TurnAsync(gpu, order, $"speech {i}", RequestPriority.Voice));

        running.Dispose();
        await Task.WhenAll(waits);

        Assert.AreEqual(10, order.IndexOf("analysis"), "every piece of speech first");
    }

    /// <summary>Waits for a turn on the card as <paramref name="client"/> — the host, unless one is named — notes it, and hands
    /// it straight on.</summary>
    private static Task TurnAsync(GpuScheduler gpu, List<string> order, string name, RequestPriority priority, ClientIdentity? client = null)
        => As(client ?? ClientIdentity.Host, () => gpu.NextAsync(priority, Card, default).AsTask())
              .ContinueWith(t => { lock (order) order.Add(name); t.Result.Dispose(); }, TaskScheduler.Default);

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
    public async Task ClientsAtOnePriority_TakeTurnsStepByStep()
    {
        var gpu = new GpuScheduler();
        var running = await gpu.NextAsync(RequestPriority.Interactive, Card, default);

        var order = new List<string>();
        var waits = new List<Task>();
        for (int i = 1; i <= 3; i++) waits.Add(TurnAsync(gpu, order, $"notes {i}", RequestPriority.Interactive, Notes));
        for (int i = 1; i <= 3; i++) waits.Add(TurnAsync(gpu, order, $"mail {i}", RequestPriority.Interactive, Mail));

        running.Dispose();
        await Task.WhenAll(waits);

        CollectionAssert.AreEqual(new[] { "notes 1", "mail 1", "notes 2", "mail 2", "notes 3", "mail 3" }, order);
    }

    [TestMethod]
    public async Task AClientQueueingManySteps_HoldsAnotherBackByOneStepOnly()
    {
        var gpu = new GpuScheduler();
        var running = await gpu.NextAsync(RequestPriority.Background, Card, default);

        var order = new List<string>();
        var waits = new List<Task>();
        for (int i = 1; i <= 20; i++) waits.Add(TurnAsync(gpu, order, $"notes {i}", RequestPriority.Background, Notes));
        waits.Add(TurnAsync(gpu, order, "mail", RequestPriority.Background, Mail));

        running.Dispose();
        await Task.WhenAll(waits);

        Assert.AreEqual(1, order.IndexOf("mail"), "one of the twenty steps queued before it goes first, then it has its turn");
    }

    [TestMethod]
    public async Task BetweenPriorities_TheOrderIsKept_WhicheverClientsAsk()
    {
        var gpu = new GpuScheduler();
        var running = await gpu.NextAsync(RequestPriority.Background, Card, default);

        var order = new List<string>();
        var waits = new[]
        {
            TurnAsync(gpu, order, "notes analysis", RequestPriority.Background, Notes),
            TurnAsync(gpu, order, "notes reply 1", RequestPriority.Interactive, Notes),
            TurnAsync(gpu, order, "paint job", RequestPriority.Job, Paint),
            TurnAsync(gpu, order, "mail reply", RequestPriority.Interactive, Mail),
            TurnAsync(gpu, order, "mail voice", RequestPriority.Voice, Mail),
            TurnAsync(gpu, order, "notes reply 2", RequestPriority.Interactive, Notes),
        };

        running.Dispose();
        await Task.WhenAll(waits);

        CollectionAssert.AreEqual(new[] { "mail voice", "notes reply 1", "mail reply", "notes reply 2", "notes analysis", "paint job" }, order);
    }

    [TestMethod]
    public async Task AClientAskingAboveItsGrant_TakesItsTurnsAtTheGrant()
    {
        var gpu = new GpuScheduler();
        var running = await gpu.NextAsync(RequestPriority.Background, Card, default);
        var indexer = Paint with { Highest = RequestPriority.Background };

        var order = new List<string>();
        var waits = new[]
        {
            TurnAsync(gpu, order, "indexer asking for voice", RequestPriority.Voice, indexer),
            TurnAsync(gpu, order, "notes reply", RequestPriority.Interactive, Notes),
        };

        running.Dispose();
        await Task.WhenAll(waits);

        CollectionAssert.AreEqual(new[] { "notes reply", "indexer asking for voice" }, order, "the indexer waits as the background work it is granted");
    }

    [TestMethod]
    public async Task WhileClientsTakeTurnsAbove_WorkBelowThemStillHasOneTurnInEight()
    {
        var gpu = new GpuScheduler();
        var running = await gpu.NextAsync(RequestPriority.Interactive, Card, default);

        var order = new List<string>();
        var waits = new List<Task> { TurnAsync(gpu, order, "analysis", RequestPriority.Background, Paint) };
        for (int i = 1; i <= 5; i++)
        {
            waits.Add(TurnAsync(gpu, order, $"notes {i}", RequestPriority.Interactive, Notes));
            waits.Add(TurnAsync(gpu, order, $"mail {i}", RequestPriority.Interactive, Mail));
        }

        running.Dispose();
        await Task.WhenAll(waits);

        Assert.AreEqual(GpuScheduler.ShareOfTurns - 1, order.IndexOf("analysis"), "seven replies' turns go by, whoever's they are, then the analysis has one");
    }

    [TestMethod]
    public async Task AClientsRequestThatStopsWaiting_IsPassedOver_AndTheClientsGoOnTakingTurns()
    {
        var gpu = new GpuScheduler();
        var running = await gpu.NextAsync(RequestPriority.Interactive, Card, default);
        using var gone = new CancellationTokenSource();
        using var closed = new CancellationTokenSource();

        var order = new List<string>();
        var waits = new List<Task> { TurnAsync(gpu, order, "notes 1", RequestPriority.Interactive, Notes) };
        var cancelled = As(Notes, () => gpu.NextAsync(RequestPriority.Interactive, Card, gone.Token).AsTask());
        waits.Add(TurnAsync(gpu, order, "notes 3", RequestPriority.Interactive, Notes));
        var paint = As(Paint, () => gpu.NextAsync(RequestPriority.Interactive, Card, closed.Token).AsTask());   // every request it had stops waiting
        for (int i = 1; i <= 3; i++) waits.Add(TurnAsync(gpu, order, $"mail {i}", RequestPriority.Interactive, Mail));
        gone.Cancel();
        closed.Cancel();

        running.Dispose();
        await Task.WhenAll(waits);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);
        await Assert.ThrowsAsync<OperationCanceledException>(() => paint);
        CollectionAssert.AreEqual(new[] { "notes 1", "mail 1", "notes 3", "mail 2", "mail 3" }, order);
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
}

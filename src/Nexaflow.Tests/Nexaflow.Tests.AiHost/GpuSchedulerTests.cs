using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>One step on the GPU at a time, the next to the most urgent request waiting.</summary>
[TestClass]
[CoversNode("ai-host")]
public class GpuSchedulerTests
{
    [TestMethod]
    public async Task TheNextStep_GoesToTheHighestPriorityWaiting_ThenFirstComeFirstServed()
    {
        var gpu = new GpuScheduler();
        var running = await gpu.NextAsync(RequestPriority.Background, default);

        var order = new List<string>();
        Task Wait(string name, RequestPriority priority) => gpu.NextAsync(priority, default).AsTask()
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
        var running = await gpu.NextAsync(RequestPriority.Interactive, default);
        using var gone = new CancellationTokenSource();

        var cancelled = gpu.NextAsync(RequestPriority.Voice, gone.Token).AsTask();
        var next      = gpu.NextAsync(RequestPriority.Background, default).AsTask();
        gone.Cancel();
        running.Dispose();

        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);
        (await next.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }

    [TestMethod]
    public async Task AStepEndsOnce_HoweverOftenItIsDisposed()
    {
        var gpu = new GpuScheduler();
        var step = await gpu.NextAsync(RequestPriority.Interactive, default);
        var second = gpu.NextAsync(RequestPriority.Interactive, default).AsTask();
        var third  = gpu.NextAsync(RequestPriority.Interactive, default).AsTask();

        step.Dispose();
        step.Dispose();

        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(third.IsCompleted, "a second dispose would have handed the GPU to two at once");
        (await second).Dispose();
        (await third.WaitAsync(TimeSpan.FromSeconds(5))).Dispose();
    }
}

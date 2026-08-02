using System.Threading.Channels;
using Nexaflow.Tests.Features.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Infrastructure;

/// <summary>
/// The pump itself. It exists to make thread-affine ViewModels testable, but a test helper must never be
/// able to fail a run that passed — and this one could: work started inside a pumped block outlives it,
/// its continuations post into the closed pump, and the resulting exception landed on a thread-pool thread
/// with nobody to catch it, killing the process after every test had reported success.
/// </summary>
[TestClass]
[NoCoverage("test harness, maps to no product node")]
public class AsyncPumpTests
{
    [TestMethod]
    public void RunsWorkToCompletionOnOneThread()
    {
        int threadInside = 0, threadAfterAwait = 0;

        AsyncPump.Run(async () =>
        {
            threadInside = Environment.CurrentManagedThreadId;
            await Task.Yield();
            threadAfterAwait = Environment.CurrentManagedThreadId;
        });

        Assert.AreEqual(threadInside, threadAfterAwait, "continuations must resume on the pump thread");
    }

    [TestMethod]
    public void SurfacesFailuresFromInsideTheBlock()
        => Assert.ThrowsExactly<AssertFailedException>(
            () => AsyncPump.Run(async () => { await Task.Yield(); Assert.Fail("boom"); }));

    [TestMethod]
    public void ContinuationArrivingAfterTheBlockEnds_DoesNotCrashTheProcess()
    {
        // The exact shape that killed the run: a channel reader started inside the block, still consuming
        // after it returns. Its ReadAllAsync continuation captures the pump's context and posts into it.
        var channel = Channel.CreateUnbounded<int>();
        var drained = new TaskCompletionSource();

        AsyncPump.Run(async () =>
        {
            _ = Task.Run(async () =>
            {
                await foreach (var _ in channel.Reader.ReadAllAsync()) { }
                drained.TrySetResult();
            });
            await Task.Yield();
            // Block ends here with the reader still live — Complete() closes the queue underneath it.
        });

        // Post-completion traffic. Before the fix this threw InvalidOperationException on a thread-pool
        // thread and took the whole test host down; now the callback runs detached instead.
        for (var i = 0; i < 50; i++) channel.Writer.TryWrite(i);
        channel.Writer.Complete();

        Assert.IsTrue(drained.Task.Wait(TimeSpan.FromSeconds(5)),
            "the abandoned work should still complete, just off the pump thread");
    }

    [TestMethod]
    public void LateContinuationThatThrows_IsSwallowed()
    {
        // An abandoned continuation belongs to a test whose outcome is already decided; letting it throw
        // onto the thread pool is the same crash by another route.
        SynchronizationContext? captured = null;

        AsyncPump.Run(async () =>
        {
            captured = SynchronizationContext.Current;
            await Task.Yield();
        });

        Assert.IsNotNull(captured);
        captured.Post(_ => throw new InvalidOperationException("late and angry"), null);

        // Give the detached callback a moment to run and not take the process with it.
        Thread.Sleep(200);
    }
}

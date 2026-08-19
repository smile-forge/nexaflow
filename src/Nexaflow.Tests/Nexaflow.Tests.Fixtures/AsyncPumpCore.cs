using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Runs an async delegate to completion on the calling thread under a single-threaded
/// <see cref="SynchronizationContext"/>, so every continuation resumes on that one thread.
/// Needed to unit-test view-models that mutate an AvalonEdit <c>TextDocument</c> — it is thread-affine and
/// throws if touched from a different thread than the one that created it, which is exactly what happens
/// when an <c>async</c> load resumes on a thread-pool thread. Construct the view-model <em>inside</em> the
/// delegate so it too is owned by the pump thread.
/// <para>
/// The single implementation behind both test projects' <c>AsyncPump</c> forwarders — they can't reference
/// each other, and two copies of a harness this subtle drift.
/// </para>
/// </summary>
public static class AsyncPumpCore
{
    public static void Run(Func<Task> work)
    {
        var previous = SynchronizationContext.Current;
        var context  = new SingleThreadContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var task = work();
            // ExecuteSynchronously: end the pump on whichever thread completed the work, instead of
            // queueing that job to the thread pool. Run() blocks its caller — an MSTest worker, i.e. a pool
            // thread — inside RunOnCurrentThread until Complete() is called, so a pool-scheduled completion
            // means every pumped test holds one pool thread hostage while waiting for another pool thread to
            // release it. Alone there is slack and nobody notices; with method-level parallelism across the
            // ~28 conformance-derived classes the pool runs out and the rest queue behind its ~1-per-500ms
            // thread injection. That is the whole of "these tests take five times longer when run together".
            task.ContinueWith(_ => context.Complete(),
                              CancellationToken.None,
                              TaskContinuationOptions.ExecuteSynchronously,
                              TaskScheduler.Default);
            context.RunOnCurrentThread();
            task.GetAwaiter().GetResult(); // surface exceptions / failed asserts
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private sealed class SingleThreadContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback callback, object? state)> _queue = new();

        /// <summary>
        /// Queues a continuation onto the pump thread — unless the pumped block has already finished.
        /// <para>
        /// Work started inside the block can outlive it: a ViewModel that streams over a channel, a timer,
        /// a fire-and-forget task. Those continuations still capture this context and post here long after
        /// <see cref="Complete"/>. Letting <see cref="BlockingCollection{T}.Add"/> throw put an
        /// <see cref="InvalidOperationException"/> on a thread-pool thread with nobody to catch it, which
        /// killed the whole test process — <em>after</em> every test had already reported success. A test
        /// helper must never be able to fail a run that passed, so a late callback is run on the thread
        /// pool instead: the abandoned work still completes, just not on a thread that no longer exists.
        /// </para>
        /// </summary>
        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                _queue.Add((d, state));
            }
            catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
            {
                RunDetached(d, state);
            }
        }

        public override void Send(SendOrPostCallback d, object? state)
            => throw new NotSupportedException("Synchronous Send is not supported on the pump.");

        public void RunOnCurrentThread()
        {
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                callback(state);
        }

        public void Complete() => _queue.CompleteAdding();

        // The test that owned this work has finished, so its outcome is already decided. An exception here
        // belongs to abandoned work and must not escape onto the thread pool and take the process with it.
        private static void RunDetached(SendOrPostCallback d, object? state)
            => ThreadPool.QueueUserWorkItem(_ =>
            {
                try { d(state); }
                catch { /* abandoned continuation from a completed pump */ }
            });
    }
}

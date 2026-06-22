using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Tests.Core.Infrastructure;

/// <summary>
/// Runs an async delegate to completion on the calling thread under a single-threaded
/// <see cref="SynchronizationContext"/>, so every continuation resumes on that one thread.
/// Needed to unit-test view-models that mutate an AvalonEdit <c>TextDocument</c> — it is
/// thread-affine and throws if touched from a different thread than the one that created it,
/// which is exactly what happens when an <c>async</c> load resumes on a thread-pool thread.
/// Construct the view-model <em>inside</em> the delegate so it too is owned by the pump thread.
/// </summary>
public static class AsyncPump
{
    public static void Run(Func<Task> work)
    {
        var previous = SynchronizationContext.Current;
        var context  = new SingleThreadContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var task = work();
            task.ContinueWith(_ => context.Complete(), TaskScheduler.Default);
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

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state)
            => throw new NotSupportedException("Synchronous Send is not supported on the pump.");

        public void RunOnCurrentThread()
        {
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                callback(state);
        }

        public void Complete() => _queue.CompleteAdding();
    }
}

using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// The GPU's one owner. Work on it is cut into steps — a token, a slice of prompt — and each step waits its turn:
/// the next step goes to the highest-priority request waiting, first come first served within a priority. Models
/// running together only time-slice the GPU anyway (two at 30 tok/s each against 60 alone), so taking turns per
/// step costs nothing, and a background analysis gives way to a reply at the next token instead of halving it.
/// </summary>
internal sealed class GpuScheduler
{
    private readonly object _gate = new();
    private readonly PriorityQueue<TaskCompletionSource<Step>, (RequestPriority Priority, long Order)> _waiting = new();
    private long _order;
    private bool _busy;

    /// <summary>Waits for the GPU; the step ends when the returned value is disposed.</summary>
    public ValueTask<Step> NextAsync(RequestPriority priority, CancellationToken ct)
    {
        TaskCompletionSource<Step> turn;
        lock (_gate)
        {
            if (!_busy)
            {
                _busy = true;
                return ValueTask.FromResult(new Step(this));
            }
            turn = new TaskCompletionSource<Step>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiting.Enqueue(turn, (priority, _order++));
        }

        // A turn given to a request that has stopped waiting is handed straight on.
        ct.Register(() => turn.TrySetCanceled(ct));
        return new ValueTask<Step>(turn.Task);
    }

    private void Release()
    {
        lock (_gate)
        {
            while (_waiting.TryDequeue(out var next, out _))
                if (next.TrySetResult(new Step(this))) return;
            _busy = false;
        }
    }

    /// <summary>One step on the GPU.</summary>
    public sealed class Step(GpuScheduler scheduler) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) scheduler.Release();
        }
    }
}

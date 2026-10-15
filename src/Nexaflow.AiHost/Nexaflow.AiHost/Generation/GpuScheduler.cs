using Nexaflow.AiHost.Contracts;
using System.Collections.Concurrent;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// The owner of the devices models run on — each GPU, and the CPU — taking turns on each separately: models on different
/// cards never wait for each other. Work on a device is cut into steps — a token, a slice of prompt, a denoiser pass — and
/// each step waits its turn: the next step goes to the highest-priority request waiting for that device, first come first
/// served within a priority. Models running together only time-slice a GPU anyway (two at 30 tok/s each against 60 alone),
/// so taking turns per step costs nothing while the steps are token-sized, and a background analysis gives way to a reply
/// at the next token.
/// <para>
/// A generation job's steps are not token-sized: a high-quality picture's denoiser pass holds the GPU for about a third
/// of a second, and a reply asks for its next token just after the GPU has been handed on, so taking turns would give it
/// one token a pass. A job therefore takes its turns through a <see cref="Lane"/> that gives way to people. For its first
/// <see cref="Deference"/>, while whoever asked for it is most likely to change their mind or ask for more, it takes no
/// step on a device where someone is being attended to (<see cref="Engage"/>) — a reply being written, a turn being heard,
/// speech being made. After that it stops taking turns and runs alongside whatever else is on the device, which
/// time-slices the two: a reply at about half speed rather than at a token a pass.
/// </para>
/// </summary>
internal sealed class GpuScheduler(TimeProvider? clock = null)
{
    /// <summary>How long a job gives way to the person it is for: the time in which they change their mind or add to it.</summary>
    internal static readonly TimeSpan Deference = TimeSpan.FromSeconds(45);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<int, Turns> _devices = new();

    /// <summary>Waits for a turn on <paramref name="device"/> — a GPU's index, or null for the CPU; the step ends when the
    /// returned value is disposed.</summary>
    public ValueTask<Step> NextAsync(RequestPriority priority, int? device, CancellationToken ct) => On(device).NextAsync(priority, ct);

    /// <summary>
    /// Someone is being attended to on <paramref name="devices"/> — a reply someone reads or hears, their words being
    /// heard — until the returned value is disposed; a job there still within its <see cref="Deference"/> takes no step
    /// meanwhile. Work nobody is watching engages nothing.
    /// </summary>
    public IDisposable Engage(RequestPriority priority, params IEnumerable<int?> devices)
    {
        if (priority is not (RequestPriority.Voice or RequestPriority.Interactive)) return Unengaged.Instance;
        return new Engagement([.. devices.Distinct().Select(On).Select(turns => turns.Engage())]);
    }

    /// <summary>A request's turns, its age counted from now: a job's give way to people, as above; anything else's are taken
    /// as <see cref="NextAsync"/> gives them.</summary>
    public Lane Open(RequestPriority priority) => new(this, priority, _clock.GetUtcNow());

    private Turns On(int? device) => _devices.GetOrAdd(device ?? -1, _ => new Turns());

    /// <summary>One device's turns: who has it, who waits for it, and whether someone is being attended to on it.</summary>
    private sealed class Turns
    {
        private readonly object _gate = new();
        private readonly PriorityQueue<TaskCompletionSource<Step>, (RequestPriority Priority, long Order)> _waiting = new();
        private long _order;
        private bool _busy;

        /// <summary>How many pieces of attended work are under way.</summary>
        private int _engaged;

        /// <summary>Completes when nobody is being attended to.</summary>
        private TaskCompletionSource _unattended = Completed();

        public bool Engaged
        {
            get { lock (_gate) return _engaged > 0; }
        }

        public Task Unattended
        {
            get { lock (_gate) return _unattended.Task; }
        }

        public ValueTask<Step> NextAsync(RequestPriority priority, CancellationToken ct)
        {
            TaskCompletionSource<Step> turn;
            lock (_gate)
            {
                if (!_busy)
                {
                    _busy = true;
                    return ValueTask.FromResult(new Step(Release));
                }
                turn = new TaskCompletionSource<Step>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiting.Enqueue(turn, (priority, _order++));
            }

            // A turn given to a request that has stopped waiting is handed straight on.
            ct.Register(() => turn.TrySetCanceled(ct));
            return new ValueTask<Step>(turn.Task);
        }

        public Action Engage()
        {
            lock (_gate)
                if (_engaged++ == 0) _unattended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return Disengage;
        }

        private void Disengage()
        {
            TaskCompletionSource unattended;
            lock (_gate)
            {
                if (--_engaged > 0) return;
                unattended = _unattended;
            }
            unattended.TrySetResult();
        }

        public void Release()
        {
            lock (_gate)
            {
                while (_waiting.TryDequeue(out var next, out _))
                    if (next.TrySetResult(new Step(Release))) return;
                _busy = false;
            }
        }

        private static TaskCompletionSource Completed()
        {
            var done = new TaskCompletionSource();
            done.SetResult();
            return done;
        }
    }

    /// <summary>One step on a device.</summary>
    public sealed class Step(Action release) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) release();
        }
    }

    /// <summary>One request's turns.</summary>
    public sealed class Lane(GpuScheduler scheduler, RequestPriority priority, DateTimeOffset opened)
    {
        /// <summary>Whether it still gives way to people: a job within its <see cref="Deference"/>.</summary>
        public bool Defers => priority == RequestPriority.Job && Age < Deference;

        private TimeSpan Age => scheduler._clock.GetUtcNow() - opened;

        /// <summary>
        /// Waits for its next step on <paramref name="device"/>; the step ends when the returned value is disposed. A job
        /// within its <see cref="Deference"/> waits while someone is attended to there, and gives back a turn that came as
        /// someone was; after it, the step takes no turn at all and runs alongside the rest.
        /// </summary>
        public async ValueTask<IDisposable> NextAsync(int? device, CancellationToken ct)
        {
            var turns = scheduler.On(device);
            if (priority != RequestPriority.Job) return await turns.NextAsync(priority, ct);
            while (Defers)
            {
                try
                {
                    await turns.Unattended.WaitAsync(Deference > Age ? Deference - Age : TimeSpan.Zero, scheduler._clock, ct);
                }
                catch (TimeoutException)
                {
                    break;   // it has given way for as long as it gives way: it runs alongside from here
                }
                var step = await turns.NextAsync(priority, ct);
                if (!turns.Engaged) return step;
                step.Dispose();   // someone came while it waited for the device: they go first
            }
            return Unengaged.Instance;
        }
    }

    private sealed class Engagement(IReadOnlyList<Action> ends) : IDisposable
    {
        private int _ended;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ended, 1) != 0) return;
            foreach (var end in ends) end();
        }
    }

    /// <summary>Nothing to give back: an engagement of work nobody watches, or a step that took no turn.</summary>
    private sealed class Unengaged : IDisposable
    {
        public static readonly Unengaged Instance = new();
        public void Dispose() { }
    }
}

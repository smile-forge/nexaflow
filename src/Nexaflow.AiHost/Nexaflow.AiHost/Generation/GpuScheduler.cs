using Nexaflow.AiHost.Contracts;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Nexaflow.AiHost.Generation;

/// <summary>
/// The owner of the devices models run on — each GPU, and the CPU — taking turns on each separately: models on different
/// cards never wait for each other. Work on a device is cut into steps — a token, a slice of prompt, a denoiser pass — and
/// each step waits its turn: the next step goes to the highest-priority request waiting for that device. Within a priority
/// the clients waiting take turns (<see cref="ClientIdentity"/>), each one's requests first come first served, so an app
/// that queues many steps holds another's back by a step at a time, never by all of them. Models running together only
/// time-slice a GPU anyway (two at 30 tok/s each against 60 alone), so taking turns per step costs nothing while the steps
/// are token-sized, and a background analysis gives way to a reply at the next token. Giving way is not waiting for good:
/// while people's replies keep a device busy, work below them still has one turn in <see cref="ShareOfTurns"/>, so an app
/// thinking in the background moves however many people are reading. Speech alone is never kept waiting.
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

    /// <summary>Of this many turns given while work below them waits, one is that work's: a reply someone reads loses an
    /// eighth of its speed at most, and what nobody watches still moves however many replies there are.</summary>
    internal const int ShareOfTurns = 8;

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<int, Turns> _devices = new();

    /// <summary>Waits for a turn on <paramref name="device"/> — a GPU's index, or null for the CPU — at <paramref name="priority"/>, or
    /// at the client's grant where it asks for more (<see cref="ClientIdentity.Granted"/>); the step ends when the returned value is
    /// disposed.</summary>
    public ValueTask<Step> NextAsync(RequestPriority priority, int? device, CancellationToken ct)
        => RequestActivity.WaitAsync(On(device).NextAsync(ClientIdentity.Current.Granted(priority), ct));

    /// <summary>
    /// Someone is being attended to on <paramref name="devices"/> — a reply someone reads or hears, their words being
    /// heard — until the returned value is disposed; a job there still within its <see cref="Deference"/> takes no step
    /// meanwhile. Work nobody is watching engages nothing.
    /// </summary>
    public IDisposable Engage(RequestPriority priority, params IEnumerable<int?> devices)
    {
        if (ClientIdentity.Current.Granted(priority) is not (RequestPriority.Voice or RequestPriority.Interactive)) return Unengaged.Instance;
        return new Engagement([.. devices.Distinct().Select(On).Select(turns => turns.Engage())]);
    }

    /// <summary>A request's turns, its age counted from now: a job's give way to people, as above; anything else's are taken
    /// as <see cref="NextAsync"/> gives them.</summary>
    public Lane Open(RequestPriority priority) => new(this, ClientIdentity.Current.Granted(priority), _clock.GetUtcNow());

    private Turns On(int? device) => _devices.GetOrAdd(device ?? -1, _ => new Turns());

    /// <summary>One device's turns: who has it, who waits for it, and whether someone is being attended to on it.</summary>
    private sealed class Turns
    {
        private readonly object _gate = new();

        /// <summary>Who waits, for each priority: the clients in turn, each one's requests first come first served.</summary>
        private readonly PriorityTurns[] _waiting = [.. Enum.GetValues<RequestPriority>().Select(_ => new PriorityTurns())];
        private bool _busy;

        /// <summary>Turns given since work below them, waiting all the while, last had one.</summary>
        private int _passed;

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
                _waiting[(int)priority].Enqueue(ClientIdentity.Current.Key, turn);
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
                while (Next() is { } next)
                    if (next.TrySetResult(new Step(Release))) return;
                _busy = false;
            }
        }

        /// <summary>
        /// Who the next turn goes to — null when nobody waits: the client whose turn it is at the most urgent priority waiting,
        /// unless work below it has watched <see cref="ShareOfTurns"/> − 1 turns go past since its last, when it is that work's.
        /// Speech is never kept waiting, and its turns are not counted. Called under the gate.
        /// </summary>
        private TaskCompletionSource<Step>? Next()
        {
            int first = Waiting(0);
            if (first < 0)
            {
                _passed = 0;
                return null;
            }
            if (first == (int)RequestPriority.Voice) return _waiting[first].Take();

            int below = Waiting(first + 1);
            if (below < 0) _passed = 0;
            else if (_passed >= ShareOfTurns - 1)
            {
                _passed = 0;
                return _waiting[below].Take();
            }
            else _passed++;
            return _waiting[first].Take();
        }

        /// <summary>The most urgent priority from <paramref name="from"/> down that someone still waits at; −1 for none. A
        /// request that stopped waiting is dropped as it is met. Called under the gate.</summary>
        private int Waiting(int from)
        {
            for (int priority = from; priority < _waiting.Length; priority++)
                if (_waiting[priority].SomeoneWaits()) return priority;
            return -1;
        }

        private static TaskCompletionSource Completed()
        {
            var done = new TaskCompletionSource();
            done.SetResult();
            return done;
        }
    }

    /// <summary>
    /// The requests waiting at one priority on one device, their clients taking turns: each client's requests in the order
    /// they came, and each turn to the client next in the round, which then goes to the back of it while it has more waiting.
    /// One client queueing many steps holds another's back by one step at a time. A client's queue is kept once made — it
    /// asks again with its next token, and the clients are the apps connected — so a turn allocates nothing. Called under the
    /// device's gate.
    /// </summary>
    private sealed class PriorityTurns
    {
        private readonly Dictionary<string, ClientTurns> _clients = new(StringComparer.Ordinal);

        /// <summary>The clients with requests waiting, whose turn it is first.</summary>
        private readonly Queue<ClientTurns> _round = new();

        /// <summary>Queues <paramref name="turn"/> behind <paramref name="client"/>'s others; a client that had none waiting
        /// joins the round at its back.</summary>
        public void Enqueue(string client, TaskCompletionSource<Step> turn)
        {
            ref var waiting = ref CollectionsMarshal.GetValueRefOrAddDefault(_clients, client, out _);
            waiting ??= new ClientTurns();
            waiting.Requests.Enqueue(turn);
            if (waiting.InRound) return;
            waiting.InRound = true;
            _round.Enqueue(waiting);
        }

        /// <summary>Whether anyone still waits here. A request that stopped waiting is dropped as it is met, and a client with
        /// nobody left waiting leaves the round; so after a true the client whose turn it is has a request still waiting
        /// first.</summary>
        public bool SomeoneWaits()
        {
            while (_round.TryPeek(out var client))
            {
                var requests = client.Requests;
                while (requests.TryPeek(out var head) && head.Task.IsCompleted) requests.Dequeue();
                if (requests.Count > 0) return true;
                client.InRound = false;
                _round.Dequeue();
            }
            return false;
        }

        /// <summary>The next turn — the first request of the client whose turn it is, the client going to the back of the
        /// round if it has more. Only after <see cref="SomeoneWaits"/> said someone does.</summary>
        public TaskCompletionSource<Step> Take()
        {
            var client = _round.Dequeue();
            var turn = client.Requests.Dequeue();
            if (client.Requests.Count > 0) _round.Enqueue(client);
            else client.InRound = false;
            return turn;
        }
    }

    /// <summary>One client's requests waiting at one priority, first come first served.</summary>
    private sealed class ClientTurns
    {
        public Queue<TaskCompletionSource<Step>> Requests { get; } = new();

        /// <summary>Whether it is in its priority's round — it is while it has requests queued, though they may have stopped
        /// waiting since.</summary>
        public bool InRound { get; set; }
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
            if (priority != RequestPriority.Job) return await RequestActivity.WaitAsync(turns.NextAsync(priority, ct));
            while (Defers)
            {
                try
                {
                    await RequestActivity.WaitAsync(turns.Unattended.WaitAsync(Deference > Age ? Deference - Age : TimeSpan.Zero, scheduler._clock, ct));
                }
                catch (TimeoutException)
                {
                    break;   // it has given way for as long as it gives way: it runs alongside from here
                }
                var step = await RequestActivity.WaitAsync(turns.NextAsync(priority, ct));
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

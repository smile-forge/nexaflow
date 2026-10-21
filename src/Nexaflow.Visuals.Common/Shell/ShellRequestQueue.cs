using System.Diagnostics;
using System.Windows.Threading;

namespace Nexaflow.Visuals.Common.Shell;

/// <summary>
/// The rows of a list waiting on the shell, answered the way a reader scrolls. One background STA thread (the shell's
/// handlers are apartment-threaded COM) waits for requests to settle — a scroll in progress asks for rows it passes straight
/// by — then answers the newest first, since those are the rows on screen now, and skips any whose row has moved on.
/// Several rows waiting on one key share one answer, and each window's thread gets its answers handed over in batches.
/// </summary>
/// <typeparam name="TKey">What is asked for; equal keys are answered once.</typeparam>
/// <typeparam name="TResult">The answer; a null answer is handed to no one.</typeparam>
internal sealed class ShellRequestQueue<TKey, TResult> where TKey : notnull where TResult : class
{
    /// <summary>How many answers are handed to a window's thread in one go.</summary>
    private const int DeliveryBatch = 32;

    private readonly string _threadName;
    private readonly Func<TKey, TResult?> _answer;
    private readonly long _quiet;
    private readonly long _patience;

    private readonly object _gate = new();
    private readonly Dictionary<TKey, Job> _jobs = [];
    private readonly Stack<(Job Job, long Turn)> _waiting = new();
    private long _turn;
    private long _lastAsked;
    private Thread? _worker;

    /// <param name="threadName">The worker thread's name, as a debugger shows it.</param>
    /// <param name="answer">Answers one key, on the worker. It may keep what it answers; it is never called for a key nobody waits on.</param>
    /// <param name="quiet">How long requests must stop arriving before answering starts.</param>
    /// <param name="patience">The longest answering waits for that quiet, so a scroll that never stops is still answered.</param>
    public ShellRequestQueue(string threadName, Func<TKey, TResult?> answer, TimeSpan quiet, TimeSpan patience)
    {
        _threadName = threadName;
        _answer     = answer;
        _quiet      = (long)(quiet.TotalSeconds * Stopwatch.Frequency);
        _patience   = (long)(patience.TotalSeconds * Stopwatch.Frequency);
    }

    /// <summary>
    /// Asks for <paramref name="key"/>; its answer is handed to <paramref name="arrived"/> on <paramref name="dispatcher"/>'s
    /// thread, unless the returned ticket is disposed first.
    /// </summary>
    public IDisposable Enqueue(TKey key, Dispatcher dispatcher, Action<TResult> arrived)
    {
        var ticket = new Ticket(dispatcher, arrived);
        lock (_gate)
        {
            if (!_jobs.TryGetValue(key, out var job))
                _jobs[key] = job = new Job(key);

            job.Waiters.RemoveAll(waiter => waiter.Cancelled);
            job.Waiters.Add(ticket);
            job.Turn = ++_turn;
            _waiting.Push((job, job.Turn));
            _lastAsked = Stopwatch.GetTimestamp();

            if (_worker is null)
            {
                _worker = new Thread(Work) { IsBackground = true, Name = _threadName, Priority = ThreadPriority.BelowNormal };
                _worker.SetApartmentState(ApartmentState.STA);
                _worker.Start();
            }
            Monitor.Pulse(_gate);
        }
        return ticket;
    }

    private void Work()
    {
        while (true)
        {
            lock (_gate)
            {
                while (_waiting.Count == 0) Monitor.Wait(_gate);
            }
            Settle(Stopwatch.GetTimestamp());
            Drain();
        }
    }

    /// <summary>Waits until no request has arrived for the quiet period, or the patience since <paramref name="since"/> runs out.</summary>
    private void Settle(long since)
    {
        while (true)
        {
            long lastAsked;
            lock (_gate) lastAsked = _lastAsked;

            long left = Math.Min(lastAsked + _quiet, since + _patience) - Stopwatch.GetTimestamp();
            if (left <= 0) return;
            Thread.Sleep(TimeSpan.FromSeconds((double)left / Stopwatch.Frequency));
        }
    }

    private void Drain()
    {
        var outgoing = new List<(Ticket Ticket, TResult Result)>();
        while (TryTake(out var job))
        {
            // The shell's handlers are third-party code running in this process: one that throws costs its row the answer,
            // never the worker every other row is waiting on.
            TResult? result;
            try { result = _answer(job.Key); }
            catch (Exception) { result = null; }

            lock (_gate)
            {
                job.Done = true;
                _jobs.Remove(job.Key);
                if (result is not null)
                    foreach (var waiter in job.Waiters)
                        if (!waiter.Cancelled) outgoing.Add((waiter, result));
            }
            if (outgoing.Count >= DeliveryBatch) Deliver(outgoing);
        }
        Deliver(outgoing);
    }

    /// <summary>The newest request still wanted. Older turns of a request asked again, and requests nobody waits for now, are passed over.</summary>
    private bool TryTake(out Job job)
    {
        lock (_gate)
        {
            while (_waiting.TryPop(out var entry))
            {
                var candidate = entry.Job;
                if (candidate.Done || entry.Turn != candidate.Turn) continue;
                if (candidate.Waiters.TrueForAll(waiter => waiter.Cancelled))
                {
                    candidate.Done = true;
                    _jobs.Remove(candidate.Key);
                    continue;
                }
                job = candidate;
                return true;
            }
        }
        job = null!;
        return false;
    }

    /// <summary>Hands each window's thread its answers in one go, behind input and layout.</summary>
    private static void Deliver(List<(Ticket Ticket, TResult Result)> outgoing)
    {
        if (outgoing.Count == 0) return;
        foreach (var batch in outgoing.GroupBy(item => item.Ticket.Dispatcher))
        {
            var arrivals = batch.ToArray();
            batch.Key.InvokeAsync(() =>
            {
                foreach (var (ticket, result) in arrivals) ticket.Arrive(result);
            }, DispatcherPriority.Background);
        }
        outgoing.Clear();
    }

    private sealed class Job(TKey key)
    {
        public TKey Key { get; } = key;
        public List<Ticket> Waiters { get; } = [];
        public long Turn { get; set; }
        public bool Done { get; set; }
    }

    /// <summary>One row's wait for one answer. Disposed when the row moves on; it then never arrives.</summary>
    private sealed class Ticket(Dispatcher dispatcher, Action<TResult> arrived) : IDisposable
    {
        private volatile bool _cancelled;

        public Dispatcher Dispatcher { get; } = dispatcher;
        public bool Cancelled => _cancelled;

        /// <summary>On <see cref="Dispatcher"/>'s thread, the same one that disposes, so a disposed ticket is never answered.</summary>
        public void Arrive(TResult result)
        {
            if (_cancelled) return;
            _cancelled = true;
            arrived(result);
        }

        public void Dispose() => _cancelled = true;
    }
}

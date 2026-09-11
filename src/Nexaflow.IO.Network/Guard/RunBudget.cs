using System.Net;

namespace Nexaflow.IO.Network.Guard;

/// <summary>
/// What one run has spent so far. Separate from <see cref="NetworkGuard"/> so the guard's decision is a
/// pure function of (intent, budget) — which is what makes the hostile-fixture tests possible without a
/// socket anywhere in sight.
/// </summary>
/// <remarks>
/// <para>
/// Time is injected rather than read from the clock, so a rate-limit test does not have to sleep.
/// </para>
/// <para>
/// Safe to share between threads, because an address sweep sends in parallel. Every read and every write
/// takes one lock, and <see cref="Admit"/> holds it across the decision and the booking — two sends racing
/// for the last packet of a ceiling cannot both be told yes.
/// </para>
/// </remarks>
public sealed class RunBudget(Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly object _gate = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _perTarget = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DateTimeOffset> _broadcasts = [];
    private readonly List<DateTimeOffset> _all = [];
    private DateTimeOffset? _started;
    private int _packets;
    private long _bytes;

    public int Packets { get { lock (_gate) return _packets; } }
    public long Bytes { get { lock (_gate) return _bytes; } }

    public TimeSpan Elapsed { get { lock (_gate) return _started is { } s ? _clock() - s : TimeSpan.Zero; } }

    /// <summary>Broadcasts in the trailing second.</summary>
    public int BroadcastsThisSecond { get { lock (_gate) return CountRecent(_broadcasts); } }

    /// <summary>Everything this run sent in the trailing second, whatever it was sent to.</summary>
    public int PacketsThisSecond { get { lock (_gate) return CountRecent(_all); } }

    /// <summary>Packets sent to one target in the trailing second.</summary>
    public int PacketsThisSecondTo(IPAddress target)
    {
        lock (_gate)
            return _perTarget.TryGetValue(target.ToString(), out var stamps) ? CountRecent(stamps) : 0;
    }

    /// <summary>
    /// Asks the guard about one send and, if it agrees, books it — as one step.
    /// </summary>
    /// <remarks>
    /// Booked before the packet leaves rather than after, because the send is awaited, and a ceiling checked
    /// on one side of an await and spent on the other can be spent twice. A send that then fails on the wire
    /// has still used its place, which errs the right way: a budget can only run out early, never over. A
    /// refusal books nothing, so one refusal cannot cascade into refusing everything after it.
    /// </remarks>
    public GuardDecision Admit(NetworkGuard guard, SendIntent intent)
    {
        lock (_gate)
        {
            var decision = guard.Evaluate(intent, this);
            if (decision.Allowed) Record(intent);
            return decision;
        }
    }

    /// <summary>Books a send the guard allowed. <see cref="Admit"/> is how a send is made; this is for a
    /// caller — a test — that decides and books separately on purpose.</summary>
    public void Record(SendIntent intent)
    {
        lock (_gate)
        {
            var now = _clock();
            _started ??= now;

            _packets++;
            _bytes += intent.ByteCount;

            var key = intent.Target.ToString();
            if (!_perTarget.TryGetValue(key, out var stamps)) _perTarget[key] = stamps = [];
            stamps.Add(now);
            _all.Add(now);

            if (intent.Broadcast) _broadcasts.Add(now);
        }
    }

    private int CountRecent(List<DateTimeOffset> stamps)
    {
        var cutoff = _clock() - TimeSpan.FromSeconds(1);
        stamps.RemoveAll(t => t < cutoff);
        return stamps.Count;
    }
}

using System.Net;

namespace Nexaflow.IO.Network.Guard;

/// <summary>
/// What one run has spent so far. Separate from <see cref="NetworkGuard"/> so the guard's decision is a
/// pure function of (intent, budget) — which is what makes the hostile-fixture tests possible without a
/// socket anywhere in sight.
/// </summary>
/// <remarks>
/// Time is injected rather than read from the clock, so a rate-limit test does not have to sleep.
/// </remarks>
public sealed class RunBudget(Func<DateTimeOffset>? clock = null)
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Dictionary<string, List<DateTimeOffset>> _perTarget = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<DateTimeOffset> _broadcasts = [];
    private DateTimeOffset? _started;

    public int Packets { get; private set; }
    public long Bytes { get; private set; }

    public TimeSpan Elapsed => _started is { } s ? _clock() - s : TimeSpan.Zero;

    /// <summary>Broadcasts in the trailing second.</summary>
    public int BroadcastsThisSecond => CountRecent(_broadcasts);

    /// <summary>Packets sent to one target in the trailing second.</summary>
    public int PacketsThisSecondTo(IPAddress target)
        => _perTarget.TryGetValue(target.ToString(), out var stamps) ? CountRecent(stamps) : 0;

    /// <summary>Books a send that actually happened. Called only after the guard allowed it — recording on
    /// evaluation would let a refused send consume budget and make refusals cascade.</summary>
    public void Record(SendIntent intent)
    {
        var now = _clock();
        _started ??= now;

        Packets++;
        Bytes += intent.ByteCount;

        var key = intent.Target.ToString();
        if (!_perTarget.TryGetValue(key, out var stamps)) _perTarget[key] = stamps = [];
        stamps.Add(now);

        if (intent.Broadcast) _broadcasts.Add(now);
    }

    private int CountRecent(List<DateTimeOffset> stamps)
    {
        var cutoff = _clock() - TimeSpan.FromSeconds(1);
        stamps.RemoveAll(t => t < cutoff);
        return stamps.Count;
    }
}

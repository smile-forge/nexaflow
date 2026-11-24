namespace Nexaflow.AiHost.Service;

/// <summary>
/// Says when the service's worker has stood unused — no client connected, nothing held — for <paramref name="linger"/>, looked at every
/// <paramref name="every"/>: it then exits (<see cref="WorkerExit.Idle"/>), and the next client starts another. How long a client stays
/// connected while it has nothing to ask is its own to decide — Nexaflow's for as long as its Settings keep an idle model — so the
/// worker outlives its last client only briefly, long enough that an app reconnecting at once finds it still there.
/// </summary>
/// <param name="used">Whether a client is connected, or anything held.</param>
internal sealed class IdleWatch(Func<bool> used, TimeSpan linger, TimeSpan every, TimeProvider time)
{
    /// <summary>How long the worker outlives its last client.</summary>
    public static readonly TimeSpan Linger = TimeSpan.FromSeconds(30);

    /// <summary>How often it looks.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(5);

    /// <summary>Completes once the worker has stood unused for the whole of <paramref name="linger"/>.</summary>
    public async Task UntilIdleAsync(CancellationToken ct)
    {
        long since = time.GetTimestamp();
        using var timer = new PeriodicTimer(every, time);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            if (used()) since = time.GetTimestamp();
            else if (time.GetElapsedTime(since) >= linger) return;
        }
    }
}

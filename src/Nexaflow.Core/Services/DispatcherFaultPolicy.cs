

namespace Nexaflow.Core.Services;

/// <summary>A fault that has become a live-lock: it recurred <paramref name="Repeats"/> times in <paramref name="Span"/>.</summary>
public readonly record struct LiveLock(int Repeats, TimeSpan Span);

/// <summary>
/// Whether a fault that reached the UI thread's last-resort handler is still worth handling. The handler marks such faults handled so
/// the shell survives them — right for a one-off fault, catastrophic for one thrown from the WPF render pass, where handling leaves
/// the layout dirty and WPF simply re-measures and throws again. A customer sent us a 170MB crash log that was one such live-lock: a
/// single exception, 17,724 times, in 85 seconds. So past <see cref="LiveLockCount"/> identical faults in one burst
/// (<see cref="FaultBurst"/>) the handler gives up and lets the process die rather than spin — a restart the user can see beats a
/// frozen shell they cannot. The logs only record; this alone decides.
/// </summary>
/// <param name="clock">The time each fault is counted at.</param>
public sealed class DispatcherFaultPolicy(Func<DateTimeOffset> clock)
{
    /// <summary>Identical faults in one burst that mean "this is a live-lock, not a fault".</summary>
    public const int LiveLockCount = 250;

    private readonly FaultBurst _burst = new();
    private readonly Lock       _gate  = new();

    /// <summary>
    /// Counts <paramref name="fault"/>, and says whether handling it is what keeps it coming: the live-lock it has become, after
    /// which the next fault starts afresh; null while it is still worth handling.
    /// </summary>
    public LiveLock? Observe(Exception fault)
    {
        lock (_gate)
        {
            var now = clock();
            _burst.Note(FaultBurst.SignatureOf(fault), now);
            if (_burst.Count < LiveLockCount) return null;

            var locked = new LiveLock(_burst.Count, _burst.Length(now));
            _burst.End();   // a restart starts fresh
            return locked;
        }
    }
}

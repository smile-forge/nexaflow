using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Generation;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>Time that moves only when told, firing the timers it passes — a periodic one again at each period it passes — and
/// counting timestamps with it.</summary>
internal sealed class ManualClock : TimeProvider
{
    private readonly List<Timer> _timers = [];
    private DateTimeOffset _now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() { lock (_timers) return _now; }

    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new Timer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan by)
    {
        DateTimeOffset until;
        lock (_timers) until = _now + by;
        while (true)
        {
            Timer? next;
            lock (_timers)
            {
                next = _timers.Where(t => t.Due <= until).MinBy(t => t.Due);
                if (next is null)
                {
                    _now = until;
                    return;
                }
                _now = next.Due;
                if (next.Period > TimeSpan.Zero) next.Due += next.Period;
                else _timers.Remove(next);
            }
            next.Fire();
        }
    }

    private sealed class Timer(ManualClock clock, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset Due { get; set; }
        public TimeSpan Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._timers)
            {
                clock._timers.Remove(this);
                if (dueTime == Timeout.InfiniteTimeSpan) return true;
                Due = clock._now + dueTime;
                Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
                clock._timers.Add(this);
            }
            return true;
        }

        public void Fire() => callback(state);
        public void Dispose() { lock (clock._timers) clock._timers.Remove(this); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

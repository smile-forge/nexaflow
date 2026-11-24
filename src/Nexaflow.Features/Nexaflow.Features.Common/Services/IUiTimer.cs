using System;

namespace Nexaflow.Features.Common;

/// <summary>
/// A timer whose ticks run on the workspace's UI thread: what a view-model polls, animates or debounces with, without
/// naming the UI toolkit's dispatcher. <see cref="IShellServices.CreateUiTimer"/> makes one that repeats,
/// <see cref="IShellServices.CreateUiDelay"/> one that ticks once each time it is started. It is made stopped. Use it from
/// the UI thread, and dispose it with its owner — disposed, it never ticks again.
/// </summary>
public interface IUiTimer : IDisposable
{
    /// <summary>The time between ticks. Changed while running, the next tick is that long from the change.</summary>
    TimeSpan Interval { get; set; }

    /// <summary>Whether it is going to tick: started, and not stopped or disposed since.</summary>
    bool IsRunning { get; }

    /// <summary>Starts ticking, one <see cref="Interval"/> from now. Already running, it carries on as it was.</summary>
    void Start();

    /// <summary>Stops ticking. A tick already due does not come.</summary>
    void Stop();

    /// <summary>
    /// Starts again from now, running or not: with a delay, a debounce — the tick comes one <see cref="Interval"/> after the
    /// last call.
    /// </summary>
    void Restart();
}

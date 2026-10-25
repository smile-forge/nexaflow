using Nexaflow.Core.Models;

namespace Nexaflow.Core.Services;

/// <summary>
/// Decides which of a window's placement changes are the user's. Windows moves windows itself when the displays
/// change under them — a monitor asleep (DisplayPort sleep detaches it), a lid closed on a dock, an undock, a resume
/// before the dock's monitors have enumerated — so reading the placement when the window closes would save where
/// Windows put it rather than where the user did. This holds the <see cref="Intended"/> placement, lets no change
/// made during a display transition or while the intended monitor is missing replace it, and says when the window
/// should go back. Pure: <see cref="WindowPlacementTracker"/> feeds it from window messages, the tests directly.
/// </summary>
internal sealed class PlacementIntent
{
    internal enum Phase
    {
        /// <summary>The displays are steady and the intended monitor attached: every change is the user's.</summary>
        Settled,

        /// <summary>A display transition has begun (suspend, display off, a display change) and the monitors have
        /// not been read since.</summary>
        InTransition,

        /// <summary>The intended monitor is missing. The window stays where Windows or the fitting put it until the
        /// monitor returns or the user moves it.</summary>
        AwaitingMonitor,
    }

    /// <summary>Where the user means the window to be; null until it is opened at a placement or first observed.</summary>
    internal WindowPlacement? Intended { get; private set; }

    internal Phase State { get; private set; }

    /// <summary>The window opens at <paramref name="intended"/>: returns the placement to apply, fitted to the
    /// monitors <paramref name="attached"/> now, and waits for the intended monitor when it is missing.</summary>
    internal WindowPlacement Open(WindowPlacement intended, IReadOnlyList<MonitorArea> attached)
    {
        Intended = intended;
        var fitted = intended.FitTo(attached, out bool onOwnMonitor);
        State = onOwnMonitor ? Phase.Settled : Phase.AwaitingMonitor;
        return fitted;
    }

    /// <summary>
    /// The window is now at <paramref name="live"/>. A change the user made (<paramref name="byUser"/>: a drag, a
    /// resize, a caption or system-menu command) always becomes the intent, ending any wait for a missing monitor.
    /// Any other becomes it only while settled with the intended monitor still attached: a change that comes after
    /// that monitor went is Windows moving the window off it, whichever order the display messages arrive in.
    /// </summary>
    internal void Observed(WindowPlacement live, IReadOnlyList<MonitorArea> attached, bool byUser)
    {
        if (byUser)
        {
            Intended = live;
            State    = Phase.Settled;
            return;
        }

        if (State != Phase.Settled) return;

        if (Intended is { } intended && !IsAttached(intended.Monitor, attached))
        {
            State = Phase.AwaitingMonitor;
            return;
        }

        Intended = live;
    }

    /// <summary>A display transition has begun: until the monitors are read again, nothing that moves the window is the user's.</summary>
    internal void TopologyChanging() => State = Phase.InTransition;

    /// <summary>
    /// The monitors read <paramref name="attached"/> after a transition. With the intended monitor there, returns
    /// the placement to put the window back at — null when <paramref name="live"/> is already that (Windows may
    /// have moved it back itself); without it, waits for the monitor.
    /// </summary>
    internal WindowPlacement? TopologySettled(IReadOnlyList<MonitorArea> attached, WindowPlacement? live)
    {
        if (State == Phase.Settled) return null;

        if (Intended is not { } intended)
        {
            State = Phase.Settled;
            return null;
        }

        if (!IsAttached(intended.Monitor, attached))
        {
            State = Phase.AwaitingMonitor;
            return null;
        }

        State = Phase.Settled;
        var fitted = intended.FitTo(attached, out _);
        return live is not null && live.SamePlaceAs(fitted) ? null : fitted;
    }

    private static bool IsAttached(MonitorArea monitor, IReadOnlyList<MonitorArea> attached)
        => attached.Any(m => m.Id == monitor.Id);
}

namespace Nexaflow.AiHost.Client;

/// <summary>The client's watchdog limits.</summary>
/// <param name="HeartbeatInterval">How often the host is pinged.</param>
/// <param name="HeartbeatTimeout">A host that has not answered a ping for this long is killed.</param>
/// <param name="CancelGrace">How long a cancelled request may take to end before its host is judged hung.</param>
/// <param name="CrashLimit">How many unexpected exits within <paramref name="CrashWindow"/> stop restarts until the
/// window has passed.</param>
public sealed record AiHostTimings(TimeSpan HeartbeatInterval, TimeSpan HeartbeatTimeout, TimeSpan CancelGrace,
                                   int CrashLimit, TimeSpan CrashWindow)
{
    public static AiHostTimings Default { get; } = new(
        HeartbeatInterval: TimeSpan.FromSeconds(2),
        HeartbeatTimeout:  TimeSpan.FromSeconds(10),
        CancelGrace:       TimeSpan.FromSeconds(10),
        CrashLimit:        3,
        CrashWindow:       TimeSpan.FromMinutes(5));
}

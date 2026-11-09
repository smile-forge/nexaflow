namespace Nexaflow.AiHost.Client;

/// <summary>The client's watchdog limits.</summary>
/// <param name="HeartbeatInterval">How often the host is pinged.</param>
/// <param name="HeartbeatTimeout">A host that has not answered a ping for this long is killed.</param>
/// <param name="CancelGrace">How long a cancelled request may take to end before its host is judged hung.</param>
/// <param name="CrashLimit">How many unexpected exits within <paramref name="CrashWindow"/> stop restarts until the
/// window has passed.</param>
/// <param name="ExitWait">How long a host whose pipe closed or broke is given to be seen exiting, so its end carries
/// the exit code and what it wrote to its error output: a host that dies takes its pipe with it a moment before its exit
/// is seen, and a crash report being written holds the exit back further.</param>
public sealed record AiHostTimings(TimeSpan HeartbeatInterval, TimeSpan HeartbeatTimeout, TimeSpan CancelGrace,
                                   int CrashLimit, TimeSpan CrashWindow, TimeSpan ExitWait)
{
    public static AiHostTimings Default { get; } = new(
        HeartbeatInterval: TimeSpan.FromSeconds(2),
        HeartbeatTimeout:  TimeSpan.FromSeconds(10),
        CancelGrace:       TimeSpan.FromSeconds(10),
        CrashLimit:        3,
        CrashWindow:       TimeSpan.FromMinutes(5),
        ExitWait:          TimeSpan.FromSeconds(5));
}

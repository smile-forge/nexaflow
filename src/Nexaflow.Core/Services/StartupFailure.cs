namespace Nexaflow.Core.Services;

/// <summary>
/// Ends a launch whose own startup sequence threw. Handled like any other dispatcher fault, the launch would carry on
/// with no window — the app shuts down only when told to — while a release build still holds the single-instance guard,
/// so every later launch hands its window request to this process and exits: the app "runs" and never appears.
/// Instead the fault is recorded, the guard released before anything can block, the user told why and where the
/// details are (unless no one is watching the launch), and the process exits non-zero.
/// </summary>
internal sealed class StartupFailure
{
    /// <summary>The exit code of a launch that could not start.</summary>
    public const int ExitCode = 1;

    private readonly CrashLog       _log;
    private readonly Action         _releaseInstance;
    private readonly Action<string> _tell;
    private readonly Action<int>    _exit;

    /// <param name="log">Where the fault is recorded.</param>
    /// <param name="releaseInstance">Gives up the single-instance guard, so the next launch starts for itself.</param>
    /// <param name="tell">Shows the user a message. Called only for a launch someone is watching.</param>
    /// <param name="exit">Ends the process with the given exit code.</param>
    public StartupFailure(CrashLog log, Action releaseInstance, Action<string> tell, Action<int> exit)
    {
        _log             = log;
        _releaseInstance = releaseInstance;
        _tell            = tell;
        _exit            = exit;
    }

    /// <summary>
    /// Records <paramref name="fault"/>, releases the guard, tells the user when <paramref name="attended"/>, and exits.
    /// The guard goes first: a relaunch while the message is still open must start for itself, not be handed to a
    /// process on its way out. An unattended launch — the windowless <c>--prestart</c> daemon, a UI journey, a timing
    /// harness — gets no message: no one is there to dismiss it, and a journey must fail on the exit, not hang.
    /// </summary>
    public void Handle(Exception fault, bool attended)
    {
        _log.Record(fault);
        _releaseInstance();
        if (attended) _tell(MessageFor(fault, _log.CurrentPath));
        _exit(ExitCode);
    }

    private static string MessageFor(Exception fault, string logPath) =>
        $"Nexaflow could not start.{Environment.NewLine}{Environment.NewLine}{fault.Message}{Environment.NewLine}"
        + $"{Environment.NewLine}The details were written to {logPath}";
}

using System.IO;
using Microsoft.Extensions.Logging;

namespace Nexaflow.Session.Services.Logging;

/// <summary>
/// The process's logs: one <see cref="ILoggerFactory"/> over the destinations its entries are written to. Made first by the app, from
/// this launch's folders, before anything that logs — the exception hooks, the single-instance guard, the steps of startup — and held
/// by the app and its fault handlers; handed to the session (<see cref="ShellHost"/>), which hands an <see cref="ILogger{T}"/>, or the
/// factory, to each service it makes by constructor, and supplies one to any feature whose constructor asks for an
/// <see cref="ILogger{T}"/>. Disposed last, on exit.
/// <para>
/// The destinations, each keeping to its own level, file and format:
/// <list type="bullet">
///   <item><see cref="Crash"/> — every fault (<see cref="LogLevel.Error"/> and above), always, a file a day in the configuration
///   folder.</item>
///   <item><see cref="Session"/> — the session's own account (<see cref="LogLevel.Information"/> and above) while safety logging is
///   on, held in memory until the shell config says whether it is.</item>
///   <item>the binding errors WPF reports, under UI automation only (<see cref="BindingErrorLogProvider"/>).</item>
///   <item><see cref="Timings"/> — the startup milestones timed, for a launch that asked to be profiled.</item>
/// </list>
/// It names no UI toolkit: the listener that hands WPF's binding errors to it is the app's (<see cref="BindingErrorTrace"/>).
/// </para>
/// </summary>
public sealed class ShellLogging : IDisposable
{
    private readonly IReadOnlyList<LogDestination> _destinations;

    /// <param name="crash">Where every fault is written.</param>
    /// <param name="session">The session's own account, kept while safety logging is on.</param>
    /// <param name="others">Any further destinations: the binding errors, the startup profiler.</param>
    public ShellLogging(CrashLogProvider crash, SessionLogProvider session, IEnumerable<LogDestination> others)
    {
        Crash   = crash;
        Session = session;
        _destinations = [crash, session, .. others];
        Timings = _destinations.OfType<StartupTimings>().FirstOrDefault();
        Factory = new LoggerFactory(_destinations);
    }

    /// <summary>
    /// The logs of a launch kept in <paramref name="folders"/>: the crash log in the configuration folder, the session's log under
    /// the local one, the binding errors beside the crash log when <paramref name="uiTest"/>, and the profiler when
    /// <paramref name="profileStartup"/>.
    /// </summary>
    public static ShellLogging Open(AppDataFolders folders, string version, bool uiTest, bool profileStartup)
    {

        Func<DateTimeOffset> clock = () => DateTimeOffset.Now;
        var crash   = new CrashLogProvider(() => folders.BaseDir, clock, version);
        var session = new SessionLogProvider(() => Path.Combine(folders.LocalDir, "logs"), clock, version, CrashLogProvider.FileNameFor);

        List<LogDestination> others = [];
        if (uiTest) others.Add(new BindingErrorLogProvider(() => folders.BaseDir));
        if (profileStartup) others.Add(new StartupTimings());
        return new ShellLogging(crash, session, others);
    }

    /// <summary>Makes every logger: each writes to every destination.</summary>
    public ILoggerFactory Factory { get; }

    /// <summary>The crash log: its file today, and the burst it may still be counting.</summary>
    public CrashLogProvider Crash { get; }

    /// <summary>The session's log: turned on and off with the setting, and the folder the AI host logs in beside it.</summary>
    public SessionLogProvider Session { get; }

    /// <summary>The startup profiler; null unless the launch asked to be profiled.</summary>
    public StartupTimings? Timings { get; }

    /// <summary>Ends the logs: a burst still being counted is written out, the session's file closed. Nothing is written after.</summary>
    public void Dispose()
    {
        Factory.Dispose();   // the factory does not dispose the destinations it was made with
        foreach (var destination in _destinations) destination.Dispose();
    }
}

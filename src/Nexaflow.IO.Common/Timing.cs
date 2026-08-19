using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Nexaflow.IO.Common;

/// <summary>
/// Opt-in scoped stopwatch for work that happens <b>after</b> startup — the counterpart to Core's
/// <c>StartupTimings</c>, which measures process-start to first paint and reports once.
/// <para>
/// Lives here rather than in Core because the things worth timing are mostly in features, and a feature
/// cannot reference Core. Its own switch (<c>NEXAFLOW_TIMING=1</c>) and deliberately <b>not</b> the startup
/// profiler's: <c>NEXAFLOW_STARTUP_TIMING</c> makes the app report and <c>Shutdown()</c> at first render so
/// a cold-start harness can loop, which would kill the process before any of the work measured here has
/// happened. Same rule though: <b>zero cost when disabled</b> — a static bool test and a null return, no
/// allocation, no stopwatch.
/// </para>
/// <para>
/// Set <c>NEXAFLOW_TIMING_LOG</c> to a file path to collect the lines somewhere a harness can read them;
/// otherwise they go to stderr like the startup report. Lines are
/// <c>[Timing] {thread} {name}: {ms}</c>, with the thread because the point of most of these measurements
/// is which side of the dispatcher the time is being spent on.
/// </para>
/// </summary>
public static class Timing
{
    /// <summary>Reads the switch once — this is checked on paths that run per directory entry.</summary>
    public static readonly bool Enabled =
        Environment.GetEnvironmentVariable("NEXAFLOW_TIMING") is "1" or "true";

    private static readonly string? LogPath = Environment.GetEnvironmentVariable("NEXAFLOW_TIMING_LOG");
    private static readonly Lock Sink = new();

    /// <summary>Times a block. <c>using var _ = Timing.Measure("name");</c> — null (and free) when disabled,
    /// which <c>using</c> accepts.</summary>
    public static IDisposable? Measure(string name) => Enabled ? new Scope(name) : null;

    /// <summary>Records a one-off value that isn't a duration — a count, a size, a batch number.</summary>
    public static void Note(string name, string value)
    {
        if (Enabled) Write($"{name}: {value}");
    }

    private static void Write(string line)
    {
        var thread = Thread.CurrentThread.ManagedThreadId;
        var text = $"[Timing] t{thread,-3} {line}";
        lock (Sink)
        {
            if (LogPath is { Length: > 0 } p)
            {
                // Append rather than hold a handle: the harness reads this while the app is still running,
                // and a crashed run should still leave everything measured up to that point.
                try { File.AppendAllText(p, text + Environment.NewLine); return; }
                catch { /* fall through to stderr */ }
            }
            Console.Error.WriteLine(text);
        }
    }

    private sealed class Scope(string name) : IDisposable
    {
        private readonly long _start = Stopwatch.GetTimestamp();

        public void Dispose() =>
            Write($"{name}: {Stopwatch.GetElapsedTime(_start).TotalMilliseconds:F1} ms");
    }
}

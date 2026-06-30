using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Nexaflow.Core.Services;

/// <summary>
/// Opt-in startup profiler. Measures wall-clock from OS process creation to the first window's
/// <c>ContentRendered</c>, with a coarse milestone breakdown and the loaded-assembly count (the headline
/// signal for the lazy-feature-loading work). Enabled by the <c>--timing</c> command-line flag or the
/// <c>NEXAFLOW_STARTUP_TIMING=1</c> environment variable; zero cost when disabled. Writes to <b>stderr</b>
/// so a release run shows the numbers without a debugger attached (unlike <see cref="Debug.WriteLine"/>).
/// </summary>
public static class StartupTimings
{
    public static bool Enabled { get; set; }

    private static readonly DateTime _processStart = SafeProcessStart();
    private static readonly List<(string Name, double Ms)> _marks = [];
    private static readonly object _lock = new();
    private static bool _reported;
    private static DateTime? _windowRequestedAt;   // set when a --prestart daemon is asked to open a window

    /// <summary>Marks the moment a window is requested on-demand from a warmed daemon (the IPC "open a
    /// window" signal). Lets <see cref="Report"/> emit the daemon's real click→window cost, separate from
    /// the cold process-start-to-first-window number.</summary>
    public static void MarkWindowRequested()
    {
        if (!Enabled) return;
        lock (_lock) { _windowRequestedAt = DateTime.Now; _reported = false; }
        Mark("WindowRequested");
    }

    private static DateTime SafeProcessStart()
    {
        try { return Process.GetCurrentProcess().StartTime; }
        catch { return DateTime.Now; }
    }

    /// <summary>Records a named milestone (ms since process start). No-op when disabled.</summary>
    public static void Mark(string name)
    {
        if (!Enabled) return;
        lock (_lock) _marks.Add((name, (DateTime.Now - _processStart).TotalMilliseconds));
    }

    /// <summary>Emits the milestone table, assembly count and the headline <c>FIRST_WINDOW_MS</c> line to
    /// stderr. Idempotent — only the first call reports. No-op when disabled.</summary>
    public static void Report()
    {
        if (!Enabled) return;
        lock (_lock)
        {
            if (_reported) return;
            _reported = true;
            _marks.Add(("ContentRendered", (DateTime.Now - _processStart).TotalMilliseconds));

            var total = _marks[^1].Ms;
            var sb = new StringBuilder();
            sb.AppendLine("[StartupTimings] milestones (ms from process start, +delta):");
            double prev = 0;
            foreach (var (name, ms) in _marks)
            {
                sb.AppendLine($"  {ms,8:F0}  (+{ms - prev,6:F0})  {name}");
                prev = ms;
            }
            sb.AppendLine($"[StartupTimings] assemblies loaded at first window: {AppDomain.CurrentDomain.GetAssemblies().Length}");
            if (_windowRequestedAt is { } req)
                sb.AppendLine($"WINDOW_ON_DEMAND_MS={(DateTime.Now - req).TotalMilliseconds:F0}");
            sb.Append($"FIRST_WINDOW_MS={total:F0}");
            Console.Error.WriteLine(sb.ToString());
        }
    }
}

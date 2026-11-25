using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Nexaflow.Session.Services;

/// <summary>
/// Opt-in startup profiler: a destination of the session's logs (<see cref="ShellLogging"/>), there only when the launch asked for it
/// — the <c>--timing</c> command-line flag or the <c>NEXAFLOW_STARTUP_TIMING=1</c> environment variable. It times each startup
/// milestone the shell logs (<see cref="StartupMilestones"/>) from OS process creation, and <see cref="Report"/> gives the wall-clock
/// to the first window's <c>ContentRendered</c>, with the milestone breakdown and the loaded-assembly count (the headline signal for
/// the lazy-feature-loading work). Writes to <b>stderr</b> so a release run shows the numbers without a debugger attached.
/// </summary>
public sealed class StartupTimings : LogDestination
{
    private readonly DateTime _processStart = SafeProcessStart();
    private readonly List<(string Name, double Ms)> _marks = [];
    private readonly Lock _gate = new();
    private bool _reported;
    private DateTime? _windowRequestedAt;   // set when a --prestart daemon is asked to open a window

    private static DateTime SafeProcessStart()
    {
        try { return Process.GetCurrentProcess().StartTime; }
        catch { return DateTime.Now; }
    }

    protected override bool Takes(string category, LogLevel level) => level == LogLevel.Information;

    /// <summary>
    /// Records a milestone (ms since process start). A window asked of a warmed daemon also starts the click→window clock
    /// <see cref="Report"/> gives apart from the cold process-start-to-first-window number, and lets it report again.
    /// </summary>
    protected override void Write(in ShellLogEntry entry)
    {
        var requested = entry.Id == StartupMilestones.WindowRequested;
        if (!requested && entry.Id != StartupMilestones.Milestone) return;

        var name = entry.Message;
        if (entry.Fields is { } fields)
            foreach (var (key, value) in fields)
                if (key == StartupMilestones.NameField && value is string named) name = named;

        var now = DateTime.Now;
        lock (_gate)
        {
            if (requested)
            {
                _windowRequestedAt = now;
                _reported = false;
            }
            _marks.Add((name, (now - _processStart).TotalMilliseconds));
        }
    }

    /// <summary>Emits the milestone table, assembly count and the headline <c>FIRST_WINDOW_MS</c> line to stderr. Only the first
    /// call after a launch or a window request reports.</summary>
    public void Report()
    {
        lock (_gate)
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

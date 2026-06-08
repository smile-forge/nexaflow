using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>gantt</c> charts.
///
/// Supported:
///   • <c>title</c>, <c>dateFormat</c>, <c>axisFormat</c>, <c>section</c>.
///   • Task lines <c>&lt;name&gt; : [tags,] [id,] [start,] (duration | end | until)</c> where
///     tags are <c>done</c>/<c>active</c>/<c>crit</c>/<c>milestone</c>, start is a date or
///     <c>after &lt;ids&gt;</c> (or implicit = after the previous task), and the end is a
///     duration (<c>30d</c>, <c>2w</c>, <c>12h</c>…), an explicit date, or <c>until &lt;ids&gt;</c>.
///   • <c>%%</c> comments.
///
/// Not yet modelled: <c>excludes</c>/weekends (durations are raw calendar time) and
/// <c>tickInterval</c> (the renderer picks its own ticks).
/// </summary>
public sealed class MermaidGanttParser
{
    public bool CanParse(string language) =>
        language.Equals("gantt", StringComparison.OrdinalIgnoreCase);

    public GanttChart Parse(string source)
    {
        var chart = new GanttChart();
        try { ParseInto(source, chart); }
        catch { /* never throw; return partial chart */ }
        return chart;
    }

    private static readonly Regex RxDur =
        new(@"^(?<n>\d+(?:\.\d+)?)(?<u>ms|s|m|h|d|w)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private sealed class Raw
    {
        public string Name = "", Section = "";
        public string? Id;
        public GanttTaskState State = GanttTaskState.Default;
        public bool Crit, Milestone;
        public DateTime? StartDate, EndDate;
        public TimeSpan? Duration;
        public List<string> After = [], Until = [];
    }

    private static void ParseInto(string source, GanttChart chart)
    {
        var raws = new List<Raw>();
        string section = string.Empty;

        foreach (var rawLine in source.Split('\n'))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            var lower = line.ToLowerInvariant();
            if (lower == "gantt") continue;

            if (lower.StartsWith("title "))       { chart.Title      = line[6..].Trim(); continue; }
            if (lower.StartsWith("dateformat"))   { chart.DateFormat = AfterKeyword(line); continue; }
            if (lower.StartsWith("axisformat"))   { chart.AxisFormat = AfterKeyword(line); continue; }
            if (lower.StartsWith("section "))     { section = line[8..].Trim(); continue; }
            if (lower.StartsWith("excludes") || lower.StartsWith("includes") || lower.StartsWith("todaymarker")
                || lower.StartsWith("tickinterval") || lower.StartsWith("weekday") || lower.StartsWith("weekend"))
                continue;

            int colon = line.IndexOf(':');
            if (colon < 0) continue;                 // not a task line
            string name = line[..colon].Trim();
            if (name.Length == 0) continue;

            var raw = new Raw { Name = name, Section = section };
            ParseSpec(line[(colon + 1)..].Trim(), chart.DateFormat, raw);
            raws.Add(raw);
        }

        Resolve(raws, chart);
    }

    // ── Task spec ────────────────────────────────────────────────────────────

    private static void ParseSpec(string spec, string fmt, Raw raw)
    {
        foreach (var token in spec.Split(','))
        {
            var t = token.Trim();
            if (t.Length == 0) continue;
            var tl = t.ToLowerInvariant();

            switch (tl)
            {
                case "done":      raw.State = GanttTaskState.Done;   continue;
                case "active":    raw.State = GanttTaskState.Active; continue;
                case "crit":      raw.Crit = true;                   continue;
                case "milestone": raw.Milestone = true;              continue;
            }
            if (tl.StartsWith("after ")) { raw.After.AddRange(t[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries)); continue; }
            if (tl.StartsWith("until ")) { raw.Until.AddRange(t[6..].Split(' ', StringSplitOptions.RemoveEmptyEntries)); continue; }
            if (RxDur.IsMatch(t))        { raw.Duration = ParseDuration(t); continue; }

            if (TryParseDate(t, fmt, out var d))
            {
                if (raw.StartDate is null && raw.After.Count == 0) raw.StartDate = d;
                else                                               raw.EndDate   = d;
                continue;
            }

            raw.Id ??= t;   // first leftover token is the task id
        }
    }

    private static void Resolve(List<Raw> raws, GanttChart chart)
    {
        DateTime baseDate = raws.Where(r => r.StartDate.HasValue)
                                .Select(r => r.StartDate!.Value)
                                .DefaultIfEmpty(DateTime.Today).Min();

        var startById = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var endById   = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var sectionMap = new Dictionary<string, GanttSection>(StringComparer.Ordinal);
        DateTime prevEnd = baseDate;

        GanttSection SectionFor(string name)
        {
            if (!sectionMap.TryGetValue(name, out var s))
            {
                s = new GanttSection { Name = name };
                sectionMap[name] = s;
                chart.Sections.Add(s);
            }
            return s;
        }

        foreach (var r in raws)
        {
            DateTime start =
                r.StartDate.HasValue ? r.StartDate.Value
              : r.After.Count > 0    ? r.After.Select(id => endById.GetValueOrDefault(id, prevEnd)).Max()
              :                        prevEnd;

            DateTime end =
                r.Until.Count > 0     ? r.Until.Select(id => startById.GetValueOrDefault(id, start)).Min()
              : r.EndDate.HasValue    ? r.EndDate.Value
              : r.Duration.HasValue   ? start + r.Duration.Value
              : r.Milestone           ? start
              :                         start.AddDays(1);

            if (end < start) end = start;

            var task = new GanttTask
            {
                Name = r.Name, Id = r.Id, Start = start, End = end,
                State = r.State, Critical = r.Crit, IsMilestone = r.Milestone,
            };
            SectionFor(r.Section).Tasks.Add(task);

            if (r.Id is not null) { startById[r.Id] = start; endById[r.Id] = end; }
            prevEnd = end;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TimeSpan ParseDuration(string t)
    {
        var m = RxDur.Match(t);
        double n = double.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture);
        return m.Groups["u"].Value.ToLowerInvariant() switch
        {
            "w"  => TimeSpan.FromDays(7 * n),
            "d"  => TimeSpan.FromDays(n),
            "h"  => TimeSpan.FromHours(n),
            "m"  => TimeSpan.FromMinutes(n),
            "s"  => TimeSpan.FromSeconds(n),
            "ms" => TimeSpan.FromMilliseconds(n),
            _    => TimeSpan.FromDays(n),
        };
    }

    /// <summary>Parses a date by the chart's day.js-style format, falling back to a loose parse.</summary>
    private static bool TryParseDate(string s, string fmt, out DateTime dt)
    {
        string net = ConvertDateFormat(fmt);
        if (DateTime.TryParseExact(s, net, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return true;
        return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt);
    }

    /// <summary>Maps day.js tokens (YYYY, DD…) to .NET ones (yyyy, dd…); MM/HH/mm/ss already match.</summary>
    private static string ConvertDateFormat(string fmt) =>
        fmt.Replace("YYYY", "yyyy").Replace("YY", "yy").Replace("DD", "dd").Replace("D", "d");

    private static string StripComment(string line)
    {
        int idx = line.IndexOf("%%", StringComparison.Ordinal);
        return idx >= 0 ? line[..idx] : line;
    }

    private static string AfterKeyword(string line)
    {
        int sp = line.IndexOfAny([' ', '\t']);
        return sp < 0 ? string.Empty : line[(sp + 1)..].Trim();
    }
}

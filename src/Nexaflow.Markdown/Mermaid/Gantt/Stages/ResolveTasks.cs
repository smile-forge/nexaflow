using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Gantt.Stages;

/// <summary>
/// Says when each task runs (<see cref="GanttTaskNode"/>), and what the lines setting something for the whole chart mean
/// (<see cref="GanttBlockNode"/>) — neither of which one line says alone.
///
/// <para>
/// A task's schedule means something only on its chart, and on a day. A task starts when its start says — a date in the chart's
/// <c>dateFormat</c>, or when the last of the tasks <c>after</c> names ends — or else when the task written before it ends; and
/// it ends when its end says: a date, a length of time from its start, or when the first of the tasks <c>until</c> names starts.
/// An id no task has means today, and a time of day means that time today. Days <c>excludes</c> names (less those
/// <c>includes</c> names) push a length's end on by as many as fall in it. Tasks waiting on each other are read over as many
/// passes as it takes, up to Mermaid's ten; a task whose schedule never comes to a start and an end — its start no date, its
/// schedule saying nothing — means no time at all, and is left as written.
/// </para>
/// <para>
/// A setting means what the last line writing it says, else what the front matter says, else Mermaid's. Today is the day the
/// block is read.
/// </para>
/// </summary>
/// <param name="config">What the front matter asks for, which the block carries for its builder.</param>
public sealed class ResolveTasks(GanttConfig config) : IAstStage
{
    public string Name => "gantt:tasks";

    public ContentNode Run(ContentNode tree)
    {
        var now = DateTime.Now;
        var chart = new Chart();
        var tasks = new List<Scheduled>();
        var unnamed = 0;

        foreach (var node in tree.SelfAndDescendants())
        {
            switch (node.Kind)
            {
                case GanttKinds.Setting: chart.Set(node); break;
                case GanttKinds.Flag: chart.Flag(node); break;
                case GanttKinds.Task: tasks.Add(Scheduled.Of(node, tasks.LastOrDefault(), ref unnamed)); break;
                case GanttKinds.Click: chart.Click(node); break;
            }
        }

        var days = new GanttDays(chart.DateFormat, chart.Excludes, chart.Includes, chart.WeekendStartsFriday);
        Schedule(tasks, days, chart.InclusiveEndDates, now.Date);

        // A tree is rewritten in the order it is written, so the tasks come in the order they were read.
        var at = 0;
        tree = AstRewrite.Each(tree, node =>
        {
            if (node.Kind != GanttKinds.Task || node is GanttTaskNode) return node;

            var task = tasks[at++];
            return task is { Start: { } start, End: { } end }
                ? new GanttTaskNode(node, start, end, task.Shown ?? end, chart.Links.GetValueOrDefault(task.Id), chart.Clicked.Contains(task.Id))
                : node;
        });

        return new GanttBlockNode(
            tree, config, days, now,
            chart.AxisFormat ?? config.AxisFormat ?? (chart.DateFormat == "D" ? "%d" : "%Y-%m-%d"),
            GanttTick.Read(chart.TickInterval ?? config.TickInterval),
            chart.Marker,
            chart.Weekday ?? (Enum.TryParse<DayOfWeek>(config.Weekday, ignoreCase: true, out var weekday) ? weekday : DayOfWeek.Sunday),
            chart.TopAxis || config.TopAxis);
    }

    /// <summary>Gives every task it can a start and an end, over as many passes as the tasks waiting on each other take.</summary>
    private static void Schedule(List<Scheduled> tasks, GanttDays days, bool inclusive, DateTime today)
    {
        var byId = new Dictionary<string, Scheduled>(StringComparer.Ordinal);
        foreach (var task in tasks) byId[task.Id] = task;

        for (var pass = 0; pass <= 10 && tasks.Any(task => task.End is null && task.Workable); pass++)
            foreach (var task in tasks.Where(task => task.End is null && task.Workable))
                Schedule(task, byId, days, inclusive, today);
    }

    private static void Schedule(Scheduled task, Dictionary<string, Scheduled> byId, GanttDays days, bool inclusive, DateTime today)
    {
        var format = days.DateFormat;

        DateTime? start;
        if (task.After is { } after)
        {
            var known = after.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
            if (known.Any(other => other.End is null)) return;
            start = known.Count == 0 ? today : known.Max(other => other.End);
        }
        else if (task.StartText is { } text)
        {
            start = (format is "x" or "X" && text.All(char.IsAsciiDigit) && text.Length > 0 ? (DateTime?)MermaidDate.FromMilliseconds(double.Parse(text, CultureInfo.InvariantCulture)) : null)
                    ?? MermaidDate.Read(text, format, today) ?? (ResolveSchedule.Starts(text, format) ? MermaidDate.Loosely(text) : null);
            if (start is null) { task.Workable = false; return; }
        }
        else
        {
            if (task.Previous is not { } previous) { task.Workable = false; return; }
            if (previous.End is null) { if (!previous.Workable) task.Workable = false; return; }
            start = previous.End;
        }

        DateTime end;
        if (task.Until is { } until)
        {
            var known = until.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
            if (known.Any(other => other.Start is null)) { task.Start = start; return; }
            end = known.Count == 0 ? today : known.Min(other => other.Start!.Value);
        }
        else if (MermaidDate.Read(task.EndText, format, today) is { } date)
        {
            end = inclusive ? date.AddDays(1) : date;
        }
        else
        {
            end = MermaidDuration.Read(task.EndText) is { } length ? MermaidDuration.After(start.Value, length) : start.Value;
        }

        task.Start = start;
        task.End = end;

        // Excluded days in a task push its end on, unless its end is a date.
        if (days.Excludes.Count == 0 || MermaidDate.Read(task.EndText, MermaidDate.Default) is not null) return;

        DateTime? shown = null;
        var excluded = false;
        var limit = end.AddDays(10_000);
        for (var at = start.Value.AddDays(1); at <= end && end < limit; at = at.AddDays(1))
        {
            if (!excluded) shown = end;
            excluded = days.Excluded(at);
            if (excluded) end = end.AddDays(1);
        }

        task.End = end;
        task.Shown = shown;
    }

    /// <summary>What the lines setting something for the whole chart say, the last of each written holding.</summary>
    private sealed class Chart
    {
        public string DateFormat = MermaidDate.Default;
        public string? AxisFormat, TickInterval;
        public GanttToday Marker = GanttToday.Default;
        public DayOfWeek? Weekday;
        public bool WeekendStartsFriday, InclusiveEndDates, TopAxis;
        public readonly List<string> Excludes = [], Includes = [];
        public readonly HashSet<string> Clicked = new(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Links = new(StringComparer.Ordinal);

        public void Set(ContentNode line)
        {
            var value = line.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Setting);
            var said = value?.Text.Trim() ?? string.Empty;
            if (said.Length == 0) return;

            switch (value!.Role)
            {
                case var role when role == GanttRoles.Of("dateFormat"): DateFormat = said; break;
                case var role when role == GanttRoles.Of("axisFormat"): AxisFormat = said; break;
                case var role when role == GanttRoles.Of("tickInterval"): TickInterval = said; break;
                case var role when role == GanttRoles.Of("todayMarker"): Marker = GanttToday.Read(said); break;
                case var role when role == GanttRoles.Of("weekday") && value.Trouble is null: Weekday = Enum.Parse<DayOfWeek>(said, ignoreCase: true); break;
                case var role when role == GanttRoles.Of("weekend") && value.Trouble is null: WeekendStartsFriday = said.Equals("friday", StringComparison.OrdinalIgnoreCase); break;
                case var role when role == GanttRoles.Of("excludes"): Merge(Excludes, said); break;
                case var role when role == GanttRoles.Of("includes"): Merge(Includes, said); break;
            }
        }

        public void Flag(ContentNode line)
        {
            var flag = line.Children.FirstOrDefault()?.Text ?? string.Empty;
            if (flag.Equals("inclusiveEndDates", StringComparison.OrdinalIgnoreCase)) InclusiveEndDates = true;
            if (flag.Equals("topAxis", StringComparison.OrdinalIgnoreCase)) TopAxis = true;
        }

        public void Click(ContentNode line)
        {
            var link = line.Inner(GanttKinds.Link).Words()?.Text;
            foreach (var id in line.Children.Where(child => child.Kind == MermaidKinds.Name).Select(Said))
            {
                Clicked.Add(id);
                if (link is not null) Links[id] = MermaidText.Decode(link);
            }
        }

        private static void Merge(List<string> into, string said)
        {
            foreach (var token in said.ToLowerInvariant().Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries))
                if (!into.Contains(token)) into.Add(token);
        }
    }

    /// <summary>A task as written, and how far giving it a start and an end has got.</summary>
    private sealed class Scheduled
    {
        public required string Id { get; init; }
        public Scheduled? Previous { get; init; }
        public IReadOnlyList<string>? After { get; init; }
        public string? StartText { get; init; }
        public IReadOnlyList<string>? Until { get; init; }
        public string EndText { get; init; } = string.Empty;
        public bool Workable = true;
        public DateTime? Start, End, Shown;

        public static Scheduled Of(ContentNode line, Scheduled? previous, ref int unnamed)
        {
            var schedule = line.Children.FirstOrDefault(child => child.Kind == GanttKinds.Schedule);
            var items = schedule?.Children ?? [];
            var id = items.FirstOrDefault(child => child is { Kind: MermaidKinds.Name, Role: GanttRoles.Id }) is { } named ? Said(named) : string.Empty;
            var start = items.FirstOrDefault(child => child.Role == GanttRoles.Start);
            var end = items.FirstOrDefault(child => child.Role == GanttRoles.End);

            return new Scheduled
            {
                Id = id.Length > 0 ? id : "task" + ++unnamed,
                Previous = previous,
                After = start?.Kind == GanttKinds.After ? References(start) : null,
                StartText = start?.Kind == GanttKinds.When ? start.Words()?.Text.Trim() : null,
                Until = end?.Kind == GanttKinds.Until ? References(end) : null,
                EndText = end?.Kind == GanttKinds.When ? end.Words()?.Text.Trim() ?? string.Empty : string.Empty,
                Workable = end is not null && schedule!.Children.All(child => child.Role != GanttRoles.Extra),
            };
        }

        private static IReadOnlyList<string> References(ContentNode item) =>
            [.. item.Children.Where(child => child.Kind == MermaidKinds.Name).Select(Said).Where(id => id.Length > 0)];
    }

    /// <summary>What a name says: its words, less the space round them.</summary>
    private static string Said(ContentNode name) => name.Words()?.Text.Trim() ?? string.Empty;
}

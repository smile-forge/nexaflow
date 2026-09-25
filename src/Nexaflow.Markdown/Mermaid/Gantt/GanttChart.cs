using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Gantt.Stages;

namespace Nexaflow.Markdown.Mermaid.Gantt;

/// <summary>A task, worked out: what it is written as, where it sits, and when it runs.</summary>
/// <param name="Part">The task as written — what pressing it means.</param>
/// <param name="Name">What its name says, or null where it is still to write.</param>
/// <param name="Hole">The hole standing where its name is still to write.</param>
/// <param name="Section">The section it is written under — null for none.</param>
/// <param name="Order">Its row: the order it is written in among the tasks that take one, or the row it shares in compact mode.</param>
/// <param name="Shown">Where its bar is drawn to — short of <paramref name="End"/> where excluded days at its end push that on.</param>
/// <param name="Link">Where a <c>click … href</c> for it points, if one does.</param>
/// <param name="Clickable">Whether a <c>click</c> names it.</param>
public sealed record GanttTask(
    ContentPart Part, ContentPart? Name, ContentPart? Hole, GanttSection? Section, string Id,
    bool Active, bool Done, bool Critical, bool Milestone, bool Vert,
    int Order, DateTime Start, DateTime End, DateTime Shown, string? Link, bool Clickable)
{
    /// <summary>What its name says.</summary>
    public string Says => Name?.Text ?? string.Empty;
}

/// <summary>A section: what its name says, where it is written, and the hole standing where its name is still to write.</summary>
public sealed record GanttSection(string Says, ContentPart? Name, ContentPart? Hole, int Index);

/// <summary>How far apart a gantt chart's axis marks are: every so many of one of the units Mermaid counts in.</summary>
public sealed record GanttTick(int Every, string Unit)
{
    /// <summary>What a <c>tickInterval</c> says — <c>1week</c> — or null where it is not a whole number and a unit Mermaid counts in.</summary>
    public static GanttTick? Read(string? written)
    {
        if (written?.Trim() is not { Length: > 0 } said) return null;

        var digits = said.TakeWhile(char.IsAsciiDigit).Count();
        var unit = said[digits..].Trim();

        return digits > 0 && MermaidNumber.Read(said[..digits]) is { } every && every > 0 && GanttGrammar.Intervals.Contains(unit)
            ? new GanttTick((int)every, unit)
            : null;
    }
}

/// <summary>How the line at today is drawn, as <c>todayMarker</c> styles it: not at all, or in its own stroke, width, opacity and dashes.</summary>
public sealed record GanttToday(bool Off, string? Stroke, double? Width, double? Opacity, IReadOnlyList<double>? Dashes)
{
    public static GanttToday Default { get; } = new(false, null, null, null, null);

    /// <summary>What a <c>todayMarker</c> says — <c>off</c>, or CSS such as <c>stroke-width:5px,stroke:#0f0,opacity:0.5</c>.</summary>
    public static GanttToday Read(string? written)
    {
        if (written is null) return Default;
        if (written.Trim().Equals("off", StringComparison.OrdinalIgnoreCase)) return Default with { Off = true };

        var today = Default;
        foreach (var style in written.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (style.Split(':', 2) is not [var key, var value]) continue;

            value = value.Trim();
            today = key.Trim().ToLowerInvariant() switch
            {
                "stroke" => today with { Stroke = value },
                "stroke-width" => today with { Width = MermaidNumber.Pixels(value) ?? today.Width },
                "opacity" => today with { Opacity = MermaidNumber.Read(value) is { } opacity ? Math.Clamp(opacity, 0, 1) : today.Opacity },
                "stroke-dasharray" => today with { Dashes = Dashed(value) },
                _ => today,
            };
        }

        return today;
    }

    private static IReadOnlyList<double>? Dashed(string value)
    {
        var dashes = value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(MermaidNumber.Pixels).ToList();
        return dashes.Count > 0 && dashes.All(dash => dash is > 0) ? dashes.Select(dash => dash!.Value).ToList() : null;
    }
}

/// <summary>
/// A <c>gantt</c> block, read and worked out as Mermaid works it out: its settings, its sections, and every task's start and end.
///
/// <para>
/// A task starts when its start says — a date in the chart's <c>dateFormat</c>, or the latest end of the tasks <c>after</c> names —
/// or else when the task written before it ends; and it ends when its end says: a date, a length of time from its start, or the
/// earliest start of the tasks <c>until</c> names. An id no task has stands for today. Days <c>excludes</c> names (less those
/// <c>includes</c> names) push a task's end on by as many as fall in it, unless its end is a date. Tasks waiting on each other are
/// worked out over as many passes as it takes, up to Mermaid's ten; a task never worked out — its start no date, its schedule
/// saying nothing — is not drawn.
/// </para>
/// </summary>
public sealed class GanttChart
{
    private GanttChart(MermaidBlock block, GanttConfig config) => (Block, Config) = (block, config);

    

    public static GanttChart Of(ContentNode tree, DateTime? today = null) => Of(MermaidBlock.Of(tree), today);

    public static GanttChart Of(MermaidBlock block, DateTime? today = null)
    {
        var chart = new GanttChart(block, GanttConfig.Read(block.Config));
        var root = block.Reading.Root;
        var day = (today ?? DateTime.Now).Date;

        var excludes = new List<string>();
        var includes = new List<string>();
        var raws = new List<Raw>();
        var sections = new List<GanttSection>();
        var named = new Dictionary<string, GanttSection>(StringComparer.Ordinal);
        GanttSection? section = null;
        var unnamed = 0;

        foreach (var part in root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case GanttKinds.Setting:
                    var value = part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Setting);
                    var said = value?.Text.Trim() ?? string.Empty;
                    if (said.Length == 0) break;

                    switch (value!.Role)
                    {
                        case var role when role == GanttRoles.Of("dateFormat"): chart.DateFormat = said; break;
                        case var role when role == GanttRoles.Of("axisFormat"): chart.AxisFormat = said; break;
                        case var role when role == GanttRoles.Of("tickInterval"): chart._tickInterval = said; break;
                        case var role when role == GanttRoles.Of("todayMarker"): chart.Today = GanttToday.Read(said); break;
                        case var role when role == GanttRoles.Of("weekday") && value.Trouble is null: chart.Weekday = Enum.Parse<DayOfWeek>(said, ignoreCase: true); break;
                        case var role when role == GanttRoles.Of("weekend") && value.Trouble is null: chart.WeekendStartsFriday = said.Equals("friday", StringComparison.OrdinalIgnoreCase); break;
                        case var role when role == GanttRoles.Of("excludes"): Merge(excludes, said); break;
                        case var role when role == GanttRoles.Of("includes"): Merge(includes, said); break;
                    }

                    break;

                case GanttKinds.Flag:
                    var flag = part.Children.FirstOrDefault()?.Text ?? string.Empty;
                    if (flag.Equals("inclusiveEndDates", StringComparison.OrdinalIgnoreCase)) chart.InclusiveEndDates = true;
                    if (flag.Equals("topAxis", StringComparison.OrdinalIgnoreCase)) chart.TopAxis = true;
                    break;

                case GanttKinds.Section:
                    var name = part.Children.FirstOrDefault(child => child.Kind == GanttKinds.Text);
                    var says = name?.Words()?.Text ?? string.Empty;
                    if (!named.TryGetValue(says, out section))
                    {
                        section = new GanttSection(says, name?.Words(), name.Hole(), sections.Count);
                        named[says] = section;
                        sections.Add(section);
                    }
                    break;

                case GanttKinds.Task:
                    raws.Add(Raw.Of(part, section, raws.LastOrDefault(), ref unnamed));
                    break;

                case GanttKinds.Click:
                    var link = part.Inner(GanttKinds.Link)?.Words()?.Text;
                    foreach (var id in part.Children.Where(child => child.Kind == MermaidKinds.Name).Select(child => child.Words()?.Text ?? string.Empty))
                    {
                        chart._clicked.Add(id);
                        if (link is not null) chart._links[id] = MermaidText.Decode(link);
                    }

                    break;
            }
        }

        chart.Excludes = excludes;
        chart.Includes = includes;
        chart.Tasks = chart.Worked(raws, day, sections);

        // A section keeps its place only where a task in it takes a row.
        var rowed = new HashSet<GanttSection>(ReferenceEqualityComparer.Instance);
        foreach (var task in chart.Tasks)
            if (!task.Vert && task.Section is { } holding) rowed.Add(holding);

        chart.Sections = [.. sections.Where(rowed.Contains)];
        return chart;
    }

    public MermaidBlock Block { get; }

    public GanttConfig Config { get; }

    /// <summary>The format dates are written in: the last <c>dateFormat</c> written, or <see cref="MermaidDate.Default"/>.</summary>
    public string DateFormat { get; private set; } = MermaidDate.Default;

    /// <summary>The format the axis writes dates in: the last <c>axisFormat</c> written, the front matter's, or Mermaid's.</summary>
    public string AxisFormat
    {
        get => _axisFormat ?? Config.AxisFormat ?? (DateFormat == "D" ? "%d" : "%Y-%m-%d");
        private set => _axisFormat = value;
    }

    private string? _axisFormat;

    private string? _tickInterval;

    /// <summary>How the line marking today is drawn, as the last <c>todayMarker</c> written styles it.</summary>
    public GanttToday Today { get; private set; } = GanttToday.Default;

    /// <summary>
    /// How far apart the axis's marks are, as the last <c>tickInterval</c> written — or the front matter's — says; null to
    /// choose, where neither says anything Mermaid counts in.
    /// </summary>
    public GanttTick? Tick => GanttTick.Read(_tickInterval ?? Config.TickInterval);

    /// <summary>The day a week starts on, for marks a week apart.</summary>
    public DayOfWeek Weekday
    {
        get => _weekday ?? (Enum.TryParse<DayOfWeek>(Config.Weekday, ignoreCase: true, out var day) ? day : DayOfWeek.Sunday);
        private set => _weekday = value;
    }

    private DayOfWeek? _weekday;

    /// <summary>Whether <c>weekends</c> are Friday and Saturday rather than Saturday and Sunday.</summary>
    public bool WeekendStartsFriday { get; private set; }

    public bool InclusiveEndDates { get; private set; }

    /// <summary>Whether the axis's dates are written over the chart as well as under it.</summary>
    public bool TopAxis
    {
        get => _topAxis || Config.TopAxis;
        private set => _topAxis = value;
    }

    private bool _topAxis;

    /// <summary>What <c>excludes</c> and <c>includes</c> name, in lower case: dates, days of the week, <c>weekends</c>.</summary>
    public IReadOnlyList<string> Excludes { get; private set; } = [];
    public IReadOnlyList<string> Includes { get; private set; } = [];

    /// <summary>The sections, in the order they are written, that hold a task with a row.</summary>
    public IReadOnlyList<GanttSection> Sections { get; private set; } = [];

    /// <summary>The tasks worked out, in the order they are written.</summary>
    public IReadOnlyList<GanttTask> Tasks { get; private set; } = [];

    /// <summary>Whether a day is left out of a task's length: named by <c>excludes</c> — as a date, a day of the week, or a weekend day — and not by <c>includes</c>.</summary>
    public bool Excluded(DateTime date)
    {
        var (formatted, plain) = (MermaidDate.Write(date, DateFormat), MermaidDate.Write(date, MermaidDate.Default));
        if (Includes.Contains(formatted.ToLowerInvariant()) || Includes.Contains(plain)) return false;

        var weekend = WeekendStartsFriday ? (DayOfWeek.Friday, DayOfWeek.Saturday) : (DayOfWeek.Saturday, DayOfWeek.Sunday);
        if (Excludes.Contains("weekends") && (date.DayOfWeek == weekend.Item1 || date.DayOfWeek == weekend.Item2)) return true;

        return Excludes.Contains(date.DayOfWeek.ToString().ToLowerInvariant()) || Excludes.Contains(formatted.ToLowerInvariant()) || Excludes.Contains(plain);
    }

    private readonly HashSet<string> _clicked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _links = new(StringComparer.Ordinal);

    // ── Working it out ──────────────────────────────────────────────────────

    private List<GanttTask> Worked(List<Raw> raws, DateTime today, IReadOnlyList<GanttSection> sections)
    {
        var byId = new Dictionary<string, Raw>(StringComparer.Ordinal);
        foreach (var raw in raws) byId[raw.Id] = raw;

        for (var pass = 0; pass <= 10 && raws.Any(raw => raw.End is null && raw.Workable); pass++)
            foreach (var raw in raws.Where(raw => raw.End is null && raw.Workable))
                Work(raw, byId, today);

        var worked = raws.Where(raw => raw.End is not null).ToList();
        var order = 0;
        var tasks = worked.Select(raw => new GanttTask(
            raw.Part, raw.Name, raw.Hole, raw.Section, raw.Id, raw.Active, raw.Done, raw.Critical, raw.Milestone, raw.Vert,
            raw.Vert ? -1 : order++, raw.Start!.Value, raw.End!.Value, raw.Shown ?? raw.End!.Value,
            _links.GetValueOrDefault(raw.Id), _clicked.Contains(raw.Id))).ToList();

        return Config.Compact ? Compacted(tasks, sections) : tasks;
    }

    private void Work(Raw raw, Dictionary<string, Raw> byId, DateTime today)
    {
        DateTime? start;
        if (raw.After is { } after)
        {
            var known = after.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
            if (known.Any(task => task.End is null)) return;
            start = known.Count == 0 ? today : known.Max(task => task.End);
        }
        else if (raw.StartText is { } text)
        {
            start = (DateFormat is "x" or "X" && text.All(char.IsAsciiDigit) && text.Length > 0 ? (DateTime?)MermaidDate.FromMilliseconds(double.Parse(text, System.Globalization.CultureInfo.InvariantCulture)) : null)
                    ?? MermaidDate.Read(text, DateFormat, today) ?? (ResolveSchedule.Starts(text, DateFormat) ? MermaidDate.Loosely(text) : null);
            if (start is null) { raw.Workable = false; return; }
        }
        else
        {
            if (raw.Previous is not { } previous) { raw.Workable = false; return; }
            if (previous.End is null) { if (!previous.Workable) raw.Workable = false; return; }
            start = previous.End;
        }

        DateTime end;
        if (raw.Until is { } until)
        {
            var known = until.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
            if (known.Any(task => task.Start is null)) { raw.Start = start; return; }
            end = known.Count == 0 ? today : known.Min(task => task.Start!.Value);
        }
        else if (MermaidDate.Read(raw.EndText, DateFormat, today) is { } date)
        {
            end = InclusiveEndDates ? date.AddDays(1) : date;
        }
        else
        {
            end = MermaidDuration.Read(raw.EndText) is { } length ? MermaidDuration.After(start.Value, length) : start.Value;
        }

        raw.Start = start;
        raw.End = end;

        // Excluded days in a task push its end on, unless its end is a date.
        if (Excludes.Count == 0 || MermaidDate.Read(raw.EndText, MermaidDate.Default) is not null) return;

        DateTime? shown = null;
        var excluded = false;
        var limit = end.AddDays(10_000);
        for (var at = start.Value.AddDays(1); at <= end && end < limit; at = at.AddDays(1))
        {
            if (!excluded) shown = end;
            excluded = Excluded(at);
            if (excluded) end = end.AddDays(1);
        }

        raw.End = end;
        raw.Shown = shown;
    }

    /// <summary>Tasks sharing rows, as compact mode draws them: each section's tasks by start, each in the first row it fits.</summary>
    private static List<GanttTask> Compacted(List<GanttTask> tasks, IReadOnlyList<GanttSection> sections)
    {
        var placed = tasks.ToDictionary(task => task, task => task.Order);
        var offset = 0;

        foreach (var group in tasks.Where(task => !task.Vert).GroupBy(task => task.Section))
        {
            var rows = new List<DateTime>();
            foreach (var task in group.OrderBy(task => task.Start).ThenBy(task => task.Order))
            {
                var row = rows.FindIndex(ends => task.Start >= ends);
                if (row < 0) { row = rows.Count; rows.Add(task.End); }
                else rows[row] = task.End;
                placed[task] = offset + row;
            }

            offset += Math.Max(1, rows.Count);
        }

        return [.. tasks.Select(task => task with { Order = placed[task] })];
    }

    private static void Merge(List<string> into, string said)
    {
        foreach (var token in said.ToLowerInvariant().Split([' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries))
            if (!into.Contains(token)) into.Add(token);
    }

    /// <summary>A task as written, and how far working it out has got.</summary>
    private sealed class Raw
    {
        public required ContentPart Part { get; init; }
        public ContentPart? Name { get; init; }
        public ContentPart? Hole { get; init; }
        public GanttSection? Section { get; init; }
        public required string Id { get; init; }
        public bool Active, Done, Critical, Milestone, Vert;
        public Raw? Previous { get; init; }
        public IReadOnlyList<string>? After { get; init; }
        public string? StartText { get; init; }
        public IReadOnlyList<string>? Until { get; init; }
        public string EndText { get; init; } = string.Empty;
        public bool Workable = true;
        public DateTime? Start, End, Shown;

        public static Raw Of(ContentPart part, GanttSection? section, Raw? previous, ref int unnamed)
        {
            var name = part.Children.FirstOrDefault(child => child.Kind == GanttKinds.Text);
            var schedule = part.Children.FirstOrDefault(child => child.Kind == GanttKinds.Schedule);
            var items = schedule?.Children ?? [];
            var tags = items.Where(child => child.Kind == GanttKinds.Tag).Select(child => child.Text).ToHashSet(StringComparer.Ordinal);
            var id = items.FirstOrDefault(child => child is { Kind: MermaidKinds.Name, Role: GanttRoles.Id })?.Words()?.Text.Trim();
            var start = items.FirstOrDefault(child => child.Role == GanttRoles.Start);
            var end = items.FirstOrDefault(child => child.Role == GanttRoles.End);

            return new Raw
            {
                Part = part,
                Name = name.Words() is { Length: > 0 } words ? words : null,
                Hole = name.Hole(),
                Section = section,
                Id = id is { Length: > 0 } ? id : "task" + ++unnamed,
                Active = tags.Contains("active"), Done = tags.Contains("done"), Critical = tags.Contains("crit"),
                Milestone = tags.Contains("milestone"), Vert = tags.Contains("vert"),
                Previous = previous,
                After = start?.Kind == GanttKinds.After ? References(start) : null,
                StartText = start?.Kind == GanttKinds.When ? start.Words()?.Text.Trim() : null,
                Until = end?.Kind == GanttKinds.Until ? References(end) : null,
                EndText = end?.Kind == GanttKinds.When ? end.Words()?.Text.Trim() ?? string.Empty : string.Empty,
                Workable = end is not null && schedule!.Children.All(child => child.Role != GanttRoles.Extra),
            };
        }

        private static IReadOnlyList<string> References(ContentPart part) =>
            [.. part.Children.Where(child => child.Kind == MermaidKinds.Name).Select(child => child.Words()?.Text.Trim() ?? string.Empty).Where(id => id.Length > 0)];
    }
}

using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Gantt;

/// <summary>
/// A <c>gantt</c> block as its stages leave it (<see cref="Stages.ResolveTasks"/>): what its front matter asks for, what the lines
/// setting something for the whole chart mean — the last of each written, else the front matter's, else Mermaid's — and the
/// moment it was read, which is today to every task and the line drawn at today. It prints as the block written.
/// </summary>
internal sealed class GanttBlockNode : ContentNode
{
    internal GanttBlockNode(ContentNode written, GanttConfig config, GanttDays days, DateTime now, string axisFormat, GanttTick? tick,
                            GanttToday marker, DayOfWeek weekday, bool topAxis) : base(written)
    {
        this.Config = config;
        this.Days = days;
        this.Now = now;
        this.AxisFormat = axisFormat;
        this.Tick = tick;
        this.Marker = marker;
        this.Weekday = weekday;
        this.TopAxis = topAxis;
    }

    public GanttConfig Config { get; }

    /// <summary>The days a task's length leaves out.</summary>
    public GanttDays Days { get; }

    /// <summary>When the block was read: the day a task with nothing to start after starts, and where the line at today is drawn.</summary>
    public DateTime Now { get; }

    /// <summary>The format the axis writes dates in.</summary>
    public string AxisFormat { get; }

    /// <summary>How far apart the axis's marks are — null to choose, where nothing says anything Mermaid counts in.</summary>
    public GanttTick? Tick { get; }

    /// <summary>How the line at today is drawn, as <c>todayMarker</c> styles it.</summary>
    public GanttToday Marker { get; }

    /// <summary>The day a week starts on, for marks a week apart.</summary>
    public DayOfWeek Weekday { get; }

    /// <summary>Whether the axis's dates are written over the chart as well as under it.</summary>
    public bool TopAxis { get; }

    protected override ContentNode Reshaped(ContentNode shape) =>
        new GanttBlockNode(shape, this.Config, this.Days, this.Now, this.AxisFormat, this.Tick, this.Marker, this.Weekday, this.TopAxis);
}

/// <summary>
/// A task as its stages leave it (<see cref="Stages.ResolveTasks"/>): when it runs, as its schedule means it on this chart on the
/// day it was read, and where a <c>click</c> for it leads. A task whose schedule never comes to a start and an end is left as
/// written. It prints as the task written.
/// </summary>
internal sealed class GanttTaskNode : ContentNode
{
    internal GanttTaskNode(ContentNode written, DateTime start, DateTime end, DateTime shown, string? link, bool clickable) : base(written)
    {
        this.Start = start;
        this.End = end;
        this.Shown = shown;
        this.Link = link;
        this.Clickable = clickable;
    }

    public DateTime Start { get; }

    public DateTime End { get; }

    /// <summary>Where its bar is drawn to — short of <see cref="End"/> where excluded days at its end push that on.</summary>
    public DateTime Shown { get; }

    /// <summary>Where a <c>click … href</c> for it points, if one does.</summary>
    public string? Link { get; }

    /// <summary>Whether a <c>click</c> names it.</summary>
    public bool Clickable { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new GanttTaskNode(shape, this.Start, this.End, this.Shown, this.Link, this.Clickable);
}

/// <summary>
/// The days <c>excludes</c> leaves out of a task's length, less those <c>includes</c> names — dates, days of the week and
/// <c>weekends</c>, in lower case — read as dates in <paramref name="DateFormat"/>.
/// </summary>
internal sealed record GanttDays(string DateFormat, IReadOnlyList<string> Excludes, IReadOnlyList<string> Includes, bool WeekendStartsFriday)
{
    /// <summary>Whether either line names anything.</summary>
    public bool Any => Excludes.Count > 0 || Includes.Count > 0;

    /// <summary>Whether a day is left out of a task's length: named by <c>excludes</c> — as a date, a day of the week, or a weekend day — and not by <c>includes</c>.</summary>
    public bool Excluded(DateTime date)
    {
        var (formatted, plain) = (MermaidDate.Write(date, DateFormat), MermaidDate.Write(date, MermaidDate.Default));
        if (Includes.Contains(formatted.ToLowerInvariant()) || Includes.Contains(plain)) return false;

        var weekend = WeekendStartsFriday ? (DayOfWeek.Friday, DayOfWeek.Saturday) : (DayOfWeek.Saturday, DayOfWeek.Sunday);
        if (Excludes.Contains("weekends") && (date.DayOfWeek == weekend.Item1 || date.DayOfWeek == weekend.Item2)) return true;

        return Excludes.Contains(date.DayOfWeek.ToString().ToLowerInvariant()) || Excludes.Contains(formatted.ToLowerInvariant()) || Excludes.Contains(plain);
    }
}

/// <summary>How far apart a gantt chart's axis marks are: every so many of one of the units Mermaid counts in.</summary>
internal sealed record GanttTick(int Every, string Unit)
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
internal sealed record GanttToday(bool Off, string? Stroke, double? Width, double? Opacity, IReadOnlyList<double>? Dashes)
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

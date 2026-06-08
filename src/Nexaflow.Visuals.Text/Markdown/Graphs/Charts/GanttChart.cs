namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>Completion state of a Gantt task (tags <c>done</c> / <c>active</c>; <c>crit</c> is separate).</summary>
public enum GanttTaskState { Default, Active, Done }

/// <summary>One bar (or milestone) on a Gantt chart, with resolved absolute dates.</summary>
public sealed class GanttTask
{
    public required string Name { get; init; }
    public string? Id           { get; init; }
    public DateTime Start       { get; set; }
    public DateTime End         { get; set; }
    public GanttTaskState State { get; set; } = GanttTaskState.Default;
    public bool Critical        { get; set; }
    public bool IsMilestone     { get; set; }
}

/// <summary>A named group of tasks (a Gantt <c>section</c>).</summary>
public sealed class GanttSection
{
    public required string Name { get; init; }
    public List<GanttTask> Tasks { get; } = [];
}

/// <summary>
/// Data model for a Mermaid <c>gantt</c> chart.  The parser resolves every task's relative
/// spec (<c>after</c>/<c>until</c>/duration/implicit) into absolute <see cref="GanttTask.Start"/>
/// and <see cref="GanttTask.End"/> dates, so the renderer only deals with a date timeline.
/// </summary>
public sealed class GanttChart
{
    public string Title      { get; set; } = string.Empty;
    public string DateFormat { get; set; } = "YYYY-MM-DD";   // input format (day.js tokens)
    public string AxisFormat { get; set; } = string.Empty;   // axis label format (d3 tokens)
    public List<GanttSection> Sections { get; } = [];

    public IEnumerable<GanttTask> Tasks => Sections.SelectMany(s => s.Tasks);
    public int TaskCount => Sections.Sum(s => s.Tasks.Count);

    public DateTime Min => Tasks.Any() ? Tasks.Min(t => t.Start) : DateTime.Today;
    public DateTime Max => Tasks.Any() ? Tasks.Max(t => t.End)   : DateTime.Today.AddDays(1);
}

namespace Nexaflow.Markdown.Mermaid.Gantt;

/// <summary>The pieces a gantt chart's layout is made of — its layers, and what is in them.</summary>
public static class GanttPiece
{
    /// <summary>The bands behind the days <c>excludes</c> leaves out.</summary>
    public const string Excluded = "Excluded";

    /// <summary>The band behind each row, tinted by its section.</summary>
    public const string Rows = "Rows";

    /// <summary>The lines up the chart at each date, and the dates under it — and over it, with <c>topAxis</c>.</summary>
    public const string Grid = "Grid";
    public const string Axis = "Axis";
    public const string Date = "Date";

    /// <summary>What is written beside the bars: each section's name, a task's name that does not fit its bar, a marker's name.</summary>
    public const string Words = "Words";
    public const string SectionName = "SectionName";
    public const string Label = "Label";

    /// <summary>The tasks: a bar, a milestone's diamond or a vertical marker, each standing for its task as written.</summary>
    public const string Tasks = "Tasks";
    public const string Task = "Task";

    /// <summary>The line at today's date.</summary>
    public const string Today = "Today";
}

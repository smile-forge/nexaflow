namespace Nexaflow.Markdown.Mermaid.Gantt;

/// <summary>What a piece of a <c>gantt</c> diagram is.</summary>
public static class GanttKinds
{
    /// <summary>A line setting something for the whole chart — <c>dateFormat YYYY-MM-DD</c>, <c>excludes weekends</c> — its word and what it says.</summary>
    public const string Setting = "gantt-setting";

    /// <summary>A line that is only its word: <c>inclusiveEndDates</c>, <c>topAxis</c>.</summary>
    public const string Flag = "gantt-flag";

    /// <summary>A <c>section</c> line: its word and its name.</summary>
    public const string Section = "gantt-section";

    /// <summary>A task: its name, a colon, and its schedule.</summary>
    public const string Task = "gantt-task";

    /// <summary>Text to the end of where it is written — a task's name, a section's name — or still to write.</summary>
    public const string Text = "gantt-text";

    /// <summary>What follows a task's colon: its tags, its id, when it starts and when it ends, a comma between each.</summary>
    public const string Schedule = "gantt-schedule";

    /// <summary>A task's tag: <c>active</c>, <c>done</c>, <c>crit</c>, <c>milestone</c> or <c>vert</c>.</summary>
    public const string Tag = "gantt-tag";

    /// <summary>When a task starts or ends, as a date or a length of time — or still to write.</summary>
    public const string When = "gantt-when";

    /// <summary>A start after other tasks end: <c>after a1 b2</c>.</summary>
    public const string After = "gantt-after";

    /// <summary>An end when another task starts: <c>until a1</c>.</summary>
    public const string Until = "gantt-until";

    /// <summary>A <c>click</c> line: the tasks it is for, and a link or a call.</summary>
    public const string Click = "gantt-click";

    /// <summary><c>href "https://…"</c> on a <c>click</c> line.</summary>
    public const string Link = "gantt-link";

    /// <summary><c>call name(arguments)</c> on a <c>click</c> line.</summary>
    public const string Call = "gantt-call";
}

/// <summary>What a piece of a <c>gantt</c> diagram is <em>to</em> the piece holding it.</summary>
public static class GanttRoles
{
    /// <summary>A task's name, and a section's.</summary>
    public const string Name = "gantt-name";
    public const string SectionName = "gantt-section-name";

    /// <summary>A task's id, where it is written — what <c>after</c>, <c>until</c> and <c>click</c> name it by.</summary>
    public const string Id = "gantt-id";

    /// <summary>A task's id where another line names it.</summary>
    public const string Reference = "gantt-reference";

    /// <summary>When a task starts, and when it ends.</summary>
    public const string Start = "gantt-start";
    public const string End = "gantt-end";

    /// <summary>Anything a task's schedule says past its end.</summary>
    public const string Extra = "gantt-extra";

    public const string Url = "gantt-url";
    public const string Callback = "gantt-callback";
    public const string Arguments = "gantt-arguments";

    /// <summary>What a setting line says, by its word: <c>gantt-dateformat</c>, <c>gantt-excludes</c>….</summary>
    public static string Of(string word) => "gantt-" + word.ToLowerInvariant();
}

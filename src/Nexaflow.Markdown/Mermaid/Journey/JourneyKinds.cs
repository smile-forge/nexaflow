namespace Nexaflow.Markdown.Mermaid.Journey;

/// <summary>What a piece of a <c>journey</c> diagram is.</summary>
public static class JourneyKinds
{
    /// <summary>A task's line: what is done, how it scored, and who took part.</summary>
    public const string Task = "journey-task";

    /// <summary>A <c>section</c> line: what the tasks under it are grouped as.</summary>
    public const string Section = "journey-section";

    /// <summary>Text to where it ends — what a task is, what a section is called.</summary>
    public const string Text = "journey-text";

    /// <summary>What a stage worked out about a line: the section a task is in.</summary>
    public const string Fact = "journey-fact";
}

/// <summary>What a piece of a <c>journey</c> diagram is <em>to</em> the piece holding it.</summary>
public static class JourneyRoles
{
    /// <summary>What a task is.</summary>
    public const string Says = "journey-says";

    /// <summary>How a task scored, from one to five.</summary>
    public const string Score = "journey-score";

    /// <summary>Who took part in a task, and one of them.</summary>
    public const string Actors = "journey-actors";
    public const string Actor = "journey-actor";

    /// <summary>What a section is called.</summary>
    public const string Name = "journey-name";

    /// <summary>The section a task is in, worked out over the whole block (<see cref="Stages.ResolveTasks"/>).</summary>
    public const string In = "journey-in";
}

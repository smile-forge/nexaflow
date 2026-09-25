namespace Nexaflow.Markdown.Mermaid.Timeline;

/// <summary>What a piece of a <c>timeline</c> diagram is.</summary>
public static class TimelineKinds
{
    /// <summary>A period's line: what the period is called, and the events written after it.</summary>
    public const string Period = "timeline-period";

    /// <summary>A line starting with a colon: more events for the period above it.</summary>
    public const string More = "timeline-more";

    /// <summary>A <c>section</c> line: what the periods under it are grouped as.</summary>
    public const string Section = "timeline-section";

    /// <summary>A <c>direction</c> line, or the way written after the header's keyword.</summary>
    public const string Direction = "timeline-direction";

    /// <summary>Text to where it ends — a period's name, what an event says, a section's name.</summary>
    public const string Text = "timeline-text";
}

/// <summary>What a piece of a <c>timeline</c> diagram is <em>to</em> the piece holding it.</summary>
public static class TimelineRoles
{
    /// <summary>What a period is called.</summary>
    public const string Says = "timeline-says";

    /// <summary>What an event says.</summary>
    public const string Event = "timeline-event";

    /// <summary>What a section is called.</summary>
    public const string Name = "timeline-name";

    /// <summary>Which way the timeline runs.</summary>
    public const string Way = "timeline-way";
}

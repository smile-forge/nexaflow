namespace Nexaflow.Markdown.Mermaid.Timeline;

/// <summary>The pieces a timeline's layout is made of — its layers, and what is in them.</summary>
public static class TimelinePiece
{
    /// <summary>The bands over the periods each section groups, and one — standing for its <c>section</c> line.</summary>
    public const string Sections = "Sections";
    public const string Section = "Section";

    /// <summary>The line the periods sit on, with a mark where each one meets it.</summary>
    public const string Spine = "Spine";

    /// <summary>The periods, and one — standing for the line it is written on.</summary>
    public const string Periods = "Periods";
    public const string Period = "Period";

    /// <summary>What leads from a period to the events written for it.</summary>
    public const string Drop = "Drop";

    /// <summary>The dot on a period's edge where the line down to its events sets out.</summary>
    public const string Mark = "Mark";

    /// <summary>The events, and one — standing for what it says, where it says it.</summary>
    public const string Events = "Events";
    public const string Event = "Event";

    /// <summary>What is written on the timeline: a section's name, a period's, and what an event says.</summary>
    public const string Name = "Name";
    public const string Says = "Says";
}

namespace Nexaflow.Markdown.EventModeling;

/// <summary>The names of the pieces an event model is laid out as — shared by its builder and its tests.</summary>
public static class EventModelingPiece
{
    /// <summary>The whole diagram.</summary>
    public const string Diagram = "eventmodeling-diagram";

    /// <summary>The diagram drawn smaller to fit the room it is given.</summary>
    public const string Reduced = "eventmodeling-reduced";

    /// <summary>The title over the diagram.</summary>
    public const string Title = "eventmodeling-title";

    /// <summary>A swimlane's band.</summary>
    public const string Lane = "eventmodeling-lane";

    /// <summary>What a swimlane is called, at its left.</summary>
    public const string LaneName = "eventmodeling-lane-name";

    /// <summary>A frame's box.</summary>
    public const string Frame = "eventmodeling-frame";

    /// <summary>What a frame is called, on it.</summary>
    public const string Name = "eventmodeling-name";

    /// <summary>A row of a frame's data, of a note, or of a specification.</summary>
    public const string Row = "eventmodeling-row";

    /// <summary>The box a frame, a note or a specification is drawn as, a press on which means the whole of it.</summary>
    public const string Box = "eventmodeling-box";

    /// <summary>The arrow from a frame to a frame it feeds.</summary>
    public const string Feed = "eventmodeling-feed";

    /// <summary>A note under the frame it is about.</summary>
    public const string Note = "eventmodeling-note";

    /// <summary>A specification under the frame it specifies.</summary>
    public const string Specification = "eventmodeling-specification";

    /// <summary>What a clause of a specification is — Given, When, Then.</summary>
    public const string Clause = "eventmodeling-clause";

    /// <summary>A statement that could not be drawn, set as written under the diagram.</summary>
    public const string Unread = "eventmodeling-unread";

    /// <summary>Why a statement set as written under the diagram could not be drawn.</summary>
    public const string Reason = "eventmodeling-reason";
}

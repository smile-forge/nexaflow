namespace Nexaflow.Markdown.Mermaid.Journey;

/// <summary>The pieces a user journey's layout is made of — its layers, and what is in them.</summary>
public static class JourneyPiece
{
    /// <summary>The bands over the tasks each named section groups, and one — standing for its <c>section</c> line.</summary>
    public const string Sections = "Sections";
    public const string Section = "Section";

    /// <summary>The faces floating over the tasks, and one — standing for the score it shows.</summary>
    public const string Faces = "Faces";
    public const string Face = "Face";

    /// <summary>What holds a face over its task.</summary>
    public const string Thread = "Thread";

    /// <summary>The tasks, and one — standing for the line it is written on.</summary>
    public const string Tasks = "Tasks";
    public const string Task = "Task";

    /// <summary>Who takes part in a task: a mark for each, standing for where they are named on its line.</summary>
    public const string Actors = "Actors";
    public const string Actor = "Actor";

    /// <summary>What is written on the journey: a section's name, what a task says, and an actor's name in the legend.</summary>
    public const string Name = "Name";
    public const string Says = "Says";
}

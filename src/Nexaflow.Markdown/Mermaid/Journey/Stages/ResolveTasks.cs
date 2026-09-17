using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Journey.Stages;

/// <summary>
/// Says which section each task is in: the one opened by the nearest <c>section</c> line above it, and none where no
/// section is opened yet — those tasks being a group of their own, as Mermaid groups them.
/// </summary>
public sealed class ResolveTasks : IAstStage
{
    public string Name => "journey:tasks";

    public ContentNode Run(ContentNode tree) =>
        MermaidGrouping.Under(tree, JourneyKinds.Section, JourneyKinds.Task, JourneyKinds.Fact, JourneyRoles.In);
}

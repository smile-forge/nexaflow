using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Timeline.Stages;

/// <summary>
/// Says where a line of further events is written before any period, with nothing above it to add them to. Which period
/// each of the others adds to, and which section each period is in, is the order the lines are written in.
/// </summary>
public sealed class ResolveEvents : IAstStage
{
    public string Name => "timeline:events";

    public ContentNode Run(ContentNode tree) =>
        MermaidGrouping.Unopened(tree, TimelineKinds.Period, TimelineKinds.More,
            "A line of events adds them to the period above it, and there is none: 2004 : Facebook.");
}

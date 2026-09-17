using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Timeline.Stages;

/// <summary>
/// Says which section each period is in — the one opened by the nearest <c>section</c> line above it, and none where no
/// section is opened yet — and which period a line of further events adds them to. A line of events written before any
/// period has nothing to add them to, so it says so.
/// </summary>
public sealed class ResolveSections : IAstStage
{
    public string Name => "timeline:sections";

    public ContentNode Run(ContentNode tree)
    {
        var said = new Dictionary<ContentNode, string?>();
        var section = -1;
        var period = -1;

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line))
        {
            switch (line.Stated())
            {
                case { Kind: TimelineKinds.Section }:
                    section++;
                    break;

                case { Kind: TimelineKinds.Period } at:
                    period++;
                    said[at] = section.ToString(CultureInfo.InvariantCulture);
                    break;

                case { Kind: TimelineKinds.More } more:
                    said[more] = period < 0 ? null : period.ToString(CultureInfo.InvariantCulture);
                    break;
            }
        }

        if (said.Count == 0) return tree;

        return AstRewrite.Each(tree, node =>
        {
            if (!said.TryGetValue(node, out var which)) return node;

            return node.Kind == TimelineKinds.Period
                ? node.Saying(TimelineKinds.Fact, TimelineRoles.In, which!)
                : which is null
                    ? node.Saying("A line of events adds them to the period above it, and there is none: 2004 : Facebook.")
                    : node.Saying(TimelineKinds.Fact, TimelineRoles.Of, which);
        });
    }
}

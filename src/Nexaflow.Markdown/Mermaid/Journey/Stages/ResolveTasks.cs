using System.Globalization;
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

    public ContentNode Run(ContentNode tree)
    {
        var said = new Dictionary<ContentNode, string>();
        var section = -1;

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line))
        {
            switch (line.Stated())
            {
                case { Kind: JourneyKinds.Section }:
                    section++;
                    break;

                case { Kind: JourneyKinds.Task } task:
                    said[task] = section.ToString(CultureInfo.InvariantCulture);
                    break;
            }
        }

        if (said.Count == 0) return tree;

        return AstRewrite.Each(tree, node =>
            said.TryGetValue(node, out var which) ? node.Saying(JourneyKinds.Fact, JourneyRoles.In, which) : node);
    }
}


using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// Where a line that goes into the group opened above it has nothing above it to go into — a timeline's events before any
/// period, a Cynefin diagram's items before any domain. Which group each of the others is in is the order the lines are
/// written in, which whatever draws the diagram reads as it goes.
/// </summary>
public static class MermaidGrouping
{
    /// <summary>
    /// Says where a line of <paramref name="joins"/> is written before any line of <paramref name="opens"/>, with no group
    /// above it to go into. Which group each of the others is in is the order they are written in.
    /// </summary>
    public static ContentNode Unopened(ContentNode tree, string opens, string joins, string trouble)
    {
        var alone = new HashSet<ContentNode>();

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { } stated) continue;
            if (stated.Kind == opens) break;
            if (stated.Kind == joins) alone.Add(stated);
        }

        return alone.Count == 0 ? tree : AstRewrite.Each(tree, node => alone.Contains(node) ? node.Saying(trouble) : node);
    }
}

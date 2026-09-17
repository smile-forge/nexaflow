using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// Which group each line is in, worked out over a whole block: the group opened by the nearest line above it that opens
/// one, and none where nothing has opened one yet — a timeline's sections, a journey's, a Cynefin diagram's domains.
///
/// <para>
/// The fact is hung under the line itself, so a model reading the tree back asks the line rather than counting the
/// sections again; a diagram whose lines must be in a group says so through <c>alone</c>.
/// </para>
/// </summary>
public static class MermaidGrouping
{
    /// <summary>Says which group each line of <paramref name="joins"/> is in.</summary>
    /// <param name="opens">The kind of line opening a group.</param>
    /// <param name="joins">The kind of line going into the group above it.</param>
    /// <param name="kind">The kind the fact is hung as, and <paramref name="role"/> what it is to the line.</param>
    /// <param name="named">What the group is called in the fact — its number from nought, where nothing names it otherwise.</param>
    /// <param name="alone">What is wrong with a line in no group at all — or null where a diagram allows that.</param>
    public static ContentNode Under(ContentNode tree, string opens, string joins, string kind, string role,
                                    Func<ContentNode, string>? named = null, string? alone = null)
    {
        var said = new Dictionary<ContentNode, string?>();
        string? group = null;
        var count = -1;

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { } stated) continue;

            if (stated.Kind == opens)
            {
                count++;
                group = named is null ? count.ToString(CultureInfo.InvariantCulture) : named(stated);
            }
            else if (stated.Kind == joins)
            {
                said[stated] = group;
            }
        }

        if (said.Count == 0) return tree;

        return AstRewrite.Each(tree, node =>
        {
            if (!said.TryGetValue(node, out var which)) return node;

            return which is not null ? node.Saying(kind, role, which)
                 : alone is not null ? node.Saying(alone)
                 : node;
        });
    }
}

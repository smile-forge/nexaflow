
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// What each line of a block is inside, worked out over the whole block: the group opened by the nearest line above it that
/// opens one and has not been ended yet — for a diagram whose groups nest and close with a word of their own, as a block
/// diagram's composites and a flowchart's subgraphs do.
///
/// <para>
/// <see cref="Nest"/> gathers each group into a <see cref="MermaidKinds.Group"/> holding the lines written in it, so the tree
/// says what is inside what and a builder reads it by walking down it.
/// </para>
/// </summary>
public static class MermaidNesting
{
    /// <summary>
    /// Gathers each group of <paramref name="tree"/> with the lines written in it into a <see cref="MermaidKinds.Group"/>: the
    /// line opening it, everything down to the line ending it, and that line. A group nothing ends holds the rest of the block.
    /// Nothing is moved past anything else, so the tree prints as it was written.
    /// </summary>
    /// <param name="opens">The kinds of line opening a group.</param>
    /// <param name="ends">The kinds of line ending the group it is in.</param>
    /// <param name="stray">What is wrong with a line ending a group when none is open — or null where a diagram allows it.</param>
    /// <param name="unclosed">What is wrong with a group nothing ends — or null where a diagram allows it.</param>
    public static ContentNode Nest(ContentNode tree, IReadOnlyList<string> opens, IReadOnlyList<string> ends,
                                   string? stray = null, string? unclosed = null) =>
        AstRewrite.Regrouping(tree, (node, lines) => node.Kind == MermaidKinds.Block ? Nested(lines, opens, ends, stray, unclosed) : null);

    private static List<ContentNode>? Nested(IReadOnlyList<ContentNode> lines, IReadOnlyList<string> opens, IReadOnlyList<string> ends,
                                             string? stray, string? unclosed)
    {
        var outermost = new List<ContentNode>(lines.Count);
        var open = new Stack<List<ContentNode>>();
        var moved = false;

        foreach (var line in lines)
        {
            var kind = line.Stated()?.Kind;
            var into = open.Count > 0 ? open.Peek() : outermost;

            if (kind is not null && opens.Contains(kind))
            {
                open.Push([line]);
                moved = true;
            }
            else if (kind is not null && ends.Contains(kind) && open.Count > 0)
            {
                into.Add(line);
                Closed(open.Pop());
            }
            else if (kind is not null && ends.Contains(kind) && stray is not null)
            {
                into.Add(Told(line, stray));
                moved = true;
            }
            else
            {
                into.Add(line);
            }
        }

        while (open.Count > 0)
        {
            var group = open.Pop();
            if (unclosed is not null) group[0] = Told(group[0], unclosed);
            Closed(group);
        }

        return moved ? outermost : null;

        void Closed(List<ContentNode> group) =>
            (open.Count > 0 ? open.Peek() : outermost).Add(ContentNode.Branch(MermaidKinds.Group, group));
    }

    /// <summary>A line whose statement is told what is wrong with it.</summary>
    private static ContentNode Told(ContentNode line, string reason)
    {
        var stated = line.Stated()!;
        return line.With([.. line.Children.Select(child => ReferenceEquals(child, stated) ? stated.Saying(reason) : child)]);
    }
}

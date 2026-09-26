using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// What each line of a block is inside, worked out over the whole block: the group opened by the nearest line above it that
/// opens one and has not been ended yet — for a diagram whose groups nest and close with a word of their own, as a block
/// diagram's composites do.
///
/// <para>
/// <see cref="Nest"/> gathers each group into a <see cref="MermaidKinds.Group"/> holding the lines written in it, so the tree
/// says what is inside what and a builder reads it by walking down it. <see cref="Inside"/> says it as facts instead, naming a
/// group by where it was opened — for a diagram still read into a model.
/// </para>
/// </summary>
public static class MermaidNesting
{
    /// <summary>What a line inside no group at all is inside: the block itself.</summary>
    public const string Outermost = "";

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

    /// <summary>Says what each line of <paramref name="tree"/> is inside, and what each line opening a group opens.</summary>
    /// <param name="opens">The kinds of line opening a group — several where a diagram has more than one kind of group and
    /// one word closes them all, as a sequence diagram's box and its fragments both end with <c>end</c>.</param>
    /// <param name="ends">The kinds of line ending the group it is in — several where a diagram is written in more than one
    /// language and each has a word of its own, as a C4 sequence's <c>}</c> closes a boundary and its <c>end</c> a frame.</param>
    /// <param name="joins">The kinds of line that go inside a group, besides the lines that open one.</param>
    /// <param name="kind">The kind the facts are hung as.</param>
    /// <param name="inside">The role naming the group a line is inside.</param>
    /// <param name="opened">The role naming the group a line opens.</param>
    /// <param name="stray">What is wrong with a line ending a group when none is open — or null where a diagram allows it.</param>
    /// <param name="unclosed">What is wrong with a group nothing ends — or null where a diagram allows it.</param>
    public static ContentNode Inside(ContentNode tree, IReadOnlyList<string> opens, IReadOnlyList<string> ends,
                                     IReadOnlyList<string> joins, string kind, string inside, string opened,
                                     string? stray = null, string? unclosed = null)
    {
        var said = new Dictionary<ContentNode, (string Inside, string? Opened)>();
        var wrong = new Dictionary<ContentNode, string>();
        var open = new Stack<(ContentNode Line, string Key)>();
        var count = 0;

        foreach (var line in tree.SelfAndDescendants().Where(node => node.Kind == MermaidKinds.Line))
        {
            if (line.Stated() is not { } stated) continue;

            if (ends.Contains(stated.Kind))
            {
                if (open.Count > 0) open.Pop();
                else if (stray is not null) wrong[stated] = stray;

                continue;
            }

            var holder = open.Count > 0 ? open.Peek().Key : Outermost;

            if (opens.Contains(stated.Kind))
            {
                var key = count++.ToString(CultureInfo.InvariantCulture);
                said[stated] = (holder, key);
                open.Push((stated, key));
            }
            else if (joins.Contains(stated.Kind))
            {
                said[stated] = (holder, null);
            }
        }

        if (unclosed is not null)
            foreach (var (line, _) in open) wrong[line] = unclosed;

        if (said.Count == 0 && wrong.Count == 0) return tree;

        return AstRewrite.Each(tree, node =>
        {
            var told = node;

            if (said.TryGetValue(node, out var where))
            {
                told = told.Saying(kind, inside, where.Inside);
                if (where.Opened is { } key) told = told.Saying(kind, opened, key);
            }

            return wrong.TryGetValue(node, out var reason) ? told.Saying(reason) : told;
        });
    }
}

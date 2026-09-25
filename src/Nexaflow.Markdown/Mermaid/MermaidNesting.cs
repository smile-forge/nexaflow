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
/// A group is named by where it was opened rather than by what it is called, since a group need not be called anything and
/// two may be called the same; the line opening one is told both the group it is inside and the group it opens, so a model
/// reading the tree back builds the whole nesting in one pass down the lines.
/// </para>
/// </summary>
public static class MermaidNesting
{
    /// <summary>What a line inside no group at all is inside: the block itself.</summary>
    public const string Outermost = "";

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

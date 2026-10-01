using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Mermaid.Flowchart.Stages;

/// <summary>
/// Hangs a <c>click</c> line's address on the node it names, so where a node leads is said where that node is written.
///
/// <para>
/// A stage, because it is a fact about what the source amounts to rather than about how it is drawn. Mermaid lets the address
/// be written on a line of its own — <c>click a href "…"</c> — and the node it applies to is somewhere else entirely, so
/// nothing above a press on that node says where it leads. This puts the two together as a derived fact on the node, which is
/// how a markdown reference link already carries what its definition said, so the engine finds both the same way
/// (<see cref="Roles.Destination"/>).
/// </para>
/// <para>
/// A diagram whose click lines only name a callback, and one with no click lines at all, is read as it was.
/// </para>
/// </summary>
public sealed class ResolveDestinations : IAstStage
{
    public string Name => "flowchart:destinations";

    public ContentNode Run(ContentNode tree)
    {
        Dictionary<string, string>? leads = null;

        foreach (var node in tree.SelfAndDescendants())
        {
            if (node.Kind != FlowchartKinds.Click) continue;
            if (Said(node) is not { Length: > 0 } where) continue;
            if (Names(node) is not { Length: > 0 } id) continue;

            (leads ??= [])[id] = where;
        }

        if (leads is null) return tree;

        return AstRewrite.Each(tree, node =>
            node.Kind != FlowchartKinds.Click
            && Names(node) is { Length: > 0 } id
            && leads.TryGetValue(id, out var where)
            && Said(node) is null
                ? node.Holding(FlowchartKinds.Click, Roles.Destination, where)
                : node);
    }

    /// <summary>
    /// The address <paramref name="node"/> says, or null where it says none. Anywhere inside it, because a quoted address is
    /// written in quotes and the words are what carry the role.
    /// </summary>
    private static string? Said(ContentNode node) =>
        node.SelfAndDescendants().FirstOrDefault(held => held.Role == Roles.Destination && !held.IsDerived)?.Print().Trim();

    /// <summary>
    /// What <paramref name="node"/> calls itself: the name it holds, which is how a node is known by everything that names one —
    /// a link's ends, a <c>class</c>, a <c>style</c>, a <c>click</c>. Null where it names nothing.
    ///
    /// <para>
    /// Found by what the name is rather than by the role it was written under, because the two ends of a link name their nodes
    /// under roles of their own while a click line names one under its. This is the same way the builder knows a node.
    /// </para>
    /// </summary>
    private static string? Names(ContentNode node) =>
        node.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name)?.Print().Trim();
}

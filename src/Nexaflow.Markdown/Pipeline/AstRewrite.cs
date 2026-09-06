using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Pipeline;

/// <summary>
/// The three shapes almost every stage is: rewrite each piece, gather a run of pieces into one, or hang
/// something underneath a piece saying what it amounts to.
///
/// <para>
/// Written once because getting them wrong is expensive in the same way each time. Every one of these
/// returns the node it was given when nothing under it moved, so an untouched subtree is shared rather
/// than rebuilt — which is what keeps a stage cheap over a long tune, and what makes "did this stage do
/// anything" a reference comparison.
/// </para>
/// </summary>
public static class AstRewrite
{
    /// <summary>
    /// The same tree with <paramref name="of"/> applied to every piece, depth first — children before the
    /// piece holding them, so a rewrite sees what its own parts have already become.
    /// </summary>
    public static ContentNode Each(ContentNode node, Func<ContentNode, ContentNode> of)
    {
        if (node.IsLeaf) return of(node);

        var rebuilt = new List<ContentNode>(node.Children.Count);
        var moved = false;

        foreach (var child in node.Children)
        {
            var seen = Each(child, of);
            moved |= !ReferenceEquals(seen, child);
            rebuilt.Add(seen);
        }

        return of(moved ? node.With(rebuilt) : node);
    }

    /// <summary>
    /// The same tree with every piece's children handed to <paramref name="regroup"/>, which returns the
    /// children it wants in their place, or null to leave them alone. Depth first, so a regrouping sees
    /// the groups made beneath it.
    /// </summary>
    /// <remarks>
    /// A stage that gathers a run into one node is doing this, and the rule it must keep is the
    /// pipeline's: the children coming back print as the children going in, in the same order.
    /// Re-nesting them, and hanging derived pieces among them, is all a stage may do.
    /// </remarks>
    public static ContentNode Regrouping(
        ContentNode node,
        Func<ContentNode, IReadOnlyList<ContentNode>, IReadOnlyList<ContentNode>?> regroup)
    {
        if (node.IsLeaf) return node;

        var rebuilt = new List<ContentNode>(node.Children.Count);
        var moved = false;

        foreach (var child in node.Children)
        {
            var seen = Regrouping(child, regroup);
            moved |= !ReferenceEquals(seen, child);
            rebuilt.Add(seen);
        }

        // What an earlier stage hung on THIS node is not among the children being regrouped: it explains
        // the node, so sweeping it into one of the groups made out of the node's contents would move an
        // answer somewhere it is not true. Held back, and put back afterwards.
        var facts = rebuilt.Where(child => child.Role == Roles.Derived).ToList();
        var contents = facts.Count == 0 ? rebuilt : [.. rebuilt.Where(child => child.Role != Roles.Derived)];

        var regrouped = regroup(node, contents);
        if (regrouped is not null) return node.With(facts.Count == 0 ? regrouped : [.. regrouped, .. facts]);

        return moved ? node.With(rebuilt) : node;
    }

    // ── Saying what a piece amounts to ──────────────────────────────────────

    /// <summary>
    /// A fact about a piece, as a part of it: what it was worked out to be, rather than what was typed.
    /// <para>
    /// <see cref="Roles.Derived"/>, so it takes up no source, prints as nothing, and is nowhere to be
    /// found by an offset. That is what lets a stage record a pitch, a duration, a printed accidental or
    /// a syllable and still leave the tune printing as exactly what was written.
    /// </para>
    /// </summary>
    public static ContentNode Fact(string kind, string role, string text) =>
        ContentNode.Branch(kind, [ContentNode.Leaf(kind, text, role)], Roles.Derived);

    /// <summary>The same piece with a fact hung underneath it.</summary>
    public static ContentNode Saying(this ContentNode node, string kind, string role, string text) =>
        node.With([.. node.Children, Fact(kind, role, text)]);

    /// <summary>The same piece with several facts hung underneath it, or unchanged where there are none.</summary>
    public static ContentNode Saying(this ContentNode node, params (string Kind, string Role, string Text)[] facts) =>
        facts.Length == 0 ? node : node.With([.. node.Children, .. facts.Select(f => Fact(f.Kind, f.Role, f.Text))]);

    /// <summary>The fact of <paramref name="role"/> hung under this piece, or null where none was.</summary>
    public static ContentNode? Fact(this ContentNode node, string role)
    {
        foreach (var child in node.Children)
        {
            if (child.Role != Roles.Derived) continue;
            foreach (var inner in child.Children)
                if (inner.Role == role) return inner;
        }

        return null;
    }

    /// <summary>The text of the fact of <paramref name="role"/>, or null.</summary>
    public static string? Said(this ContentNode node, string role) => node.Fact(role)?.Text;
}

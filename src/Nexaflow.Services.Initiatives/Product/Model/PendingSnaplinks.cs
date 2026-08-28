using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>
/// One branch's snaplink changes, waiting to be folded into the shared tree when the branch merges.
/// <para>
/// Nodes and snaplinks are different kinds of claim. A node — "there should be a Latex parser" — is a plan,
/// and the tree is deliberately forward-looking about those, so a node is written to the shared tree at
/// once. A snaplink is a claim that <i>this file exists and contains this</i>, and on a branch that has not
/// merged, that claim is simply not true anywhere else. Writing it to the shared tree is what made the main
/// checkout report broken links for work nobody had finished.
/// </para>
/// <para>
/// The set is recorded per node (and per concern), whole rather than as individual link deltas: the promote
/// rule is "this branch's links replace what is there", and a whole set expresses a removal — an empty list
/// — as clearly as an addition. Inferring removals from absence could not tell "I deleted this link" from
/// "I never touched this node".
/// </para>
/// <para>
/// It is stored under the <b>committed</b> export directory rather than in gitignored working state, which
/// is what lets it travel with the pull request. At merge the change set arrives in the main checkout along
/// with the code it describes, so consolidating it needs no knowledge of which machine or worktree produced
/// it — and a branch that is abandoned never merges, so its pending set never arrives and there is nothing
/// to clean up.
/// </para>
/// </summary>
public sealed class PendingSnaplinks
{
    /// <summary>The branch these changes belong to — also the file's name.</summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>Node id → the link sets this branch has changed for it.</summary>
    public Dictionary<string, PendingNodeLinks> Nodes { get; set; } = new(StringComparer.Ordinal);

    public bool IsEmpty => Nodes.Count == 0;

    /// <summary>How many link sets are recorded — the number promote would overwrite.</summary>
    public int ChangedSets => Nodes.Values.Sum(n => (n.Links is null ? 0 : 1) + (n.Concerns?.Count ?? 0));

    /// <summary>
    /// Records a node's links exactly as they now stand, replacing any earlier record for the same target.
    /// </summary>
    /// <param name="concernTag">Null for the node's own links, else the concern whose links changed.</param>
    public void Capture(string nodeId, string? concernTag, IReadOnlyList<Snaplink> links)
    {
        if (!Nodes.TryGetValue(nodeId, out var entry)) Nodes[nodeId] = entry = new PendingNodeLinks();

        if (concernTag is { Length: > 0 })
        {
            entry.Concerns ??= new Dictionary<string, List<Snaplink>>(StringComparer.Ordinal);
            entry.Concerns[concernTag] = [.. links];
        }
        else entry.Links = [.. links];
    }

    /// <summary>
    /// Overlays these changes onto a tree — used both to read the branch's own view and to promote.
    /// A recorded set replaces whatever the target holds; a node the branch does not have is skipped, since
    /// promoting links onto a node that never merged would be inventing one.
    /// </summary>
    /// <returns>How many link sets were applied.</returns>
    public int ApplyTo(ProductState state)
    {
        var applied = 0;
        foreach (var (nodeId, entry) in Nodes)
        {
            if (!state.Nodes.TryGetValue(nodeId, out var node)) continue;

            if (entry.Links is { } links)
            {
                node.Snaplinks = [.. links];
                applied++;
            }

            foreach (var (tag, concernLinks) in entry.Concerns ?? [])
            {
                var concern = node.Concerns?.FirstOrDefault(c => string.Equals(c.Tag, tag, StringComparison.Ordinal));
                if (concern is null) continue;
                concern.Snaplinks = [.. concernLinks];
                applied++;
            }
        }
        return applied;
    }

    /// <summary>Node ids this branch has changed links for, in a stable order.</summary>
    public IReadOnlyList<string> TouchedNodes => [.. Nodes.Keys.Order(StringComparer.Ordinal)];
}

/// <summary>The link sets one node has had changed on a branch. A null member means "not touched here".</summary>
public sealed class PendingNodeLinks
{
    /// <summary>The node's own links, when this branch changed them.</summary>
    public List<Snaplink>? Links { get; set; }

    /// <summary>Concern tag → that concern's links, for the concerns this branch changed.</summary>
    public Dictionary<string, List<Snaplink>>? Concerns { get; set; }
}

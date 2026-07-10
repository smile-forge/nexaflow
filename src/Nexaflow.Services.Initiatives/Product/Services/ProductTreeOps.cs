using Nexaflow.Services.Initiatives.Product.Model;

namespace Nexaflow.Services.Initiatives.Product.Services;

/// <summary>
/// Pure structural edits on the node tree (promote / demote / re-parent) — mutate <see cref="ProductState.Nodes"/>
/// in place and report whether anything changed. No persistence or UI; the view-model wraps these with
/// <c>SaveTree</c> + rebuild. Each guards its own preconditions and re-parenting rejects cycles.
/// </summary>
public static class ProductTreeOps
{
    /// <summary>A node's ordered siblings — its parent's child list, or the roots when it's top-level.</summary>
    public static List<string> OrderedSiblings(ProductState s, string? parentId) =>
        parentId is not null && s.Nodes.TryGetValue(parentId, out var p)
            ? p.Children.Where(s.Nodes.ContainsKey).ToList()
            : ProductAggregator.Roots(s).ToList();

    public static bool CanPromote(ProductState s, string id) =>
        s.Nodes.TryGetValue(id, out var n) && n.Parent is not null;

    public static bool CanDemote(ProductState s, string id) =>
        s.Nodes.TryGetValue(id, out var n) && OrderedSiblings(s, n.Parent).IndexOf(id) > 0;

    /// <summary>Outdent: the node becomes a sibling of its parent, placed just after it.</summary>
    public static bool Promote(ProductState s, string id)
    {
        if (!s.Nodes.TryGetValue(id, out var node)) return false;
        var parentId = node.Parent;
        if (parentId is null || !s.Nodes.TryGetValue(parentId, out var parent)) return false;   // already a root
        var grandId = parent.Parent;

        parent.Children.Remove(id);
        node.Parent = grandId;
        if (grandId is not null && s.Nodes.TryGetValue(grandId, out var grand))
        {
            var at = grand.Children.IndexOf(parentId);
            if (at >= 0) grand.Children.Insert(at + 1, id); else grand.Children.Add(id);
        }
        return true;
    }

    /// <summary>Indent: the node moves under its previous sibling.</summary>
    public static bool Demote(ProductState s, string id)
    {
        if (!s.Nodes.TryGetValue(id, out var node)) return false;
        var siblings = OrderedSiblings(s, node.Parent);
        var idx = siblings.IndexOf(id);
        if (idx <= 0 || !s.Nodes.TryGetValue(siblings[idx - 1], out var prev)) return false;

        if (node.Parent is not null && s.Nodes.TryGetValue(node.Parent, out var parent))
            parent.Children.Remove(id);
        node.Parent = siblings[idx - 1];
        prev.Children.Add(id);
        return true;
    }

    /// <summary>Re-parents <paramref name="id"/> under <paramref name="newParentId"/> (null = top-level).
    /// Rejects no-ops, missing targets, and cycles (can't move a node under its own descendant).</summary>
    public static bool Reparent(ProductState s, string id, string? newParentId)
    {
        if (!s.Nodes.TryGetValue(id, out var node)) return false;
        if (id == newParentId || newParentId == node.Parent) return false;
        if (newParentId is not null && (!s.Nodes.ContainsKey(newParentId) || IsAncestorOrSelf(s, id, newParentId)))
            return false;

        if (node.Parent is not null && s.Nodes.TryGetValue(node.Parent, out var oldParent))
            oldParent.Children.Remove(id);
        node.Parent = newParentId;
        if (newParentId is not null && s.Nodes.TryGetValue(newParentId, out var newParent))
            newParent.Children.Add(id);
        return true;
    }

    /// <summary>True if <paramref name="ancestor"/> is <paramref name="node"/> itself or one of its ancestors.</summary>
    public static bool IsAncestorOrSelf(ProductState s, string ancestor, string node)
    {
        for (string? cur = node; cur is not null; cur = s.Nodes.TryGetValue(cur, out var n) ? n.Parent : null)
            if (cur == ancestor) return true;
        return false;
    }

    /// <summary>
    /// Applies <paramref name="status"/> down the subtree rooted at <paramref name="id"/> (used by the sunburst
    /// "Status: …" menu). The clicked node's own status changes directly; descendant <em>leaf</em> statuses and
    /// <b>every concern</b> in the subtree change only when they are currently <c>should</c> — deliberate
    /// <c>shouldnt</c>/<c>faulted</c> (and already-<c>done</c>) items are protected from the cascade and must
    /// be changed on the node itself. Parent stored statuses are derived, so they're left untouched.
    /// </summary>
    public static void CascadeStatus(ProductState s, string id, Status status)
    {
        var seen = new HashSet<string>();
        void Walk(string n, bool direct)
        {
            if (!seen.Add(n) || !s.Nodes.TryGetValue(n, out var node)) return;
            var kids = node.Children.Where(s.Nodes.ContainsKey).ToList();

            if (kids.Count == 0 && (direct || node.Status == Status.Should))
                node.Status = status;                       // the clicked leaf changes regardless; others only if "should"

            if (node.Concerns is not null)
                foreach (var c in node.Concerns)
                    if (c.Status == Status.Should) c.Status = status;

            foreach (var c in kids) Walk(c, direct: false);
        }
        Walk(id, direct: true);
    }
}

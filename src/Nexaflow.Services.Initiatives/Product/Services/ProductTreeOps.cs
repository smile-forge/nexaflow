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

    /// <summary>Deletes <paramref name="id"/> — and, with <paramref name="recursive"/>, its whole subtree —
    /// removing it from its parent's child list. Returns the ids actually removed, or <c>null</c> when the node
    /// is missing or (without <paramref name="recursive"/>) still has children — the caller reports that guard.</summary>
    public static List<string>? Remove(ProductState s, string id, bool recursive)
    {
        if (!s.Nodes.TryGetValue(id, out var node)) return null;
        if (node.Children.Count(s.Nodes.ContainsKey) > 0 && !recursive) return null;

        var removed = new List<string>();
        void Collect(string n)
        {
            removed.Add(n);
            if (s.Nodes.TryGetValue(n, out var nn))
                foreach (var c in nn.Children.Where(s.Nodes.ContainsKey).ToList()) Collect(c);
        }
        Collect(id);

        if (node.Parent is { } pid && s.Nodes.TryGetValue(pid, out var parent)) parent.Children.Remove(id);
        foreach (var n in removed) s.Nodes.Remove(n);
        return removed;
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

    // ── Concern / snaplink / field edits (the mutations the CLI and in-app tools share) ──────────

    /// <summary>Adds or updates this node's link to a concern <paramref name="tag"/>, creating the concern
    /// list/link as needed. Returns false only when the node is missing — the caller validates the tag
    /// against the product's concern vocabulary (so it can list the valid tags on error).</summary>
    public static bool SetConcern(ProductState s, string id, string tag, Status status)
    {
        if (!s.Nodes.TryGetValue(id, out var node)) return false;
        node.Concerns ??= [];
        var link = node.Concerns.FirstOrDefault(c => c.Tag == tag);
        if (link is null) node.Concerns.Add(new ConcernLink { Tag = tag, Status = status });
        else link.Status = status;
        return true;
    }

    /// <summary>Removes this node's link to concern <paramref name="tag"/> (and any snaplinks on it). Returns
    /// false when the node has no such concern; nulls the concern list once its last entry is gone.</summary>
    public static bool RemoveConcern(ProductState s, string id, string tag)
    {
        if (!s.Nodes.TryGetValue(id, out var node) || node.Concerns is null) return false;
        var removed = node.Concerns.RemoveAll(c => c.Tag == tag) > 0;
        if (node.Concerns.Count == 0) node.Concerns = null;
        return removed;
    }

    /// <summary>Attaches <paramref name="link"/> to the node itself, or — when <paramref name="concernTag"/>
    /// is given — to that concern's link. Returns false if the node (or the named concern) doesn't exist.</summary>
    public static bool AddSnaplink(ProductState s, string id, Snaplink link, string? concernTag = null)
    {
        if (!s.Nodes.TryGetValue(id, out var node)) return false;
        if (concernTag is null)
        {
            (node.Snaplinks ??= []).Add(link);
            return true;
        }
        var concern = node.Concerns?.FirstOrDefault(c => c.Tag == concernTag);
        if (concern is null) return false;
        (concern.Snaplinks ??= []).Add(link);
        return true;
    }

    /// <summary>Removes snaplinks from the node itself, or — with <paramref name="concernTag"/> — from that
    /// concern's link. Three addressing modes, in priority order: <paramref name="match"/> removes every link
    /// whose fields agree with it, <paramref name="index"/> removes just that one entry (0-based), and neither
    /// clears them all. Returns how many were removed (0 if the node/concern/list is absent, the index is out of
    /// range, or nothing matched).
    /// <para>
    /// Prefer <paramref name="match"/>: an index is a position in a list that any other edit reorders, which
    /// makes it the wrong handle for a script, while clear-them-all is only ever right when you mean all.
    /// </para></summary>
    public static int RemoveSnaplink(ProductState s, string id, string? concernTag = null, int? index = null,
                                     SnaplinkFilter? match = null)
    {
        if (!s.Nodes.TryGetValue(id, out var node)) return 0;
        var list = concernTag is null
            ? node.Snaplinks
            : node.Concerns?.FirstOrDefault(c => c.Tag == concernTag)?.Snaplinks;
        if (list is null || list.Count == 0) return 0;

        if (match is { IsEmpty: false } filter) return list.RemoveAll(filter.Matches);

        if (index is { } i)
        {
            if (i < 0 || i >= list.Count) return 0;
            list.RemoveAt(i);
            return 1;
        }
        var n = list.Count;
        list.Clear();
        return n;
    }

    /// <summary>
    /// Edits one existing snaplink in place: <paramref name="set"/> assigns fields, <paramref name="clear"/>
    /// names fields to unset. Returns false if the node, concern, or index isn't there.
    /// <para>
    /// Clearing is the half <see cref="Remove"/>-then-<see cref="AddSnaplink"/> cannot do without losing the
    /// link's other fields and its position, and it is what a link needs when a target stops having the
    /// structure it names — a <c>.xaml</c> that turns out to be a ResourceDictionary declares no class, so the
    /// honest link is the file alone rather than a class that cannot exist.
    /// </para></summary>
    public static bool SetSnaplink(ProductState s, string id, int index, string? concernTag,
                                   Action<Snaplink>? set = null, IEnumerable<string>? clear = null)
    {
        if (!s.Nodes.TryGetValue(id, out var node)) return false;
        var list = concernTag is null
            ? node.Snaplinks
            : node.Concerns?.FirstOrDefault(c => c.Tag == concernTag)?.Snaplinks;
        if (list is null || index < 0 || index >= list.Count) return false;

        var link = list[index];
        set?.Invoke(link);
        foreach (var field in clear ?? [])
            switch (field.Trim().ToLowerInvariant())
            {
                case "class":      link.Class = null; break;
                case "method":     link.Method = null; break;
                case "ast":        link.Ast = null; break;
                case "doc":        link.Doc = null; break;
                case "target":     link.Target = null; break;
                case "title-path": link.TitlePath = null; break;
                case "status":     link.Status = null; break;
                default: return false;
            }
        return true;
    }

    /// <summary>Why a <see cref="Rename"/> was refused — so the caller can word the error.</summary>
    public enum RenameError { None, NoSuchNode, IdTaken, IdInvalid }

    /// <summary>
    /// Changes a node's <b>id</b>, the one field every other reference is keyed on. Node ids are a single flat
    /// global namespace (<see cref="ProductState.Nodes"/> is one dictionary), so a too-generic id like
    /// <c>run</c> or <c>viewlet</c> is a collision waiting to happen — this is the safe way to specialise one.
    /// Retargets everything that names it: the dictionary key (kept in place, so the file doesn't churn), the
    /// parent's <see cref="ProductNode.Children"/> entry (in position), every child's
    /// <see cref="ProductNode.Parent"/> back-reference, and every <c>node</c>-type snaplink in the tree —
    /// node-level and concern-level alike — whose target is the old id.
    /// </summary>
    /// <remarks>
    /// It cannot reach references that live <em>outside</em> the tree: a test's <c>[CoversNode("old-id")]</c>
    /// still names the old id (the NXCOV002 analyzer flags it) and a committed release snapshot keeps it by
    /// design. The caller is expected to say so.
    /// </remarks>
    public static RenameError Rename(ProductState s, string oldId, string newId)
    {
        if (string.IsNullOrWhiteSpace(newId) || newId.Any(char.IsWhiteSpace)) return RenameError.IdInvalid;
        if (!s.Nodes.ContainsKey(oldId)) return RenameError.NoSuchNode;
        if (oldId == newId || s.Nodes.ContainsKey(newId)) return oldId == newId ? RenameError.IdInvalid : RenameError.IdTaken;

        var node = s.Nodes[oldId];

        // Rebuild the map so the renamed node keeps its slot — a moved entry would churn the whole file's ordering.
        var rebuilt = new Dictionary<string, ProductNode>(s.Nodes.Count, StringComparer.Ordinal);
        foreach (var (id, n) in s.Nodes) rebuilt[id == oldId ? newId : id] = n;
        s.Nodes = rebuilt;

        if (node.Parent is { } pid && s.Nodes.TryGetValue(pid, out var parent))
        {
            var at = parent.Children.IndexOf(oldId);
            if (at >= 0) parent.Children[at] = newId; else parent.Children.Add(newId);
        }
        foreach (var child in node.Children)
            if (s.Nodes.TryGetValue(child, out var c) && c.Parent == oldId) c.Parent = newId;

        foreach (var n in s.Nodes.Values)
        foreach (var link in (n.Snaplinks ?? []).Concat((n.Concerns ?? []).SelectMany(c => c.Snaplinks ?? [])))
            if (link.Type == "node" && link.Target == oldId) link.Target = newId;

        return RenameError.None;
    }

    /// <summary>Edits the node's scalar fields; only non-null arguments are applied, and an empty string
    /// clears an optional field (description/note). Returns false if the node is missing.</summary>
    public static bool EditNode(ProductState s, string id, string? title = null, string? description = null, string? note = null)
    {
        if (!s.Nodes.TryGetValue(id, out var node)) return false;
        if (title is { Length: > 0 }) node.Title = title;
        if (description is not null) node.Description = description.Length == 0 ? null : description;
        if (note is not null) node.Note = note.Length == 0 ? null : note;
        return true;
    }

    // ── Structural integrity: reconcile children[] against the child→Parent back-references ──────

    /// <summary>One parent whose <see cref="ProductNode.Children"/> list disagreed with the back-references.</summary>
    public sealed record ChildRepair(string Parent, List<string> Before, List<string> After, List<string> Dropped);

    /// <summary>
    /// Reconciles every parent's <see cref="ProductNode.Children"/> against the authoritative child→
    /// <see cref="ProductNode.Parent"/> back-references, catching the two structural failure modes a hand-edit
    /// can introduce: <b>dangling</b> child ids (no such node — including two ids accidentally concatenated into
    /// one string) and <b>orphans</b> (a node names this parent but isn't listed). Back-references are trusted as
    /// the truth: valid existing entries keep their order, a concatenated dangling id is split back into its real
    /// members, orphans are appended, and an entry naming a real node that belongs elsewhere is dropped. With
    /// <paramref name="apply"/> the tree is mutated in place; either way the changes are returned for reporting.
    /// </summary>
    public static List<ChildRepair> RepairChildren(ProductState s, bool apply)
    {
        // authoritative membership: parent id → child ids (in nodes order) whose Parent points at it
        var byRef = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (cid, node) in s.Nodes)
            if (node.Parent is { } p) (byRef.TryGetValue(p, out var l) ? l : byRef[p] = []).Add(cid);

        var repairs = new List<ChildRepair>();
        foreach (var (pid, parent) in s.Nodes)
        {
            var members = byRef.GetValueOrDefault(pid) ?? [];
            var memberSet = new HashSet<string>(members, StringComparer.Ordinal);
            var placed = new List<string>();
            var placedSet = new HashSet<string>(StringComparer.Ordinal);
            var dropped = new List<string>();

            void Place(string cid) { if (memberSet.Contains(cid) && placedSet.Add(cid)) placed.Add(cid); }

            foreach (var entry in parent.Children)
            {
                if (memberSet.Contains(entry)) { Place(entry); continue; }         // a real child — keep author order
                if (s.Nodes.ContainsKey(entry)) { dropped.Add(entry); continue; }  // real node, belongs elsewhere — drop
                var split = TrySplit(entry, memberSet, placedSet);                 // dangling — recover a concatenation
                if (split.Count > 0) foreach (var part in split) Place(part);
                else dropped.Add(entry);                                            // unrecoverable dangling id
            }
            foreach (var m in members) Place(m);   // append any still-unplaced orphans

            if (parent.Children.SequenceEqual(placed) && dropped.Count == 0) continue;
            repairs.Add(new ChildRepair(pid, [.. parent.Children], placed, dropped));
            if (apply) parent.Children = placed;
        }
        return repairs;
    }

    /// <summary>Greedy longest-first decomposition of <paramref name="s"/> into a run of (2+) distinct member
    /// ids; empty if it doesn't split cleanly into members. Longest-first disambiguates prefix-overlapping ids.</summary>
    private static List<string> TrySplit(string s, HashSet<string> members, HashSet<string> alreadyPlaced)
    {
        var result = new List<string>();
        var remaining = s;
        var candidates = members.Where(m => !alreadyPlaced.Contains(m)).OrderByDescending(m => m.Length).ToList();
        while (remaining.Length > 0)
        {
            var next = candidates.FirstOrDefault(m => !result.Contains(m) && remaining.StartsWith(m, StringComparison.Ordinal));
            if (next is null) return [];
            result.Add(next);
            remaining = remaining[next.Length..];
        }
        return result.Count >= 2 ? result : [];
    }
}

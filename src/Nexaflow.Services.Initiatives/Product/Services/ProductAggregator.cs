using Nexaflow.Services.Initiatives.Product.Model;

namespace Nexaflow.Services.Initiatives.Product.Services;

/// <summary>
/// Pure, derived views over a loaded <see cref="ProductState"/> — the testable core of the survey. A
/// parent's dashboard rolls up the statuses of its <em>descendant leaves</em> (a leaf counts as itself).
/// No UI, no IO; cycle-guarded.
/// </summary>
public static class ProductAggregator
{
    public sealed record StatusCount(int Should, int Shouldnt, int Done, int Faulted)
    {
        public int    Total    => Should + Shouldnt + Done + Faulted;
        public double Fraction => Total == 0 ? 0 : (double)Done / Total;
        public int    Percent  => (int)Math.Round(Fraction * 100);
        public int Of(Status s) => s switch
        {
            Status.Should => Should, Status.Shouldnt => Shouldnt, Status.Done => Done, _ => Faulted
        };
    }

    public sealed record AttentionNode(string Id, string Title, Status Status, string? Note);

    /// <summary>The roots of the tree: nodes with no parent (or a parent that isn't present).</summary>
    public static IEnumerable<string> Roots(ProductState s) =>
        s.Nodes.Where(kv => kv.Value.Parent is null || !s.Nodes.ContainsKey(kv.Value.Parent))
               .Select(kv => kv.Key);

    /// <summary>
    /// The status shown on a node's sunburst arc — derived, not the stored field for parents. A node's own
    /// concern-link statuses count at <em>every</em> level:
    /// <list type="bullet">
    /// <item>A <b>leaf</b> combines its own status with its concern statuses.</item>
    /// <item>A <b>parent</b> combines its own concern statuses with each child's derived status (which already
    /// folds in that child's concerns). The parent's own stored status is ignored.</item>
    /// </list>
    /// Precedence (highest first): <c>faulted</c> → <c>should</c> → <c>done</c> → <c>shouldnt</c>.
    /// </summary>
    public static Status EffectiveStatus(ProductState s, string id) => EffectiveStatus(s, id, []);

    private static Status EffectiveStatus(ProductState s, string id, HashSet<string> seen)
    {
        if (!seen.Add(id) || !s.Nodes.TryGetValue(id, out var node)) return Status.Shouldnt;
        var kids = node.Children.Where(s.Nodes.ContainsKey).ToList();

        var set = new List<Status>(ConcernStatuses(node));         // a node's own concerns count, at any level
        if (kids.Count == 0)
            set.Add(node.Status);                                  // a leaf also carries its own status
        else
            foreach (var c in kids) set.Add(EffectiveStatus(s, c, seen));   // child's effective already folds in its concerns

        return Combine(set);
    }

    /// <summary>The status for the <em>detail pill</em>, which excludes the node's OWN concerns (those are shown
    /// separately as concern boxes): a leaf → its stored status; a parent → the roll-up of its children's
    /// <see cref="EffectiveStatus"/>. (The sunburst arc uses <see cref="EffectiveStatus"/>, which DOES bundle the
    /// node's own concerns.)</summary>
    public static Status DerivedStatusExcludingConcerns(ProductState s, string id)
    {
        if (!s.Nodes.TryGetValue(id, out var node)) return Status.Shouldnt;
        var kids = node.Children.Where(s.Nodes.ContainsKey).ToList();
        return kids.Count == 0 ? node.Status : Combine(kids.Select(c => EffectiveStatus(s, c)));
    }

    private static IEnumerable<Status> ConcernStatuses(ProductNode node) =>
        node.Concerns?.Select(c => c.Status) ?? [];

    /// <summary>Folds a set of statuses to one by precedence: faulted → should → done → shouldnt
    /// (an empty set is treated as shouldnt).</summary>
    public static Status Combine(IEnumerable<Status> statuses)
    {
        bool faulted = false, should = false, done = false;
        foreach (var st in statuses)
            switch (st)
            {
                case Status.Faulted: faulted = true; break;
                case Status.Should:  should  = true; break;
                case Status.Done:    done    = true; break;
            }
        if (faulted) return Status.Faulted;
        if (should)  return Status.Should;
        if (done)    return Status.Done;
        return Status.Shouldnt;
    }

    /// <summary>The descendant leaf ids of <paramref name="id"/> (a leaf yields itself). Cycle-guarded.</summary>
    public static IReadOnlyList<string> Leaves(ProductState s, string id)
    {
        var acc = new List<string>();
        var seen = new HashSet<string>();
        void Walk(string n)
        {
            if (!seen.Add(n) || !s.Nodes.TryGetValue(n, out var node)) return;
            var kids = node.Children.Where(s.Nodes.ContainsKey).ToList();
            if (kids.Count == 0) { acc.Add(n); return; }
            foreach (var c in kids) Walk(c);
        }
        Walk(id);
        return acc;
    }

    /// <summary>Counts of descendant leaves per status — drives the stacked bar and "Shipping done/total".
    /// Each leaf is counted by its <see cref="EffectiveStatus"/> (so a leaf's concerns count).</summary>
    public static StatusCount StatusBreakdown(ProductState s, string id)
    {
        int should = 0, shouldnt = 0, done = 0, faulted = 0;
        foreach (var leaf in Leaves(s, id))
            switch (EffectiveStatus(s, leaf))
            {
                case Status.Should:   should++;   break;
                case Status.Shouldnt: shouldnt++; break;
                case Status.Done:     done++;     break;
                case Status.Faulted:  faulted++;  break;
            }
        return new StatusCount(should, shouldnt, done, faulted);
    }

    /// <summary>(done, total) for a concern tag across descendant leaves — drives "Has tests N/M".</summary>
    public static (int Done, int Total) ConcernBreakdown(ProductState s, string id, string tag)
    {
        var leaves = Leaves(s, id);
        var done = leaves.Count(l =>
            s.Nodes[l].Concerns?.Any(c => c.Tag == tag && c.Status == Status.Done) == true);
        return (done, leaves.Count);
    }

    /// <summary>The faulted <em>sources</em> under <paramref name="id"/>: each descendant node that is itself
    /// faulted — a leaf whose status is faulted, or any node carrying a faulted concern. Ancestors that are
    /// only faulted by roll-up are not repeated (their faulted child is what's listed).</summary>
    public static IReadOnlyList<AttentionNode> NeedsAttention(ProductState s, string id)
    {
        var acc = new List<AttentionNode>();
        var seen = new HashSet<string>();
        void Walk(string n)
        {
            if (!seen.Add(n) || !s.Nodes.TryGetValue(n, out var node)) return;
            if (IsLocallyFaulted(s, n)) acc.Add(new AttentionNode(n, node.Title, Status.Faulted, node.Note));
            foreach (var c in node.Children) Walk(c);
        }
        Walk(id);
        return acc;
    }

    /// <summary>True if the node is faulted in its own right — a leaf with a faulted status, or any node with
    /// a faulted concern link (a parent's stored status is derived, so it never counts on its own).</summary>
    public static bool IsLocallyFaulted(ProductState s, string id)
    {
        if (!s.Nodes.TryGetValue(id, out var node)) return false;
        var isLeaf = !node.Children.Any(s.Nodes.ContainsKey);
        return node.Concerns?.Any(c => c.Status == Status.Faulted) == true
               || (isLeaf && node.Status == Status.Faulted);
    }

    /// <summary>Title path from the root down to <paramref name="id"/> (for the breadcrumb line).</summary>
    public static IReadOnlyList<string> BreadcrumbPath(ProductState s, string id)
    {
        var path = new List<string>();
        var seen = new HashSet<string>();
        var cur  = id;
        while (cur is not null && s.Nodes.TryGetValue(cur, out var node) && seen.Add(cur))
        {
            path.Insert(0, node.Title);
            cur = node.Parent;
        }
        return path;
    }
}

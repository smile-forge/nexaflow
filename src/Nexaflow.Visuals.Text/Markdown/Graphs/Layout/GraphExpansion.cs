using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Layout;

/// <summary>
/// Derives the <i>visible</i> graph from a parsed one plus an expansion state.
/// <para>
/// This is what makes a large graph tractable: the source can describe the whole tree while the
/// diagram only ever lays out the part that is open, and every node standing in front of something
/// hidden is marked <see cref="NodeExpansion.Collapsed"/> so the renderer can offer a way in. The
/// parsed graph is never mutated — a view is derived from it — so expanding and collapsing are both
/// just another derivation rather than an edit that has to be undone.
/// </para>
/// <para>
/// Three things can hide a subtree, and they compose: an <c>expandDepth</c> frontier, an explicit
/// <c>collapsed</c> mark from a producer whose hidden subtree is not in the source at all, and a
/// <c>maxFanOut</c> cap that folds surplus siblings behind one "+N more" node.
/// </para>
/// </summary>
public static class GraphExpansion
{
    /// <summary>Id prefix of the synthetic node that stands in for folded-away siblings. Toggling it
    /// opens its parent's full fan-out.</summary>
    public const string OverflowPrefix = "nexaflow-overflow:";

    /// <summary>The parent whose fan-out an overflow node folds, or null for any other id.</summary>
    public static string? OverflowParent(string nodeId) =>
        nodeId.StartsWith(OverflowPrefix, StringComparison.Ordinal) ? nodeId[OverflowPrefix.Length..] : null;

    /// <summary>The expansion key for the stand-in folding <paramref name="parentKey"/>'s fan-out.</summary>
    public static string OverflowKey(string parentKey) => OverflowPrefix + parentKey;

    /// <summary>
    /// The visible graph for <paramref name="graph"/> under <paramref name="config"/>, with
    /// <paramref name="opened"/> holding whatever the user has since opened by hand.
    /// <para>
    /// Always a fresh graph, even when nothing is hidden. Layout writes back into the edges it is
    /// given (cycle removal flips <see cref="Edge.IsReversed"/>), so handing it a derived copy is
    /// what lets the same parsed graph be laid out again — at a new width, or with one more node
    /// open — and come out the same each time.
    /// </para>
    /// </summary>
    /// <param name="overrides">
    /// What the reader has opened or closed, keyed by the producer's own name for the node (see
    /// <see cref="NexaflowGraphConfig.KeyFor"/>) — never by mermaid id, which is positional and
    /// shifts the moment the graph grows.
    /// </param>
    public static Graph Apply(Graph graph, NexaflowGraphConfig? config,
                              IReadOnlyDictionary<string, bool>? overrides = null)
    {
        var cfg      = config ?? new NexaflowGraphConfig();
        var children = ChildMap(graph);
        var depth    = Depths(graph, children);

        bool Overridden(string id, out bool open)
        {
            open = false;
            return overrides is not null && overrides.TryGetValue(cfg.KeyFor(id), out open);
        }

        // The reader's own opening and closing wins over what the source declared; below that, an
        // explicit mark wins over the depth frontier.
        bool IsOpen(string id) =>
            Overridden(id, out var user) ? user
          : !cfg.Collapsed.ContainsKey(id) &&
            (cfg.Expanded.ContainsKey(id) ||
             cfg.ExpandDepth is not int limit ||
             depth.GetValueOrDefault(id, 0) < limit);

        // Expansion is "in play" for a node only when something asked for it — otherwise an ordinary
        // parent in an ordinary flowchart would sprout a collapse chip it never earned.
        bool Governed(string id) =>
            cfg.ExpandDepth is not null || cfg.Collapsed.ContainsKey(id) || cfg.Expanded.ContainsKey(id) ||
            Overridden(id, out _);

        // ── Walk from the roots, stopping at every closed node ────────────────
        var visible  = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new HashSet<string>(StringComparer.Ordinal);   // visible but closed
        var queue    = new Queue<string>();
        foreach (var root in Roots(graph)) if (visible.Add(root)) queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!IsOpen(id)) { frontier.Add(id); continue; }
            foreach (var child in children.GetValueOrDefault(id, []))
                if (visible.Add(child)) queue.Enqueue(child);
        }

        // ── Fan-out folding, once the visible set is known ────────────────────
        var folded = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (cfg.MaxFanOut > 0)
            FoldFanOut(children, visible, cfg.MaxFanOut,
                       parentId => overrides?.GetValueOrDefault(OverflowKey(cfg.KeyFor(parentId))) == true,
                       folded);

        var hiddenBySibling = folded.SelectMany(kv => kv.Value).ToHashSet(StringComparer.Ordinal);
        var view = new Graph { Title = graph.Title, Direction = graph.Direction, Legend = graph.Legend };

        foreach (var node in graph.Nodes)
        {
            if (!visible.Contains(node.Id) || hiddenBySibling.Contains(node.Id)) continue;
            var copy = node.Copy();

            if (Governed(node.Id))
            {
                bool closed = frontier.Contains(node.Id);
                bool hasSubtree = closed || children.GetValueOrDefault(node.Id, []).Count > 0;
                copy.Expansion = !hasSubtree      ? NodeExpansion.Leaf
                               : closed           ? NodeExpansion.Collapsed
                                                  : NodeExpansion.Expanded;
                if (closed) copy.HiddenCount = HiddenBehind(node.Id, children, visible);
                copy.ExpandKey ??= cfg.KeyFor(node.Id);
            }

            view.Nodes.Add(copy);
        }

        foreach (var (parentId, hiddenIds) in folded)
            view.Nodes.Add(new Node
            {
                Id          = OverflowPrefix + parentId,
                Label       = $"+{hiddenIds.Count} more",
                Shape       = NodeShape.RoundedRect,
                Expansion   = NodeExpansion.Collapsed,
                HiddenCount = hiddenIds.Count,
                Tooltip     = $"Show the {hiddenIds.Count} remaining siblings",
                // Keyed off the parent's stable name, not its mermaid id: opening one of these has to
                // survive the host re-emitting the diagram, which renumbers every id.
                ExpandKey   = OverflowKey(cfg.KeyFor(parentId)),
            });

        foreach (var edge in graph.Edges)
        {
            if (!visible.Contains(edge.SourceId) || !visible.Contains(edge.TargetId)) continue;
            // A folded sibling's edges are replaced by the single edge to its "+N more" stand-in.
            if (hiddenBySibling.Contains(edge.SourceId) || hiddenBySibling.Contains(edge.TargetId)) continue;
            view.Edges.Add(CopyEdge(edge));
        }

        foreach (var parentId in folded.Keys)
            view.AddEdge(parentId, OverflowPrefix + parentId, style: EdgeStyle.Dotted, arrow: EdgeArrow.None);

        // A group survives when it still holds a visible node — or when a group inside it does.
        // The second half matters for deployment diagrams, whose outer nodes contain nothing but
        // more nodes: dropping a group for having no *direct* members deleted the outermost box of
        // every one of them.
        var keptNodes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var sub in graph.Subgraphs)
            keptNodes[sub.Id] = sub.NodeIds.Where(id => visible.Contains(id) && !hiddenBySibling.Contains(id)).ToList();

        var survives = new HashSet<string>(
            keptNodes.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key), StringComparer.Ordinal);

        // Walk parents upward until nothing new survives (a chain of empty groups is only as deep
        // as the subgraph list, so one pass per subgraph is always enough).
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var sub in graph.Subgraphs)
                if (sub.ParentId is string pid && survives.Contains(sub.Id) && survives.Add(pid))
                    grew = true;
        }

        foreach (var sub in graph.Subgraphs)
        {
            if (!survives.Contains(sub.Id)) continue;
            var copy = new Subgraph
            {
                Id = sub.Id, Label = sub.Label, ParentId = sub.ParentId,
                Href = sub.Href, Tooltip = sub.Tooltip, Style = sub.Style?.Copy(),
            };
            copy.NodeIds.AddRange(keptNodes[sub.Id]);
            view.Subgraphs.Add(copy);
        }

        return view;
    }

    // ── Structure helpers ─────────────────────────────────────────────────────

    private static Dictionary<string, List<string>> ChildMap(Graph graph)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var e in graph.Edges)
        {
            if (string.Equals(e.SourceId, e.TargetId, StringComparison.Ordinal)) continue;   // a self-loop hides nothing
            if (!map.TryGetValue(e.SourceId, out var list)) map[e.SourceId] = list = [];
            if (!list.Contains(e.TargetId, StringComparer.Ordinal)) list.Add(e.TargetId);
        }
        return map;
    }

    /// <summary>Nodes nothing points at. A wholly cyclic graph has none, in which case every node is
    /// a root — better a flat diagram than an empty one.</summary>
    private static List<string> Roots(Graph graph)
    {
        var targets = graph.Edges
            .Where(e => !string.Equals(e.SourceId, e.TargetId, StringComparison.Ordinal))
            .Select(e => e.TargetId)
            .ToHashSet(StringComparer.Ordinal);
        var roots = graph.Nodes.Where(n => !targets.Contains(n.Id)).Select(n => n.Id).ToList();
        return roots.Count > 0 ? roots : graph.Nodes.Select(n => n.Id).ToList();
    }

    /// <summary>Breadth-first depth from the roots; a node reached by several paths takes the shortest.</summary>
    private static Dictionary<string, int> Depths(Graph graph, Dictionary<string, List<string>> children)
    {
        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var root in Roots(graph)) { depth[root] = 0; queue.Enqueue(root); }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var child in children.GetValueOrDefault(id, []))
                if (depth.TryAdd(child, depth[id] + 1)) queue.Enqueue(child);
        }
        return depth;
    }

    /// <summary>How many nodes sit behind a closed node and are not reachable any other way.</summary>
    private static int HiddenBehind(string id, Dictionary<string, List<string>> children, HashSet<string> visible)
    {
        var seen  = new HashSet<string>(StringComparer.Ordinal) { id };
        var queue = new Queue<string>();
        queue.Enqueue(id);
        int count = 0;

        while (queue.Count > 0)
            foreach (var child in children.GetValueOrDefault(queue.Dequeue(), []))
            {
                if (visible.Contains(child) || !seen.Add(child)) continue;
                count++;
                queue.Enqueue(child);
            }
        return count;
    }

    /// <summary>
    /// Folds each over-wide sibling set down to <paramref name="max"/> plus a stand-in. Only children
    /// that are leaves of the visible graph are foldable — folding a node that itself has visible
    /// children would orphan them.
    /// </summary>
    private static void FoldFanOut(
        Dictionary<string, List<string>> children, HashSet<string> visible, int max,
        Func<string, bool> unfolded, Dictionary<string, List<string>> folded)
    {
        // A node pointed at by more than one parent is nobody's exclusive sibling; folding it would
        // silently drop the other parent's edge.
        var parentCount = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, kids) in children)
            foreach (var kid in kids)
                parentCount[kid] = parentCount.GetValueOrDefault(kid) + 1;

        foreach (var (parentId, kids) in children)
        {
            if (!visible.Contains(parentId)) continue;
            if (unfolded(parentId)) continue;

            var foldable = kids
                .Where(k => visible.Contains(k)
                         && parentCount.GetValueOrDefault(k) == 1
                         && children.GetValueOrDefault(k, []).All(g => !visible.Contains(g)))
                .ToList();
            if (foldable.Count <= max || kids.Count <= max) continue;

            // Keep the first `max` in source order; the rest go behind the stand-in.
            int keep    = Math.Max(0, max - 1);
            var hidden  = foldable.Skip(keep).ToList();
            if (hidden.Count < 2) continue;   // one node behind a "+1 more" chip is a worse diagram
            folded[parentId] = hidden;
        }
    }

    private static Edge CopyEdge(Edge e) => new()
    {
        SourceId   = e.SourceId,
        TargetId   = e.TargetId,
        Label      = e.Label,
        Style      = e.Style,
        Arrow      = e.Arrow,
        StartArrow = e.StartArrow,
        StartLabel = e.StartLabel,
        EndLabel   = e.EndLabel,
        SubLabel   = e.SubLabel,
        LineColor  = e.LineColor,
        TextColor  = e.TextColor,
        Href       = e.Href,
        Tooltip    = e.Tooltip,
    };
}

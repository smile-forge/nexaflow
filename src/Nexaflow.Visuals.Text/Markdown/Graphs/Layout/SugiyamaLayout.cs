using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Layout;

// ── Result types ──────────────────────────────────────────────────────────────

/// <summary>
/// A node in the layout graph.  Real nodes wrap a <see cref="Node"/>; dummy nodes
/// are inserted by long-edge splitting and have <see cref="Source"/> == null.
/// </summary>
public sealed class LayoutNode
{
    public Node? Source  { get; init; }
    public int   Layer   { get; set; }
    public int   Order   { get; set; }  // position within layer (updated by crossing-min)
    public double X      { get; set; }
    public double Y      { get; set; }
    public double Width  { get; set; }
    public double Height { get; set; }
    public bool IsDummy  { get; init; }
}

/// <summary>
/// A routed edge in the layout graph.  <see cref="Waypoints"/> are screen-space
/// points from the source port through any bend points to the target port.
/// </summary>
public sealed class LayoutEdge
{
    public required LayoutNode From { get; init; }
    public required LayoutNode To   { get; init; }
    /// <summary>The original graph edge (null for intermediate dummy segments — these are merged away before the result is returned).</summary>
    public Edge? Source { get; init; }
    public List<Point> Waypoints { get; } = [];
    /// <summary>Where to anchor the edge's label. Set by parallel-edge separation to stagger the
    /// labels of grouped edges so they don't collide; null → the renderer uses the path midpoint.</summary>
    public Point? LabelAnchor { get; set; }
}

/// <summary>
/// The fully computed layout: positioned nodes grouped by layer and routed edges.
/// </summary>
public sealed class LayoutedGraph
{
    public required Graph Source              { get; init; }
    /// <summary>Layer 0 is topmost (TD) or leftmost (LR).</summary>
    public List<List<LayoutNode>> Layers     { get; } = [];
    public List<LayoutEdge> Edges            { get; } = [];
    public double Width                                  { get; set; }
    public double Height                                 { get; set; }
    public List<(string Label, Rect Bounds)> SubgraphBoxes { get; } = [];
    public IEnumerable<LayoutNode> AllNodes              => Layers.SelectMany(l => l);
}

// ── Algorithm ─────────────────────────────────────────────────────────────────

/// <summary>
/// Sugiyama-style hierarchical graph layout.
///
/// Pipeline:
///   1. Cycle removal (DFS back-edge reversal)
///   2. Longest-path layer assignment
///   3. Dummy node insertion (long-edge splitting)
///   4. Barycenter crossing minimisation (sweep up/down)
///   5. Coordinate assignment
///   6. Waypoint computation + dummy-chain merging
/// </summary>
public static class SugiyamaLayout
{
    // ── Geometry constants (public so renderers can align) ────────────────
    public const double NodeW    = 130;
    public const double NodeH    = 38;
    public const double GapX     = 44;   // horizontal gap between nodes in same layer (TD) / between layers (LR secondary)
    public const double GapY     = 64;   // vertical gap between nodes in same layer (LR)
    public const double GapYTD   = 80;   // vertical gap between layers in TD/BT graphs (+25% over GapY)
    public const double MarginX  = 28;
    public const double MarginY  = 28;
    private const double GapXMin = 12;  // minimum allowed GapX when compacting a wide layout

    // ── Public entry ──────────────────────────────────────────────────────

    /// <param name="preferredMaxWidth">
    /// When &gt; 0 and the layout is not horizontal, GapX will be reduced
    /// (down to <see cref="GapXMin"/>) to try to keep the diagram within this width.
    /// </param>
    public static LayoutedGraph Compute(Graph graph, double preferredMaxWidth = 0)
    {
        if (graph.Nodes.Count == 0)
            return new LayoutedGraph { Source = graph };

        var result = graph.Subgraphs.Count > 0
            ? ComputeClustered(graph, preferredMaxWidth)
            : ComputeFlat(graph, preferredMaxWidth, null);

        bool horizontal = result.Source.Direction is GraphDirection.LeftRight or GraphDirection.RightLeft;
        SeparateParallelEdges(result.Edges, horizontal);
        return result;
    }

    /// <summary>
    /// Cleans up edge endpoints so overlapping lines read clearly: first fans every edge sharing a
    /// node face out across distinct ports (so a lone edge never overlaps a coupled pair leaving the
    /// same node), then for parallel/antiparallel groups (a state-diagram <c>A --&gt; B</c> /
    /// <c>B --&gt; A</c> couple) adds an outward bow + staggered labels so the pair draws as a clean
    /// lens with non-colliding labels.
    /// </summary>
    private static void SeparateParallelEdges(List<LayoutEdge> edges, bool horizontal)
    {
        DistributePorts(edges, horizontal);

        const double Bow = 12.0;   // outward bulge at the middle of each coupled edge
        double px = horizontal ? 0 : 1, py = horizontal ? 1 : 0;   // lateral axis
        double Lat(Point p) => horizontal ? p.Y : p.X;

        var groups = new Dictionary<(LayoutNode, LayoutNode), List<LayoutEdge>>();
        foreach (var e in edges)
        {
            var key = e.From.GetHashCode() <= e.To.GetHashCode() ? (e.From, e.To) : (e.To, e.From);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(e);
        }

        foreach (var group in groups.Values)
        {
            if (group.Count < 2) continue;
            int n = group.Count;
            for (int i = 0; i < n; i++)
            {
                var e = group[i];
                if (e.Waypoints.Count < 2) continue;

                // The edge already leaves its ports on one side of centre (port distribution); bow the
                // middle further out on that same side so the couple opens into a lens.
                double off  = ((Lat(e.Waypoints[0]) - (horizontal ? e.From.Y : e.From.X))
                             + (Lat(e.Waypoints[^1]) - (horizontal ? e.To.Y : e.To.X))) / 2.0;
                double sign = off >= 0 ? 1 : -1;

                if (e.Waypoints.Count == 2)
                {
                    Point a = e.Waypoints[0], b = e.Waypoints[1];
                    e.Waypoints.Insert(1, new Point((a.X + b.X) / 2.0 + px * Bow * sign,
                                                    (a.Y + b.Y) / 2.0 + py * Bow * sign));
                }

                if (e.Source is { Label.Length: > 0 })
                {
                    Point a = e.Waypoints[0], b = e.Waypoints[^1];
                    double t = 0.30 + 0.40 * (i / (n - 1.0));
                    e.LabelAnchor = new Point(a.X + (b.X - a.X) * t + px * Bow * sign,
                                              a.Y + (b.Y - a.Y) * t + py * Bow * sign);
                }
            }
        }
    }

    /// <summary>
    /// Fans the edges attached to each node face out across distinct ports instead of all meeting at
    /// the face centre, so lines leaving/entering the same node don't overlap. Stubs are ordered by
    /// the direction they head (left-bound edges take left ports) and spread within the node's width
    /// (or height, for left-right graphs).
    /// </summary>
    private static void DistributePorts(List<LayoutEdge> edges, bool horizontal)
    {
        const double Pad = 8.0, MaxStep = 26.0;

        var atStart = new Dictionary<LayoutNode, List<LayoutEdge>>();
        var atEnd   = new Dictionary<LayoutNode, List<LayoutEdge>>();
        foreach (var e in edges)
        {
            if (e.Waypoints.Count < 2) continue;
            (atStart.TryGetValue(e.From, out var sl) ? sl : atStart[e.From] = []).Add(e);
            (atEnd.TryGetValue(e.To,    out var el) ? el : atEnd[e.To]      = []).Add(e);
        }

        void Spread(LayoutNode node, List<LayoutEdge> stubs, bool isStart)
        {
            if (stubs.Count < 2) return;

            double Key(LayoutEdge e)
            {
                var adj = isStart ? e.Waypoints[1] : e.Waypoints[^2];
                return horizontal ? adj.Y : adj.X;
            }
            stubs.Sort((a, b) => Key(a).CompareTo(Key(b)));

            double span   = Math.Max(0, (horizontal ? node.Height : node.Width) - 2 * Pad);
            double step    = Math.Min(MaxStep, span / (stubs.Count - 1));
            double center = horizontal ? node.Y : node.X;
            double from    = center - step * (stubs.Count - 1) / 2.0;

            for (int i = 0; i < stubs.Count; i++)
            {
                double pos = from + i * step;
                var e = stubs[i];
                int idx = isStart ? 0 : e.Waypoints.Count - 1;
                var p = e.Waypoints[idx];
                e.Waypoints[idx] = horizontal ? new Point(p.X, pos) : new Point(pos, p.Y);
            }
        }

        foreach (var (node, stubs) in atStart) Spread(node, stubs, isStart: true);
        foreach (var (node, stubs) in atEnd)   Spread(node, stubs, isStart: false);
    }

    /// <summary>The core Sugiyama pipeline with no cluster handling. <paramref name="sizes"/> overrides
    /// node dimensions by id (used to reserve space for collapsed subgraph super-nodes).</summary>
    private static LayoutedGraph ComputeFlat(
        Graph graph, double preferredMaxWidth, IReadOnlyDictionary<string, (double w, double h)>? sizes)
    {
        bool horizontal = graph.Direction is GraphDirection.LeftRight or GraphDirection.RightLeft;

        var work = graph.Edges
            .Select(e => new WorkEdge(e.SourceId, e.TargetId, e))
            .ToList();
        RemoveCycles(graph.Nodes.Select(n => n.Id).ToList(), work);

        var components = FindComponents(graph.Nodes.Select(n => n.Id).ToList(), work);

        if (components.Count <= 1)
            return LayoutComponent(graph, graph.Nodes.Select(n => n.Id).ToHashSet(), work, preferredMaxWidth, sizes);

        var compLayouts = components
            .Select(ids => LayoutComponent(graph, ids, work.Where(e => ids.Contains(e.Src)).ToList(), preferredMaxWidth, sizes))
            .ToList();
        return PackComponents(graph, compLayouts, horizontal);
    }

    // ── Clustered layout (subgraphs) ──────────────────────────────────────

    private const double ClusterPad = 14, ClusterLabelH = 26;

    /// <summary>
    /// Lays out a graph with subgraphs by recursively collapsing each subgraph (and its nested
    /// children) to a sized super-node, laying out one level at a time, then expanding each
    /// super-node back into its internally-laid-out members + box. Nesting is driven by
    /// <see cref="Subgraph.ParentId"/>: flowchart subgraphs (no parent) form a single level,
    /// state composites nest arbitrarily deep. Edges attach to whichever entity owns each endpoint
    /// at the level being laid out.
    /// </summary>
    private static LayoutedGraph ComputeClustered(Graph graph, double preferredMaxWidth)
    {
        // Each node → the subgraph that directly owns it (the parser lists innermost first; first wins).
        var ownerOf = new Dictionary<string, Subgraph>(StringComparer.Ordinal);
        foreach (var sg in graph.Subgraphs)
            foreach (var id in sg.NodeIds)
                ownerOf.TryAdd(id, sg);

        var sgById = new Dictionary<string, Subgraph>(StringComparer.Ordinal);
        foreach (var sg in graph.Subgraphs)
            if (sg.Id.Length > 0) sgById[sg.Id] = sg;

        var childrenOf = new Dictionary<string, List<Subgraph>>(StringComparer.Ordinal);
        var topLevel   = new List<Subgraph>();
        foreach (var sg in graph.Subgraphs)
        {
            // Treat a parent that doesn't resolve (e.g. flowchart) as top level.
            if (sg.ParentId is string pid && sgById.ContainsKey(pid))
            {
                if (!childrenOf.TryGetValue(pid, out var list)) childrenOf[pid] = list = [];
                list.Add(sg);
            }
            else topLevel.Add(sg);
        }

        // The entity that owns `id` at the level identified by `levelId` (null = top level), or null
        // when `id` is outside that level's subtree.
        string? LevelEntity(string id, string? levelId)
        {
            Subgraph? home = sgById.TryGetValue(id, out var asSg) ? asSg
                           : ownerOf.GetValueOrDefault(id);
            if (home is null) return levelId is null ? id : null;   // free node → only at the top level

            for (var cur = home; cur is not null; cur = ResolveParent(cur, sgById))
            {
                if (string.Equals(cur.Id, levelId, StringComparison.Ordinal)) return id;       // direct member
                if (string.Equals(cur.ParentId, levelId, StringComparison.Ordinal)
                    || (cur.ParentId is null && levelId is null)) return cur.Id;                 // child box at this level
            }
            return null;
        }

        // Recursively lay out one level (its direct member nodes + child boxes as super-nodes).
        LayoutedGraph LayoutLevel(string? levelId)
        {
            var children = levelId is null ? topLevel : childrenOf.GetValueOrDefault(levelId, []);

            var inner = new Dictionary<string, LayoutedGraph>(StringComparer.Ordinal);
            var sizes = new Dictionary<string, (double w, double h)>(StringComparer.Ordinal);
            foreach (var child in children)
            {
                var cl = LayoutLevel(child.Id);
                inner[child.Id] = cl;
                var cb = ContentBounds(cl);
                sizes[child.Id] = (cb.Width + 2 * ClusterPad, cb.Height + ClusterLabelH + ClusterPad);
            }

            var level = new Graph { Direction = graph.Direction, Title = levelId is null ? graph.Title : string.Empty };
            foreach (var n in graph.Nodes)
                if (!sgById.ContainsKey(n.Id) &&
                    string.Equals(ownerOf.GetValueOrDefault(n.Id)?.Id, levelId, StringComparison.Ordinal))
                    level.Nodes.Add(n);
            foreach (var child in children)
                level.Nodes.Add(new Node { Id = child.Id, Label = child.Label });

            foreach (var e in graph.Edges)
            {
                string? s = LevelEntity(e.SourceId, levelId);
                string? t = LevelEntity(e.TargetId, levelId);
                if (s is null || t is null || s == t) continue;
                level.AddEdge(s, t, e.Label, e.Style, e.Arrow).StartArrow = e.StartArrow;
            }

            var lg = level.Nodes.Count > 0
                ? ComputeFlat(level, levelId is null ? preferredMaxWidth : 0, sizes)
                : new LayoutedGraph { Source = level };

            // Expand each child super-node back into its members + nested boxes.
            var supers = new HashSet<LayoutNode>();
            var extra  = new List<LayoutNode>();
            foreach (var child in children)
            {
                if (!inner.TryGetValue(child.Id, out var cl)) continue;
                var superLn = lg.AllNodes.FirstOrDefault(n => !n.IsDummy && n.Source?.Id == child.Id);
                if (superLn is null) continue;
                supers.Add(superLn);

                double boxLeft = superLn.X - superLn.Width  / 2.0;
                double boxTop  = superLn.Y - superLn.Height / 2.0;
                var ms = cl.AllNodes.ToList();
                if (ms.Count == 0) continue;
                // Align the whole content block (nodes + nested boxes) so it gets a Pad gap on the
                // left/right/bottom and the header band on top.
                var cb = ContentBounds(cl);
                double dx = boxLeft + ClusterPad    - cb.Left;
                double dy = boxTop  + ClusterLabelH - cb.Top;

                foreach (var n in ms) { n.X += dx; n.Y += dy; }
                foreach (var e in cl.Edges)
                    for (int i = 0; i < e.Waypoints.Count; i++)
                        e.Waypoints[i] = new Point(e.Waypoints[i].X + dx, e.Waypoints[i].Y + dy);
                for (int i = 0; i < cl.SubgraphBoxes.Count; i++)
                {
                    var (lbl, b) = cl.SubgraphBoxes[i];
                    cl.SubgraphBoxes[i] = (lbl, new Rect(b.Left + dx, b.Top + dy, b.Width, b.Height));
                }

                extra.AddRange(ms.Where(n => !n.IsDummy));
                lg.Edges.AddRange(cl.Edges);
                lg.SubgraphBoxes.AddRange(cl.SubgraphBoxes);
                lg.SubgraphBoxes.Add((child.Label, new Rect(boxLeft, boxTop, superLn.Width, superLn.Height)));
            }

            foreach (var layer in lg.Layers) layer.RemoveAll(supers.Contains);
            if (extra.Count > 0) lg.Layers.Add(extra);

            if (lg.AllNodes.Any() || lg.SubgraphBoxes.Count > 0)
            {
                var cb = ContentBounds(lg);
                lg.Width  = Math.Max(lg.Width,  cb.Right  + MarginX);
                lg.Height = Math.Max(lg.Height, cb.Bottom + MarginY);
            }
            return lg;
        }

        return LayoutLevel(null);
    }

    private static Subgraph? ResolveParent(Subgraph sg, Dictionary<string, Subgraph> sgById) =>
        sg.ParentId is string pid ? sgById.GetValueOrDefault(pid) : null;

    /// <summary>The bounding rectangle of a laid-out level — the union of every node bound AND every
    /// nested subgraph box. Including the boxes is what reserves padding around a child composite so it
    /// doesn't sit flush against its parent's edge.</summary>
    private static Rect ContentBounds(LayoutedGraph lg)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var n in lg.AllNodes)
        {
            minX = Math.Min(minX, n.X - n.Width  / 2.0); maxX = Math.Max(maxX, n.X + n.Width  / 2.0);
            minY = Math.Min(minY, n.Y - n.Height / 2.0); maxY = Math.Max(maxY, n.Y + n.Height / 2.0);
        }
        foreach (var (_, b) in lg.SubgraphBoxes)
        {
            minX = Math.Min(minX, b.Left); maxX = Math.Max(maxX, b.Right);
            minY = Math.Min(minY, b.Top);  maxY = Math.Max(maxY, b.Bottom);
        }
        return minX > maxX ? new Rect(0, 0, 0, 0) : new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    // ── Connected-component detection ─────────────────────────────────────

    private static List<HashSet<string>> FindComponents(List<string> nodeIds, List<WorkEdge> edges)
    {
        var adj = nodeIds.ToDictionary(id => id, _ => new List<string>());
        foreach (var e in edges)
        {
            if (adj.TryGetValue(e.Src, out var sl)) sl.Add(e.Dst);
            if (adj.TryGetValue(e.Dst, out var dl)) dl.Add(e.Src);
        }

        var visited    = new HashSet<string>();
        var components = new List<HashSet<string>>();
        foreach (var id in nodeIds)
        {
            if (!visited.Add(id)) continue;
            var comp  = new HashSet<string> { id };
            var queue = new Queue<string>();
            queue.Enqueue(id);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                foreach (var nb in adj.GetValueOrDefault(cur, []))
                    if (visited.Add(nb)) { comp.Add(nb); queue.Enqueue(nb); }
            }
            components.Add(comp);
        }
        return components;
    }

    // ── Single-component layout ───────────────────────────────────────────

    private static LayoutedGraph LayoutComponent(
        Graph graph,
        HashSet<string> nodeIds,
        List<WorkEdge> work,
        double preferredMaxWidth,
        IReadOnlyDictionary<string, (double w, double h)>? sizes = null)
    {
        bool horizontal = graph.Direction is GraphDirection.LeftRight or GraphDirection.RightLeft;
        var  nodeIdList = nodeIds.ToList();

        var layerOf = AssignLayers(nodeIdList, work);
        var lnMap   = BuildLayoutNodes(graph, layerOf, horizontal, nodeIds, sizes);

        var (allLNodes, routes) = InsertDummies(work, lnMap);

        int maxLayer = allLNodes.Count > 0 ? allLNodes.Max(n => n.Layer) : 0;
        var layers   = Enumerable.Range(0, maxLayer + 1)
            .Select(l => allLNodes.Where(n => n.Layer == l).ToList())
            .ToList();

        CrossingMinimise(layers, routes);
        AssignCoordinates(layers, graph.Direction, GapX);

        double w = allLNodes.Count > 0 ? allLNodes.Max(n => n.X + n.Width / 2.0) + MarginX : MarginX * 2;
        if (preferredMaxWidth > 0 && !horizontal && w > preferredMaxWidth)
        {
            int    widestCount = layers.Max(l => l.Count);
            double perNode     = (preferredMaxWidth - 2 * MarginX) / Math.Max(widestCount, 1);
            double maxNodeW    = allLNodes.Where(n => !n.IsDummy).DefaultIfEmpty().Max(n => n?.Width ?? 0);
            double compact     = Math.Max(GapXMin, perNode - maxNodeW);
            if (compact < GapX)
            {
                AssignCoordinates(layers, graph.Direction, compact);
                w = allLNodes.Max(n => n.X + n.Width / 2.0) + MarginX;
            }
        }

        // Centre each layer on the secondary axis relative to the widest layer
        CenterLayers(layers, horizontal);

        // Anchor to the top-left margins so reverse-direction (RL/BU) flips and wide
        // single-node components never sit left of / above the canvas and clip.
        if (allLNodes.Count > 0)
        {
            double dx = MarginX - allLNodes.Min(n => n.X - n.Width / 2.0);
            double dy = MarginY - allLNodes.Min(n => n.Y - n.Height / 2.0);
            foreach (var n in allLNodes) { n.X += dx; n.Y += dy; }
        }

        var    ledges = BuildEdges(routes, graph.Direction);
        w             = allLNodes.Count > 0 ? allLNodes.Max(n => n.X + n.Width / 2.0) + MarginX : MarginX * 2;
        double h      = allLNodes.Count > 0 ? allLNodes.Max(n => n.Y + n.Height / 2.0) + MarginY : MarginY * 2;

        var result = new LayoutedGraph { Source = graph, Width = w, Height = h };
        result.Layers.AddRange(layers);
        result.Edges.AddRange(ledges);
        return result;
    }

    // ── Multi-component packing ───────────────────────────────────────────

    private const double ComponentGap = 1.0;

    /// <summary>
    /// Translates component sub-layouts so they don't overlap, then merges them.
    /// TD graphs are packed left-to-right; LR graphs are packed top-to-bottom.
    /// </summary>
    private static LayoutedGraph PackComponents(
        Graph graph, List<LayoutedGraph> layouts, bool horizontal)
    {
        double offset = 0;

        foreach (var layout in layouts)
        {
            foreach (var n in layout.AllNodes)
            {
                if (horizontal) n.Y += offset;
                else            n.X += offset;
            }
            foreach (var e in layout.Edges)
                for (int i = 0; i < e.Waypoints.Count; i++)
                    e.Waypoints[i] = horizontal
                        ? new Point(e.Waypoints[i].X, e.Waypoints[i].Y + offset)
                        : new Point(e.Waypoints[i].X + offset, e.Waypoints[i].Y);

            offset += (horizontal ? layout.Height : layout.Width) + ComponentGap;
        }

        double totalW = horizontal ? layouts.Max(l => l.Width)  : offset - ComponentGap + MarginX;
        double totalH = horizontal ? offset - ComponentGap + MarginY : layouts.Max(l => l.Height);

        var result = new LayoutedGraph { Source = graph, Width = totalW, Height = totalH };
        foreach (var layout in layouts)
        {
            result.Layers.AddRange(layout.Layers);
            result.Edges.AddRange(layout.Edges);
        }
        return result;
    }

    // ── 1: Cycle removal ──────────────────────────────────────────────────

    private static void RemoveCycles(List<string> nodeIds, List<WorkEdge> edges)
    {
        var visited = new HashSet<string>();
        var onStack = new HashSet<string>();

        void Dfs(string id)
        {
            if (!visited.Add(id)) return;
            onStack.Add(id);
            foreach (var e in edges.Where(e => e.Src == id).ToList())
            {
                if (onStack.Contains(e.Dst))
                    e.Reverse(); // back edge → reverse
                else
                    Dfs(e.Dst);
            }
            onStack.Remove(id);
        }

        foreach (var id in nodeIds) Dfs(id);
    }

    // ── 2: Layer assignment (longest-path from sources) ────────────────────

    private static Dictionary<string, int> AssignLayers(List<string> nodeIds, List<WorkEdge> edges)
    {
        var layer = nodeIds.ToDictionary(id => id, _ => 0);
        var inDeg = nodeIds.ToDictionary(id => id, _ => 0);
        foreach (var e in edges)
            if (inDeg.ContainsKey(e.Dst)) inDeg[e.Dst]++;

        var queue = new Queue<string>(nodeIds.Where(id => inDeg[id] == 0));
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            foreach (var e in edges.Where(e => e.Src == id))
            {
                if (!layer.ContainsKey(e.Dst)) continue;
                int proposed = layer[id] + 1;
                if (proposed > layer[e.Dst]) layer[e.Dst] = proposed;
                if (--inDeg[e.Dst] == 0) queue.Enqueue(e.Dst);
            }
        }
        return layer;
    }

    // ── 3a: Build layout nodes ────────────────────────────────────────────

    private static Dictionary<string, LayoutNode> BuildLayoutNodes(
        Graph graph, Dictionary<string, int> layerOf, bool horizontal,
        HashSet<string>? filter = null,
        IReadOnlyDictionary<string, (double w, double h)>? sizes = null)
    {
        var map = new Dictionary<string, LayoutNode>();
        foreach (var n in graph.Nodes)
        {
            if (filter is not null && !filter.Contains(n.Id)) continue;
            // Width = horizontal extent on screen, Height = vertical extent.
            // Size shapes relative to their label length so text fits.
            int    lineCount = n.Label.Count(c => c == '\n') + 1;
            double labelW   = n.Label.Split('\n').Max(l => l.Length) * 7.5 + 24;
            double w        = Math.Max(NodeW, labelW);
            double h        = Math.Max(NodeH, lineCount * 16.0 + 8);

            if (n.Shape == NodeShape.Diamond)
            {
                // An empty-label diamond is a state-diagram choice — keep it compact.
                double d = n.Label.Length == 0 ? NodeH : Math.Max(NodeH * 2.2, labelW * 0.8);
                w = d * 1.4; h = d;
            }
            if (n.Shape == NodeShape.Circle)
            {
                double d = Math.Max(NodeH * 1.6, labelW * 0.7);
                w = d; h = d;
            }
            if (n.Shape == NodeShape.Hexagon)  { w = Math.Max(NodeW * 1.1, labelW + 24); }

            // State-diagram pseudostates have no label and a fixed footprint.
            if (n.Shape is NodeShape.StateStart or NodeShape.StateEnd) { w = h = 20; }
            // A fork/join bar runs across the flow: short on the primary axis, long on the secondary.
            if (n.Shape == NodeShape.ForkJoin) { if (horizontal) { w = 12; h = 70; } else { w = 70; h = 12; } }

            // Explicit size override (collapsed subgraph super-nodes).
            if (sizes is not null && sizes.TryGetValue(n.Id, out var sz)) { w = sz.w; h = sz.h; }

            map[n.Id] = new LayoutNode
            {
                Source = n,
                Layer  = layerOf.GetValueOrDefault(n.Id, 0),
                Width  = w,
                Height = h,
            };
        }
        return map;
    }

    // ── 3b: Dummy insertion ───────────────────────────────────────────────

    private static (List<LayoutNode> all, List<EdgeRoute> routes) InsertDummies(
        List<WorkEdge> work, Dictionary<string, LayoutNode> lnMap)
    {
        var all    = new List<LayoutNode>(lnMap.Values);
        var routes = new List<EdgeRoute>();

        foreach (var we in work)
        {
            if (!lnMap.TryGetValue(we.Src, out var fromLN) ||
                !lnMap.TryGetValue(we.Dst, out var toLN)) continue;

            int fl = fromLN.Layer, tl = toLN.Layer;
            // Ensure chain always goes from lower → higher layer index
            bool edgeFlipped = fl > tl;
            if (edgeFlipped) (fromLN, toLN, fl, tl) = (toLN, fromLN, tl, fl);

            var chain = new List<LayoutNode> { fromLN };
            for (int l = fl + 1; l < tl; l++)
            {
                var d = new LayoutNode { Source = null, Layer = l, IsDummy = true, Width = 0, Height = 0 };
                all.Add(d);
                chain.Add(d);
            }
            chain.Add(toLN);

            routes.Add(new EdgeRoute(we.Edge, chain, edgeFlipped));
        }

        return (all, routes);
    }

    // ── 4: Crossing minimisation (barycenter) ─────────────────────────────

    private static void CrossingMinimise(List<List<LayoutNode>> layers, List<EdgeRoute> routes)
    {
        // Initialise orders
        for (int l = 0; l < layers.Count; l++)
            for (int i = 0; i < layers[l].Count; i++)
                layers[l][i].Order = i;

        if (layers.Count < 2) return;

        // Build adjacency from edge chains (forward and backward neighbours)
        var fwd  = new Dictionary<LayoutNode, List<LayoutNode>>();
        var back = new Dictionary<LayoutNode, List<LayoutNode>>();

        foreach (var route in routes)
        {
            var chain = route.Chain;
            for (int i = 0; i < chain.Count - 1; i++)
            {
                var from = chain[i]; var to = chain[i + 1];
                if (!fwd.TryGetValue(from, out var fl))  fwd[from]  = fl = [];
                if (!back.TryGetValue(to, out var bl))   back[to]   = bl = [];
                fl.Add(to);
                bl.Add(from);
            }
        }

        int maxL = layers.Count - 1;
        for (int pass = 0; pass < 4; pass++)
        {
            if (pass % 2 == 0) // downward sweep
                for (int l = 1; l <= maxL; l++)
                    SortByBarycenter(layers[l], layers[l - 1], fwd, back, forward: true);
            else               // upward sweep
                for (int l = maxL - 1; l >= 0; l--)
                    SortByBarycenter(layers[l], layers[l + 1], fwd, back, forward: false);
        }
    }

    private static void SortByBarycenter(
        List<LayoutNode> layer, List<LayoutNode> refLayer,
        Dictionary<LayoutNode, List<LayoutNode>> fwd,
        Dictionary<LayoutNode, List<LayoutNode>> back,
        bool forward)
    {
        var refPos = new Dictionary<LayoutNode, int>();
        for (int i = 0; i < refLayer.Count; i++) refPos[refLayer[i]] = i;

        var bary = new Dictionary<LayoutNode, double>();
        foreach (var n in layer)
        {
            var nbrs = (forward ? fwd.GetValueOrDefault(n) : back.GetValueOrDefault(n)) ?? [];
            var valid = nbrs.Where(nb => refPos.ContainsKey(nb)).ToList();
            bary[n] = valid.Count > 0 ? valid.Average(nb => (double)refPos[nb]) : (double)n.Order;
        }

        layer.Sort((a, b) => bary[a].CompareTo(bary[b]));
        for (int i = 0; i < layer.Count; i++) layer[i].Order = i;
    }

    // ── 5a: Post-layout layer centering ──────────────────────────────────

    /// <summary>
    /// After coordinates are assigned, centres each layer on the secondary axis
    /// relative to the overall secondary-axis extent of the widest layer.
    /// For TD graphs this horizontally centres narrow layers; for LR graphs it
    /// vertically centres short layers.
    /// </summary>
    private static void CenterLayers(List<List<LayoutNode>> layers, bool horizontal)
    {
        if (layers.Count == 0) return;

        // Find the overall secondary-axis span across all layers
        double overallMin = double.MaxValue, overallMax = double.MinValue;
        foreach (var layer in layers)
        {
            foreach (var n in layer)
            {
                double lo = horizontal ? n.Y - n.Height / 2.0 : n.X - n.Width  / 2.0;
                double hi = horizontal ? n.Y + n.Height / 2.0 : n.X + n.Width  / 2.0;
                if (lo < overallMin) overallMin = lo;
                if (hi > overallMax) overallMax = hi;
            }
        }
        if (overallMin >= overallMax) return;
        double overallCenter = (overallMin + overallMax) / 2.0;

        // Shift each layer so its secondary-axis midpoint aligns with overallCenter
        foreach (var layer in layers)
        {
            if (layer.Count == 0) continue;
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (var n in layer)
            {
                double nlo = horizontal ? n.Y - n.Height / 2.0 : n.X - n.Width  / 2.0;
                double nhi = horizontal ? n.Y + n.Height / 2.0 : n.X + n.Width  / 2.0;
                if (nlo < lo) lo = nlo;
                if (nhi > hi) hi = nhi;
            }
            double shift = overallCenter - (lo + hi) / 2.0;
            if (Math.Abs(shift) < 0.5) continue;
            foreach (var n in layer)
            {
                if (horizontal) n.Y += shift;
                else            n.X += shift;
            }
        }
    }

    // ── 5b: Coordinate assignment ─────────────────────────────────────────

    private static void AssignCoordinates(
        List<List<LayoutNode>> layers, GraphDirection dir, double effectiveGapX)
    {
        bool horizontal = dir is GraphDirection.LeftRight or GraphDirection.RightLeft;

        // Primary-axis centre per layer, accumulated from each layer's actual extent so tall
        // nodes (e.g. collapsed-subgraph super-nodes) don't overlap the neighbouring layer.
        double primGap     = horizontal ? GapX : GapYTD;
        double primDefault = horizontal ? NodeW : NodeH;
        var primaryCenter  = new double[layers.Count];
        double pcursor     = horizontal ? MarginX : MarginY;
        for (int l = 0; l < layers.Count; l++)
        {
            double extent = layers[l].Count > 0
                ? layers[l].Max(n => horizontal ? n.Width : n.Height)
                : primDefault;
            primaryCenter[l] = pcursor + extent / 2.0;
            pcursor += extent + primGap;
        }

        for (int l = 0; l < layers.Count; l++)
        {
            var ly = layers[l];
            double primary = primaryCenter[l];

            // Secondary axis: nodes are stacked on the perpendicular axis.
            // For LR: stack vertically (Y), spacing = Height + GapY.
            // For TD: stack horizontally (X), spacing = Width + effectiveGapX (compressible).
            double cursor = horizontal ? MarginY : MarginX;
            foreach (var n in ly)
            {
                double halfSize  = horizontal ? n.Height / 2.0 : n.Width / 2.0;
                double secondary = cursor + halfSize;
                cursor += (horizontal ? n.Height : n.Width) + (horizontal ? GapY : effectiveGapX);

                if (horizontal)
                {
                    n.X = primary;
                    n.Y = secondary;
                }
                else
                {
                    n.X = secondary;
                    n.Y = primary;
                }
            }
        }

        // Flip axis for reverse directions
        if (dir is GraphDirection.RightLeft or GraphDirection.BottomUp)
        {
            double maxP = layers.SelectMany(l => l)
                                .Max(n => horizontal ? n.X : n.Y);
            foreach (var n in layers.SelectMany(l => l))
            {
                if (horizontal) n.X = maxP - n.X;
                else            n.Y = maxP - n.Y;
            }
        }
    }

    // ── 6: Build routed edges ─────────────────────────────────────────────

    private static List<LayoutEdge> BuildEdges(List<EdgeRoute> routes, GraphDirection dir)
    {
        bool horizontal = dir is GraphDirection.LeftRight or GraphDirection.RightLeft;
        var result = new List<LayoutEdge>();

        foreach (var route in routes)
        {
            var chain = route.Chain;
            var le    = new LayoutEdge
            {
                From   = chain[0],
                To     = chain[^1],
                Source = route.Edge
            };

            // Source port
            le.Waypoints.Add(OutPort(chain[0], horizontal));
            // Dummy bend points
            for (int i = 1; i < chain.Count - 1; i++)
                le.Waypoints.Add(new Point(chain[i].X, chain[i].Y));
            // Target port
            le.Waypoints.Add(InPort(chain[^1], horizontal));

            result.Add(le);
        }
        return result;
    }

    private static Point OutPort(LayoutNode n, bool horizontal) =>
        horizontal
            ? new Point(n.X + n.Width  / 2.0, n.Y)
            : new Point(n.X,                  n.Y + n.Height / 2.0);

    private static Point InPort(LayoutNode n, bool horizontal) =>
        horizontal
            ? new Point(n.X - n.Width  / 2.0, n.Y)
            : new Point(n.X,                  n.Y - n.Height / 2.0);

    // ── Internal helpers ──────────────────────────────────────────────────

    /// <summary>Mutable directed edge used only during layout computation.</summary>
    private sealed class WorkEdge(string src, string dst, Edge edge)
    {
        public string Src { get; private set; } = src;
        public string Dst { get; private set; } = dst;
        public Edge   Edge { get; } = edge;

        public void Reverse()
        {
            (Src, Dst)      = (Dst, Src);
            Edge.IsReversed = !Edge.IsReversed;
        }
    }

    /// <summary>The chain of layout nodes for one original graph edge (real + dummies).</summary>
    private sealed class EdgeRoute(Edge edge, List<LayoutNode> chain, bool flipped)
    {
        public Edge              Edge    { get; } = edge;
        public List<LayoutNode>  Chain   { get; } = chain;
        public bool              Flipped { get; } = flipped;
    }
}

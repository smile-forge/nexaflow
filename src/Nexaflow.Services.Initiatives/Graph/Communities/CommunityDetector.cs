using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph.Model;

namespace Nexaflow.Services.Initiatives.Graph.Communities;

/// <summary>Tuning for <see cref="CommunityDetector"/>. Relationship weights bias which edges hold a community
/// together (the containment spine dominates so members stay with their file, files with their feature).</summary>
public sealed record CommunityOptions(
    double Resolution = 1.0,
    IReadOnlyDictionary<string, double>? RelationshipWeights = null,
    int MaxLevels = 20,
    int MaxSweeps = 50,
    double MinGain = 1e-9,
    bool SplitDisconnected = true);

/// <summary>
/// Community detection by <b>Louvain</b> modularity optimisation, followed by a connected-components post-split
/// (so a community is never rendered as two disconnected blobs — the cheap 90% of Leiden's guarantee). Fully
/// deterministic: nodes are indexed by sorted id, neighbours are visited in id order, no RNG — so the same graph
/// always yields the same numbering. Communities are renumbered by descending size, so <c>graph.json</c> diffs stay stable.
/// </summary>
public static class CommunityDetector
{
    private static readonly Dictionary<string, double> DefaultWeights = new(StringComparer.Ordinal)
    {
        [EdgeRelationship.Contains] = 4.0,
        [EdgeRelationship.Extends] = 3.0,
        [EdgeRelationship.Implements] = 3.0,
        [EdgeRelationship.Imports] = 2.0,
        [EdgeRelationship.Calls] = 1.5,
        [EdgeRelationship.References] = 1.5,
        [EdgeRelationship.Instantiates] = 1.5,
        [EdgeRelationship.Tests] = 1.5,
        [EdgeRelationship.Documents] = 1.0,
    };

    public static IReadOnlyDictionary<string, int> Detect(KnowledgeGraph graph, CommunityOptions? options = null)
    {
        var opt = options ?? new CommunityOptions();
        var weights = opt.RelationshipWeights ?? DefaultWeights;

        var ids = graph.Nodes.Select(node => node.Id).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var n = ids.Length;
        var result = new Dictionary<string, int>(n, StringComparer.Ordinal);
        if (n == 0) return result;

        var index = new Dictionary<string, int>(n, StringComparer.Ordinal);
        for (var i = 0; i < n; i++) index[ids[i]] = i;

        var pair = new Dictionary<(int, int), double>();
        void AddW(int a, int b, double w)
        {
            if (a == b || w <= 0) return;
            var key = a < b ? (a, b) : (b, a);
            pair[key] = pair.GetValueOrDefault(key) + w;
        }
        foreach (var e in graph.Edges)
            if (index.TryGetValue(e.Source, out var a) && index.TryGetValue(e.Target, out var b))
                AddW(a, b, e.Weight * weights.GetValueOrDefault(e.Relationship, 1.0));
        foreach (var h in graph.HyperEdges)
            for (var i = 0; i < h.Endpoints.Count; i++)
                for (var j = i + 1; j < h.Endpoints.Count; j++)
                    if (index.TryGetValue(h.Endpoints[i].Node, out var a) && index.TryGetValue(h.Endpoints[j].Node, out var b))
                        AddW(a, b, h.Weight * weights.GetValueOrDefault(h.Relationship, 1.0));

        var g0 = WorkGraph.FromPairs(n, pair);

        // ── Louvain: local moving + aggregation, flattened back to the original nodes ──
        var node2comm = Enumerable.Range(0, n).ToArray();
        var g = g0;
        for (var level = 0; level < opt.MaxLevels; level++)
        {
            var comm = LocalMoving(g, opt.Resolution, opt.MaxSweeps, opt.MinGain, out var moved);
            var (agg, compact) = g.Aggregate(comm);
            for (var i = 0; i < n; i++) node2comm[i] = compact[node2comm[i]];
            g = agg;
            if (!moved || agg.N == comm.Length) break;   // nothing improved / no coarsening → done
        }

        // ── Post-split disconnected communities against the ORIGINAL graph ──
        var label = node2comm;
        if (opt.SplitDisconnected) label = SplitComponents(g0, node2comm, n);

        // ── Renumber by descending size (ties: smallest member index) for stable diffs ──
        var members = new Dictionary<int, List<int>>();
        for (var i = 0; i < n; i++)
        {
            if (!members.TryGetValue(label[i], out var list)) members[label[i]] = list = [];
            list.Add(i);
        }
        var renumber = new Dictionary<int, int>();
        var ordered = members.OrderByDescending(kv => kv.Value.Count).ThenBy(kv => kv.Value[0]).ToList();
        for (var r = 0; r < ordered.Count; r++) renumber[ordered[r].Key] = r;

        for (var i = 0; i < n; i++) result[ids[i]] = renumber[label[i]];
        return result;
    }

    private static int[] LocalMoving(WorkGraph g, double resolution, int maxSweeps, double minGain, out bool movedAny)
    {
        var n = g.N;
        var comm = new int[n];
        var sigmaTot = new double[n];
        for (var i = 0; i < n; i++) { comm[i] = i; sigmaTot[i] = g.K[i]; }
        movedAny = false;

        var twoM = 2 * g.M;
        if (twoM <= 0) return comm;

        var neigh = new Dictionary<int, double>();
        for (var sweep = 0; sweep < maxSweeps; sweep++)
        {
            var moved = false;
            for (var i = 0; i < n; i++)
            {
                var ci = comm[i];
                sigmaTot[ci] -= g.K[i];

                neigh.Clear();
                foreach (var (to, w) in g.Adj[i])
                {
                    if (to == i) continue;
                    neigh[comm[to]] = neigh.GetValueOrDefault(comm[to]) + w;
                }

                var bestC = ci;
                var bestGain = neigh.GetValueOrDefault(ci) - (resolution * sigmaTot[ci] * g.K[i] / twoM);
                foreach (var c in neigh.Keys.OrderBy(x => x))   // sorted → deterministic tie-breaking
                {
                    if (c == ci) continue;
                    var gain = neigh[c] - (resolution * sigmaTot[c] * g.K[i] / twoM);
                    if (gain > bestGain + minGain) { bestGain = gain; bestC = c; }
                }

                sigmaTot[bestC] += g.K[i];
                comm[i] = bestC;
                if (bestC != ci) { moved = true; movedAny = true; }
            }
            if (!moved) break;
        }
        return comm;
    }

    private static int[] SplitComponents(WorkGraph g0, int[] comm, int n)
    {
        var inSame = comm;
        var label = new int[n];
        var visited = new bool[n];
        var next = 0;
        var queue = new Queue<int>();
        for (var start = 0; start < n; start++)   // ascending order → deterministic labels
        {
            if (visited[start]) continue;
            var lbl = next++;
            visited[start] = true;
            label[start] = lbl;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                foreach (var (to, _) in g0.Adj[u])
                    if (!visited[to] && inSame[to] == inSame[u]) { visited[to] = true; label[to] = lbl; queue.Enqueue(to); }
            }
        }
        return label;
    }

    /// <summary>A weighted undirected graph: symmetric adjacency (each edge in both endpoints' lists), plus a
    /// self-loop weight and pre-summed degree per node. Self-loops appear only in aggregated levels.</summary>
    private sealed class WorkGraph
    {
        public int N;
        public List<(int To, double W)>[] Adj = default!;
        public double[] Self = default!;
        public double[] K = default!;
        public double M;

        public static WorkGraph FromPairs(int n, Dictionary<(int, int), double> pair)
        {
            var g = new WorkGraph { N = n, Adj = new List<(int, double)>[n], Self = new double[n], K = new double[n] };
            for (var i = 0; i < n; i++) g.Adj[i] = [];
            foreach (var ((a, b), w) in pair) { g.Adj[a].Add((b, w)); g.Adj[b].Add((a, w)); }
            for (var i = 0; i < n; i++) { double s = 0; foreach (var (_, w) in g.Adj[i]) s += w; g.K[i] = s; }
            g.M = 0.5 * g.K.Sum();
            return g;
        }

        public (WorkGraph Agg, int[] Compact) Aggregate(int[] comm)
        {
            var map = new Dictionary<int, int>();
            var compact = new int[N];
            for (var i = 0; i < N; i++)
            {
                if (!map.TryGetValue(comm[i], out var c)) { c = map.Count; map[comm[i]] = c; }
                compact[i] = c;
            }
            var k = map.Count;
            var pair = new Dictionary<(int, int), double>();
            var self = new double[k];
            for (var i = 0; i < N; i++)
            {
                var cu = compact[i];
                self[cu] += Self[i];
                foreach (var (to, w) in Adj[i])
                {
                    var cv = compact[to];
                    if (cu == cv) self[cu] += w * 0.5;              // internal edge seen twice → half each
                    else if (cu < cv) pair[(cu, cv)] = pair.GetValueOrDefault((cu, cv)) + w;
                }
            }

            var agg = new WorkGraph { N = k, Adj = new List<(int, double)>[k], Self = self, K = new double[k] };
            for (var c = 0; c < k; c++) agg.Adj[c] = [];
            foreach (var ((a, b), w) in pair) { agg.Adj[a].Add((b, w)); agg.Adj[b].Add((a, w)); }
            for (var c = 0; c < k; c++) { double s = 0; foreach (var (_, w) in agg.Adj[c]) s += w; agg.K[c] = s + (2 * self[c]); }
            agg.M = 0.5 * agg.K.Sum();
            return (agg, compact);
        }
    }
}

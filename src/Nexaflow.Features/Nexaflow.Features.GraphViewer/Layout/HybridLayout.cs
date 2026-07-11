using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Nexaflow.Features.GraphViewer.Layout;

/// <summary>
/// One node handed to the layout: its id, whether it is a product-tree node, and — when it should stay put — a
/// fixed position. Pinned nodes (the focus and any already-visible node during an incremental expand) are held
/// exactly where they are; free nodes (newly revealed) are force-positioned around them.
/// </summary>
public readonly record struct LayoutNode(string Id, bool IsProduct, double? FixedX = null, double? FixedY = null);

/// <summary>
/// Force-directed layout of a focused neighbourhood: Barnes-Hut repulsion + edge springs + a weak radial gravity
/// that keeps each free node near its hop-ring (so "distance from focus" reads). Pinned nodes never move — for a
/// fresh view only the focus is pinned (at the origin); for an incremental expand every already-visible node is
/// pinned at its current spot so new nodes grow into the gaps. Pure and deterministic (FNV-seeded, fixed
/// iterations, single-threaded) → runs off the UI thread and reproduces exactly.
/// </summary>
internal static class HybridLayout
{
    private const double RingGap = 138;    // rings pulled in so the neighbourhood sits closer to the focus
    private const double K = 92;           // ideal edge length
    private const double Theta2 = 0.81;    // θ² = 0.9²
    private const int Iterations = 260;
    private const double Cool0 = 180;

    public static Dictionary<string, (double X, double Y)> Compute(
        IReadOnlyList<LayoutNode> nodes,
        IReadOnlyList<(string A, string B)> edges,
        IReadOnlyDictionary<string, int> hop,
        CancellationToken ct)
    {
        var order = nodes.OrderBy(n => n.Id, StringComparer.Ordinal).ToArray();
        var n = order.Length;
        var idx = new Dictionary<string, int>(n, StringComparer.Ordinal);
        for (var i = 0; i < n; i++) idx[order[i].Id] = i;

        var x = new double[n];
        var y = new double[n];
        var pinned = new bool[n];
        var ring = new double[n];

        for (var i = 0; i < n; i++)
        {
            var h = hop.GetValueOrDefault(order[i].Id, 1);
            ring[i] = Math.Max(h, 1) * RingGap;
            if (order[i].FixedX is { } fixX && order[i].FixedY is { } fixY)
            {
                x[i] = fixX; y[i] = fixY; pinned[i] = true;
                continue;
            }
            // Seed free nodes on their hop-ring: a 24-bit angle + radial jitter from the id's FNV hash, so distinct
            // ids (almost) never coincide (which the quadtree would otherwise choke on).
            var hash = Fnv(order[i].Id);
            var angle = (hash & 0xFFFFFF) / (double)0xFFFFFF * 2 * Math.PI;
            var jitter = ((hash >> 24) & 0xFF) / 255.0 * 30.0;
            x[i] = (ring[i] + jitter) * Math.Cos(angle);
            y[i] = (ring[i] + jitter) * Math.Sin(angle);
        }

        var links = new List<(int A, int B)>(edges.Count);
        foreach (var (a, b) in edges)
            if (idx.TryGetValue(a, out var ia) && idx.TryGetValue(b, out var ib) && ia != ib)
                links.Add((ia, ib));

        var fx = new double[n];
        var fy = new double[n];
        var k2 = K * K;

        for (var it = 0; it < Iterations; it++)
        {
            if ((it & 15) == 0) ct.ThrowIfCancellationRequested();
            Array.Clear(fx);
            Array.Clear(fy);

            // Repulsion (Barnes-Hut): every node repels; only free nodes are pushed.
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            for (var i = 0; i < n; i++)
            {
                if (x[i] < minX) minX = x[i];
                if (y[i] < minY) minY = y[i];
                if (x[i] > maxX) maxX = x[i];
                if (y[i] > maxY) maxY = y[i];
            }
            var half = (Math.Max(maxX - minX, maxY - minY) / 2) + 1;
            var tree = new Quadtree((minX + maxX) / 2, (minY + maxY) / 2, half);
            for (var i = 0; i < n; i++) tree.Insert(x[i], y[i]);
            for (var i = 0; i < n; i++)
            {
                if (pinned[i]) continue;
                double rfx = 0, rfy = 0;
                tree.Repulsion(x[i], y[i], k2, Theta2, ref rfx, ref rfy);
                fx[i] += rfx;
                fy[i] += rfy;
            }

            // Attraction along edges (~ d²/k), applied to whichever endpoint is free.
            foreach (var (a, b) in links)
            {
                var dx = x[b] - x[a];
                var dy = y[b] - y[a];
                var d = Math.Sqrt((dx * dx) + (dy * dy)) + 1e-6;
                var f = d * d / K;
                double ux = dx / d * f, uy = dy / d * f;
                if (!pinned[a]) { fx[a] += ux; fy[a] += uy; }
                if (!pinned[b]) { fx[b] -= ux; fy[b] -= uy; }
            }

            // Weak radial gravity (toward the node's hop-ring) + centre gravity.
            for (var i = 0; i < n; i++)
            {
                if (pinned[i]) continue;
                var d = Math.Sqrt((x[i] * x[i]) + (y[i] * y[i])) + 1e-6;
                var gr = (ring[i] - d) * 0.032;   // firmer ring adherence → hop-1 groups stay a uniform distance in
                fx[i] += (x[i] / d * gr) - (x[i] * 0.004);
                fy[i] += (y[i] / d * gr) - (y[i] * 0.004);
            }

            var temp = Cool0 * (1.0 - ((double)it / Iterations));
            for (var i = 0; i < n; i++)
            {
                if (pinned[i]) continue;
                var d = Math.Sqrt((fx[i] * fx[i]) + (fy[i] * fy[i])) + 1e-6;
                var step = Math.Min(d, temp);
                x[i] += fx[i] / d * step;
                y[i] += fy[i] / d * step;
            }
        }

        var result = new Dictionary<string, (double, double)>(n, StringComparer.Ordinal);
        for (var i = 0; i < n; i++) result[order[i].Id] = (x[i], y[i]);
        return result;
    }

    private static uint Fnv(string s)
    {
        uint h = 2166136261;
        foreach (var c in s) { h ^= c; h *= 16777619; }
        return h;
    }
}

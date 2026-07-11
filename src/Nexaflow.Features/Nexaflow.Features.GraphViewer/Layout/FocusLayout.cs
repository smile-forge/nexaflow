using System;
using System.Collections.Generic;
using Nexaflow.Features.GraphViewer.ViewModels;

namespace Nexaflow.Features.GraphViewer.Layout;

/// <summary>
/// Radial-by-hop layout for a focused neighbourhood: the focus node sits at the centre and each node is placed on
/// the ring for its BFS distance from it, spread evenly around that ring. Deterministic (nodes arrive in a stable
/// id order) — a genuine layout for the bounded neighbourhood, ahead of the full hybrid radial+force pass.
/// </summary>
internal static class FocusLayout
{
    private const double RingGap = 138.0;

    public static void Apply(IReadOnlyList<GraphNodeViewModel> nodes, IReadOnlyDictionary<string, int> distance)
    {
        var byRing = new Dictionary<int, List<GraphNodeViewModel>>();
        foreach (var n in nodes)
        {
            var d = distance.TryGetValue(n.Id, out var v) ? v : 1;
            if (!byRing.TryGetValue(d, out var list)) byRing[d] = list = [];
            list.Add(n);
        }

        foreach (var (ring, list) in byRing)
        {
            if (ring == 0)
            {
                foreach (var n in list) n.SnapTo(-n.Size / 2, -n.Size / 2);
                continue;
            }

            var radius = ring * RingGap;
            for (var i = 0; i < list.Count; i++)
            {
                // Per-ring angular offset so successive rings don't line their nodes up radially.
                var angle = (2 * Math.PI * i / list.Count) + (ring * 0.55);
                var n = list[i];
                n.SnapTo((radius * Math.Cos(angle)) - (n.Size / 2), (radius * Math.Sin(angle)) - (n.Size / 2));
            }
        }
    }
}

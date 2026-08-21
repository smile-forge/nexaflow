using System.Text;
using Nexaflow.Services.Initiatives.Graph.Model;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// One rendering of each <see cref="GraphQuery"/> result, as text — the graph half of what
/// <see cref="Product.Services.ProductReport"/> does for the tree, and for the same reason: the CLI and the
/// in-app assistant ask the same questions and must get the same answer back.
/// </summary>
public static class GraphReport
{
    /// <summary>One compact line for a node: type (+ code kind), label, file:line, then its id underneath —
    /// the id is what every other call takes, so it is always present but never leads.</summary>
    public static string NodeLine(GraphNode n)
    {
        var kind = (n.Type is NodeType.Type or NodeType.Member)
                && n.Metadata?.GetValueOrDefault("kind") is { Length: > 0 } k ? "/" + k : "";
        var loc = n.FilePath is { Length: > 0 } f
            ? $"  ({f}{(n.Metadata?.GetValueOrDefault("line") is { Length: > 0 } ln ? ":" + ln : "")})"
            : "";
        return $"  [{n.Type}{kind}] {n.Label}{loc}\n      {n.Id}";
    }

    public static string Search(IReadOnlyList<GraphNode> hits, string term, int limit)
    {
        if (hits.Count == 0) return $"No graph nodes match '{term}'.";

        var sb = new StringBuilder();
        foreach (var n in hits.Take(limit)) sb.AppendLine(NodeLine(n));
        sb.Append($"{hits.Count} match(es)"
                + (hits.Count > limit ? $" - showing {limit} (raise the limit or narrow by type)" : "") + ".");
        return sb.ToString();
    }

    /// <summary>
    /// A node and every edge it takes part in, in full: each related node's <b>id</b> alongside its label,
    /// because the id is what every other call takes, and each edge's confidence when it is inferred rather
    /// than extracted. <paramref name="near"/> bounds each relationship group separately, so one huge group
    /// cannot crowd out the rest.
    /// </summary>
    public static string Node(GraphQuery.Neighbourhood h, int near = 12)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Identity(h.Node));

        void Groups(char arrow, IReadOnlyList<GraphQuery.RelationGroup> groups)
        {
            foreach (var g in groups)
            {
                sb.AppendLine($"  {arrow} {g.Relationship} ({g.Items.Count}):");
                foreach (var r in g.Items.Take(near))
                    sb.AppendLine($"      {r.Node.Label}{(r.Confidence < 0.999 ? $" ~{r.Confidence:0.##}" : "")}  {r.Node.Id}");
                if (g.Items.Count > near) sb.AppendLine($"      … +{g.Items.Count - near} more (raise the limit)");
            }
        }

        Groups('→', h.Outgoing);
        Groups('←', h.Incoming);
        foreach (var hyper in h.HyperEdges.Take(near))
            sb.AppendLine($"  ⬡ {hyper.Relationship}: "
                        + string.Join(", ", hyper.Endpoints.Select(p => $"{p.Role}={LabelOf(h, p.Node)}")));
        if (h.HyperEdges.Count > near) sb.AppendLine($"  ⬡ … +{h.HyperEdges.Count - near} more");

        if (h.Outgoing.Count == 0 && h.Incoming.Count == 0 && h.HyperEdges.Count == 0)
            sb.AppendLine("  (no edges)");
        return sb.ToString().TrimEnd();
    }

    /// <summary>A hyperedge endpoint names a node id; show the label when the neighbourhood already knows it.</summary>
    private static string LabelOf(GraphQuery.Neighbourhood h, string id)
    {
        if (h.Node.Id == id) return h.Node.Label;
        foreach (var g in h.Outgoing.Concat(h.Incoming))
            foreach (var r in g.Items)
                if (r.Node.Id == id) return r.Node.Label;
        return id;
    }

    public static string Context(GraphQuery.NodeContext c, int near = 6)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Identity(c.Neighbourhood.Node));

        if (c.Source is { } src)
        {
            sb.AppendLine($"  --- source {src.RelativePath}:{src.StartLine}-{src.EndLine} ---");
            for (var i = 0; i < src.Lines.Count; i++)
                sb.AppendLine($"  {src.StartLine + i,5}  {src.Lines[i]}");
            if (src.MoreLines > 0)
                sb.AppendLine($"  … +{src.MoreLines} more line(s) — graph code {c.Neighbourhood.Node.Id}");
        }

        AppendRelations(sb, c.Neighbourhood, near);

        if (c.OwningFeatures.Count > 0)
            sb.AppendLine("  owning feature(s): "
                        + string.Join(", ", c.OwningFeatures.Select(o => $"{o.Label} <{o.Id}>")));

        // The grep anchor, spelled out. Knowing a feature's node is not the same as knowing how to search its
        // code, and the gap between the two is where someone gives up and reaches for a blanket text search.
        if (c.OwnedFiles.Count > 0)
        {
            var shown = c.OwnedFiles.Take(6).ToList();
            sb.AppendLine($"  owns {c.OwnedFiles.Count} file(s): " + string.Join(", ", shown)
                        + (c.OwnedFiles.Count > shown.Count ? $", +{c.OwnedFiles.Count - shown.Count} more" : ""));
            sb.AppendLine($"  search them: graph grep <regex> --from {c.Neighbourhood.Node.Id} --scope owned --mode content");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>The neighbourhood grouped by distance. <paramref name="perHop"/> caps each hop band separately
    /// so the near ones, which are the interesting ones, are never crowded out by a huge outer band.</summary>
    public static string Walk(IReadOnlyList<(GraphNode Node, int Hops)> reached, string fromLabel, int hops,
                              int perHop = int.MaxValue)
    {
        if (reached.Count == 0) return $"Nothing within {hops} hop(s) of '{fromLabel}'.";

        var sb = new StringBuilder();
        foreach (var group in reached.GroupBy(r => r.Hops).OrderBy(g => g.Key))
        {
            sb.AppendLine($"— hop {group.Key} ({group.Count()}) —");
            foreach (var (node, _) in group.Take(perHop)) sb.AppendLine(NodeLine(node));
        }
        sb.Append($"{reached.Count} node(s) within {hops} hop(s) of {fromLabel}"
                + (reached.Count > perHop ? " (per-hop capped by --limit)" : "") + ".");
        return sb.ToString();
    }

    public static string Grep(IReadOnlyList<GraphQuery.GrepHit> hits, string pattern, string? fromId,
                             GraphQuery.GrepScope scope = GraphQuery.GrepScope.Hops)
    {
        var where = fromId is { Length: > 0 }
            ? scope == GraphQuery.GrepScope.Owned ? $" in the files owned by '{fromId}'" : $" near '{fromId}'"
            : "";
        if (hits.Count == 0) return $"No source matches /{pattern}/{where}.";

        var sb = new StringBuilder();
        foreach (var byNode in hits.GroupBy(h => h.Node.Id))
        {
            var node = byNode.First().Node;
            sb.AppendLine($"  [{node.Type}] {node.Label}  ({node.FilePath})");
            foreach (var h in byNode) sb.AppendLine($"      {h.Line,5}  {h.Text}");
        }
        sb.Append($"{hits.Count} match(es) for /{pattern}/{where}.");
        return sb.ToString();
    }

    public static string Source(GraphNode node, GraphQuery.SourceBlock? block)
    {
        if (block is null)
            return node.FilePath is { Length: > 0 }
                ? $"Could not read {node.FilePath} for '{node.Id}'."
                : $"'{node.Id}' is not a code node - it has no source.";

        var sb = new StringBuilder();
        sb.AppendLine($"{block.RelativePath}:{block.StartLine}-{block.EndLine}  [{node.Type}] {node.Label}");
        for (var i = 0; i < block.Lines.Count; i++)
            sb.AppendLine($"{block.StartLine + i,5}  {block.Lines[i]}");
        if (block.MoreLines > 0) sb.Append($"... +{block.MoreLines} more line(s).");
        return sb.ToString().TrimEnd();
    }

    public static string Stats(KnowledgeGraph g)
    {
        var byType  = g.Nodes.GroupBy(n => n.Type).OrderByDescending(x => x.Count());
        var byRel   = g.Edges.GroupBy(e => e.Relationship).OrderByDescending(x => x.Count());
        var byHyper = g.HyperEdges.GroupBy(h => h.Relationship).OrderByDescending(x => x.Count());
        var comm    = g.Nodes.Where(n => n.Community is not null).GroupBy(n => n.Community!.Value)
                             .OrderByDescending(x => x.Count()).Take(12);

        var sb = new StringBuilder();
        sb.AppendLine($"{g.Metadata.ProductName ?? "graph"} - {g.Nodes.Count:N0} nodes, {g.Edges.Count:N0} edges, "
                    + $"{g.HyperEdges.Count:N0} hyperedges, {g.Metadata.CommunityCount} communities (scope {g.Metadata.Scope}).");
        sb.AppendLine("nodes:  " + string.Join("  ", byType.Select(x => $"{x.Key}={x.Count():N0}")));
        sb.AppendLine("edges:  " + string.Join("  ", byRel.Select(x => $"{x.Key}={x.Count():N0}")));
        if (g.HyperEdges.Count > 0)
            sb.AppendLine("hyper:  " + string.Join("  ", byHyper.Select(x => $"{x.Key}={x.Count():N0}")));
        sb.AppendLine("biggest communities: " + string.Join("  ", comm.Select(x =>
        {
            var rep = x.OrderBy(n => n.Type == NodeType.Product ? 0 : 1).ThenBy(n => n.Label?.Length ?? 99).First();
            return $"#{x.Key}={x.Count()}({rep.Label})";
        })));
        sb.Append("next:   graph search <term> | graph node product:<feature> | graph list --type file");
        return sb.ToString();
    }

    // ── Shared pieces ─────────────────────────────────────────────────────────

    private static string Identity(GraphNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{node.Id}");
        sb.AppendLine($"  [{node.Type}]  {node.Label}");
        if (node.FilePath is { Length: > 0 } fp)
        {
            var line = node.Metadata?.GetValueOrDefault("line") is { Length: > 0 } l ? ":" + l : "";
            sb.AppendLine($"  file:      {fp}{line}");
        }
        if (node.Language is { Length: > 0 }) sb.AppendLine($"  language:  {node.Language}");
        if (node.Community is { } cm) sb.AppendLine($"  community: #{cm}");
        if (node.Confidence < 0.999) sb.AppendLine($"  confidence:{node.Confidence:0.##}");
        if (node.Metadata is { Count: > 0 } md)
            foreach (var kv in md.Where(k => k.Key is not ("line" or "ast")))
                sb.AppendLine($"  {kv.Key}: {kv.Value}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>The one-line-per-relationship summary the context view uses: labels only, no ids. The full
    /// form with ids and confidences is <see cref="Node"/> - context is the overview, node is the detail.</summary>
    private static void AppendRelations(StringBuilder sb, GraphQuery.Neighbourhood h, int near)
    {
        foreach (var g in h.Outgoing)
            sb.AppendLine($"  → {g.Relationship} ({g.Items.Count}): "
                        + string.Join(", ", g.Items.Take(near).Select(r => r.Node.Label))
                        + (g.Items.Count > near ? " …" : ""));
        foreach (var g in h.Incoming)
            sb.AppendLine($"  ← {g.Relationship} ({g.Items.Count}): "
                        + string.Join(", ", g.Items.Take(near).Select(r => r.Node.Label))
                        + (g.Items.Count > near ? " …" : ""));
        foreach (var hyper in h.HyperEdges.Take(near))
            sb.AppendLine($"  ⬡ {hyper.Relationship}: "
                        + string.Join(", ", hyper.Endpoints.Select(p => $"{p.Role}={LabelOf(h, p.Node)}")));
    }

    /// <summary>
    /// The orphan list. Deliberately reads as a lead rather than a verdict: the closing line says what the
    /// graph cannot see, because a reader who takes "0 incoming edges" as proof will delete something a
    /// serializer or a DI container was using.
    /// </summary>
    public static string Orphans(IReadOnlyList<GraphQuery.Orphan> orphans, string type, bool includeExcused)
    {
        var sb = new StringBuilder();
        var noun = type == NodeType.Type ? "type" : "member";

        if (orphans.Count == 0)
        {
            sb.AppendLine($"No unreached {noun}s found.");
        }
        else
        {
            foreach (var o in orphans)
            {
                var where = o.Node.FilePath is { } f
                    ? $"{f}:{o.Node.Metadata?.GetValueOrDefault("line") ?? "?"}"
                    : "";
                sb.AppendLine($"  {o.Node.Label,-42} {where}");
                sb.AppendLine($"      {o.Node.Id}");
                if (o.Excuse is { } excuse) sb.AppendLine($"      likely reached anyway - {excuse}");
            }
            sb.AppendLine();
            sb.AppendLine($"{orphans.Count} {noun}(s) with no incoming reference"
                        + (includeExcused ? ", including ones with a known reason." : "."));
        }

        sb.Append("Nothing calls, constructs, extends, tests or snaplinks these. Worth a look - not proof: "
                + "edges are name-resolved, and anything reached only at runtime (reflection, DI, a serializer "
                + "reading properties) leaves no edge to find."
                + (includeExcused ? "" : " Add --all to also list the ones with a known reason."));
        return sb.ToString();
    }
}

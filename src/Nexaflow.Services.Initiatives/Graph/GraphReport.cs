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

    public static string Node(GraphQuery.Neighbourhood h, int near = 12)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Identity(h.Node));
        AppendRelations(sb, h, near);
        return sb.ToString().TrimEnd();
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
                sb.AppendLine($"  ... +{src.MoreLines} more line(s) - read the whole block with the code tool");
        }

        AppendRelations(sb, c.Neighbourhood, near);

        if (c.OwningFeatures.Count > 0)
            sb.AppendLine("  owning feature(s): "
                        + string.Join(", ", c.OwningFeatures.Select(o => $"{o.Label} <{o.Id}>")));

        return sb.ToString().TrimEnd();
    }

    public static string Walk(IReadOnlyList<(GraphNode Node, int Hops)> reached, string fromId, int hops)
    {
        if (reached.Count == 0) return $"Nothing within {hops} hop(s) of '{fromId}'.";

        var sb = new StringBuilder();
        foreach (var group in reached.GroupBy(r => r.Hops).OrderBy(g => g.Key))
        {
            sb.AppendLine($"  -- {group.Key} hop(s) --");
            foreach (var (node, _) in group) sb.AppendLine(NodeLine(node));
        }
        sb.Append($"{reached.Count} node(s) within {hops} hop(s) of '{fromId}'.");
        return sb.ToString();
    }

    public static string Grep(IReadOnlyList<GraphQuery.GrepHit> hits, string pattern, string? fromId)
    {
        var where = fromId is { Length: > 0 } ? $" near '{fromId}'" : "";
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
        var sb = new StringBuilder();
        sb.AppendLine($"{g.Nodes.Count:N0} node(s), {g.Edges.Count:N0} edge(s), {g.HyperEdges.Count:N0} hyperedge(s).");
        foreach (var byType in g.Nodes.GroupBy(n => n.Type).OrderBy(x => GraphQuery.TypeRank(x.Key)))
            sb.AppendLine($"  {byType.Key,-10} {byType.Count():N0}");
        foreach (var byRel in g.Edges.GroupBy(e => e.Relationship).OrderByDescending(x => x.Count()).Take(12))
            sb.AppendLine($"  -> {byRel.Key,-18} {byRel.Count():N0}");
        return sb.ToString().TrimEnd();
    }

    // ── Shared pieces ─────────────────────────────────────────────────────────

    private static string Identity(GraphNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{node.Id}");
        sb.AppendLine($"  [{node.Type}] {node.Label}");
        if (node.FilePath is { Length: > 0 } fp)
        {
            var line = node.Metadata?.GetValueOrDefault("line") is { Length: > 0 } l ? ":" + l : "";
            sb.AppendLine($"  file: {fp}{line}");
        }
        if (node.Community is { } cm) sb.AppendLine($"  community: #{cm}");
        if (node.Metadata is { Count: > 0 } md)
            foreach (var kv in md.Where(k => k.Key is not ("line" or "ast")))
                sb.AppendLine($"  {kv.Key}: {kv.Value}");
        return sb.ToString().TrimEnd();
    }

    private static void AppendRelations(StringBuilder sb, GraphQuery.Neighbourhood h, int near)
    {
        foreach (var g in h.Outgoing)
            sb.AppendLine($"  -> {g.Relationship} ({g.Nodes.Count}): "
                        + string.Join(", ", g.Nodes.Take(near).Select(n => n.Label))
                        + (g.Nodes.Count > near ? " ..." : ""));
        foreach (var g in h.Incoming)
            sb.AppendLine($"  <- {g.Relationship} ({g.Nodes.Count}): "
                        + string.Join(", ", g.Nodes.Take(near).Select(n => n.Label))
                        + (g.Nodes.Count > near ? " ..." : ""));
        foreach (var hyper in h.HyperEdges.Take(near))
            sb.AppendLine($"  (o) {hyper.Relationship}: "
                        + string.Join(", ", hyper.Endpoints.Select(p => $"{p.Role}={p.Node}")));
    }
}

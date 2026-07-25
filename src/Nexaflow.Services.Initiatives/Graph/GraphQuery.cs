using System.Text;
using System.Text.RegularExpressions;
using Nexaflow.Services.Initiatives.Graph.Model;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// Reading a built knowledge graph: search, one node's neighbourhood, the one-shot context view, an N-hop
/// walk, and grep over the source of nodes near a starting point.
/// <para>
/// This is the code-discovery surface CLAUDE.md calls the primary way to explore the repo, so it cannot live
/// in the CLI's <c>Program.cs</c> where only a terminal can reach it — the in-app assistant needs exactly the
/// same answers. Everything here is a pure function of a loaded <see cref="KnowledgeGraph"/> plus a callback
/// for reading source, so neither caller has to own the other's I/O.
/// </para>
/// </summary>
public static class GraphQuery
{
    /// <summary>Reads a repo-relative file as lines, or null when it cannot be read. Supplied by the caller
    /// because the CLI resolves against the working tree first and the app against the product root.</summary>
    public delegate string[]? ReadLines(string relativePath);

    // ── Search ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Nodes whose id or label contains <paramref name="term"/>, best match first: an exact label, then a
    /// prefix, then a label substring, then id-only — so searching a type name finds the type before the
    /// hundred members that mention it.
    /// </summary>
    public static IReadOnlyList<GraphNode> Search(KnowledgeGraph g, string term, string? type = null)
    {
        int Rank(GraphNode n) =>
            n.Label is null ? 3
            : n.Label.Equals(term, StringComparison.OrdinalIgnoreCase) ? 0
            : n.Label.StartsWith(term, StringComparison.OrdinalIgnoreCase) ? 1
            : n.Label.Contains(term, StringComparison.OrdinalIgnoreCase) ? 2 : 3;

        return [.. g.Nodes
            .Where(n => (type is null || n.Type == type)
                     && (n.Id.Contains(term, StringComparison.OrdinalIgnoreCase)
                      || (n.Label?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)))
            .OrderBy(Rank).ThenBy(n => TypeRank(n.Type))
            .ThenBy(n => n.Label?.Length ?? int.MaxValue)
            .ThenBy(n => n.Id, StringComparer.Ordinal)];
    }

    /// <summary>Product before type before file before member — the order someone exploring wants them.</summary>
    public static int TypeRank(string type) => type switch
    {
        NodeType.Product => 0, NodeType.Type => 1, NodeType.File => 2, NodeType.Member => 3, _ => 4,
    };

    // ── Neighbourhood ─────────────────────────────────────────────────────────

    /// <summary>One node's edges in both directions, grouped by relationship, plus the hyperedges it takes
    /// part in.</summary>
    public sealed record Neighbourhood(
        GraphNode Node,
        IReadOnlyList<RelationGroup> Outgoing,
        IReadOnlyList<RelationGroup> Incoming,
        IReadOnlyList<GraphHyperEdge> HyperEdges);

    public sealed record RelationGroup(string Relationship, IReadOnlyList<GraphNode> Nodes);

    public static Neighbourhood? Node(KnowledgeGraph g, string id)
    {
        var byId = Index(g);
        if (!byId.TryGetValue(id, out var node)) return null;

        IReadOnlyList<RelationGroup> Group(IEnumerable<GraphEdge> edges, Func<GraphEdge, string> other) =>
            [.. edges.GroupBy(e => e.Relationship)
                     .OrderBy(x => x.Key, StringComparer.Ordinal)
                     .Select(x => new RelationGroup(x.Key,
                         [.. x.Select(e => byId.GetValueOrDefault(other(e))).Where(n => n is not null)!]))];

        return new Neighbourhood(
            node,
            Group(g.Edges.Where(e => e.Source == id), e => e.Target),
            Group(g.Edges.Where(e => e.Target == id), e => e.Source),
            [.. g.HyperEdges.Where(h => h.Endpoints.Any(p => p.Node == id))]);
    }

    // ── Context (the one-shot view) ───────────────────────────────────────────

    /// <summary>A node, its source, its immediate relationships and the product features that own it — the
    /// single call that answers "what is this and what is it part of".</summary>
    public sealed record NodeContext(
        Neighbourhood Neighbourhood,
        SourceBlock? Source,
        IReadOnlyList<GraphNode> OwningFeatures);

    public sealed record SourceBlock(string RelativePath, int StartLine, int EndLine, int MoreLines,
                                     IReadOnlyList<string> Lines);

    public static NodeContext? Context(KnowledgeGraph g, string id, ReadLines read, int sourceLines = 60)
    {
        var hood = Node(g, id);
        if (hood is null) return null;

        var byId = Index(g);
        // The nearest product nodes within three hops — a code node's "owning feature(s)", which is the thing
        // you actually want to know before changing it.
        var dist = Bfs(Adjacency(g), id, 3);
        var owners = dist.Keys
            .Where(k => byId.TryGetValue(k, out var o) && o.Type == NodeType.Product)
            .OrderBy(k => dist[k]).ThenBy(k => k, StringComparer.Ordinal)
            .Take(5).Select(k => byId[k]).ToList();

        return new NodeContext(hood, ReadSource(hood.Node, read, sourceLines), owners);
    }

    /// <summary>The source block a code node points at, budgeted to <paramref name="maxLines"/>.</summary>
    public static SourceBlock? ReadSource(GraphNode node, ReadLines read, int maxLines)
    {
        if (node.FilePath is not { Length: > 0 } rel) return null;
        if (node.Metadata?.GetValueOrDefault("line") is not { } lineText
            || !int.TryParse(lineText, out var startLine)) return null;
        if (read(rel) is not { } lines) return null;

        var s0 = Math.Max(0, startLine - 1);
        var full = BlockEnd(lines, s0, 400);
        var e0 = Math.Min(full, s0 + maxLines - 1);
        var slice = new List<string>();
        for (var i = s0; i <= e0 && i < lines.Length; i++) slice.Add(lines[i]);
        return new SourceBlock(rel, s0 + 1, e0 + 1, Math.Max(0, full - e0), slice);
    }

    // ── Walk ──────────────────────────────────────────────────────────────────

    /// <summary>Every node within <paramref name="hops"/> edges of the start, with its hop distance.</summary>
    public static IReadOnlyList<(GraphNode Node, int Hops)>? Walk(KnowledgeGraph g, string id, int hops)
    {
        var byId = Index(g);
        if (!byId.ContainsKey(id)) return null;

        return [.. Bfs(Adjacency(g), id, hops)
            .Where(kv => byId.ContainsKey(kv.Key))
            .OrderBy(kv => kv.Value).ThenBy(kv => TypeRank(byId[kv.Key].Type))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (byId[kv.Key], kv.Value))];
    }

    // ── Grep ──────────────────────────────────────────────────────────────────

    public sealed record GrepHit(GraphNode Node, int Line, string Text);

    /// <summary>
    /// Search the <i>source</i> of nodes near a starting point — the thing plain grep cannot do, because it
    /// has no notion of "near". With no start node it searches every code node in the graph.
    /// </summary>
    public static IReadOnlyList<GrepHit> Grep(KnowledgeGraph g, string pattern, ReadLines read,
                                              string? fromId = null, int hops = 2, int limit = 40)
    {
        Regex regex;
        try { regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
        catch (ArgumentException) { return []; }

        var byId = Index(g);
        IEnumerable<GraphNode> scope = g.Nodes;
        if (fromId is { Length: > 0 } && byId.ContainsKey(fromId))
            scope = Bfs(Adjacency(g), fromId, hops).Keys
                        .Where(byId.ContainsKey).Select(k => byId[k]);

        var hits = new List<GrepHit>();
        foreach (var node in scope.Where(n => n.FilePath is { Length: > 0 }))
        {
            if (hits.Count >= limit) break;
            if (ReadSource(node, read, 400) is not { } block) continue;
            for (var i = 0; i < block.Lines.Count && hits.Count < limit; i++)
                if (regex.IsMatch(block.Lines[i]))
                    hits.Add(new GrepHit(node, block.StartLine + i, block.Lines[i].Trim()));
        }
        return hits;
    }

    // ── Shared internals ──────────────────────────────────────────────────────

    public static Dictionary<string, GraphNode> Index(KnowledgeGraph g)
    {
        var byId = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        foreach (var n in g.Nodes) byId[n.Id] = n;
        return byId;
    }

    /// <summary>Undirected adjacency, with every pair of a hyperedge's endpoints joined — reachability is
    /// about "related to", not about which way an edge happens to point.</summary>
    public static Dictionary<string, HashSet<string>> Adjacency(KnowledgeGraph g)
    {
        var adj = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Link(string a, string b)
        {
            if (!adj.TryGetValue(a, out var s)) adj[a] = s = new HashSet<string>(StringComparer.Ordinal);
            s.Add(b);
        }
        foreach (var e in g.Edges) { Link(e.Source, e.Target); Link(e.Target, e.Source); }
        foreach (var h in g.HyperEdges)
            for (var i = 0; i < h.Endpoints.Count; i++)
                for (var j = i + 1; j < h.Endpoints.Count; j++)
                {
                    Link(h.Endpoints[i].Node, h.Endpoints[j].Node);
                    Link(h.Endpoints[j].Node, h.Endpoints[i].Node);
                }
        return adj;
    }

    public static Dictionary<string, int> Bfs(Dictionary<string, HashSet<string>> adj, string start, int hops)
    {
        var dist = new Dictionary<string, int>(StringComparer.Ordinal) { [start] = 0 };
        var q = new Queue<string>();
        q.Enqueue(start);
        while (q.Count > 0)
        {
            var u = q.Dequeue();
            if (dist[u] >= hops || !adj.TryGetValue(u, out var ns)) continue;
            foreach (var v in ns) if (!dist.ContainsKey(v)) { dist[v] = dist[u] + 1; q.Enqueue(v); }
        }
        return dist;
    }

    /// <summary>
    /// The last line of the brace-delimited block starting at <paramref name="start"/>, so a member's source
    /// ends where the member does. Tracks strings, chars and comments, because a brace inside any of those
    /// is not a brace.
    /// </summary>
    public static int BlockEnd(string[] lines, int start, int maxLines)
    {
        int depth = 0;
        bool opened = false, inBlockComment = false, inString = false, inChar = false, verbatim = false;

        for (var i = start; i < lines.Length && i - start <= maxLines; i++)
        {
            var line = lines[i];
            var inLineComment = false;
            for (var j = 0; j < line.Length; j++)
            {
                var ch = line[j];
                var next = j + 1 < line.Length ? line[j + 1] : '\0';
                if (inLineComment) break;
                if (inBlockComment) { if (ch == '*' && next == '/') { inBlockComment = false; j++; } continue; }
                if (inString)
                {
                    if (verbatim) { if (ch == '"' && next == '"') j++; else if (ch == '"') inString = false; }
                    else if (ch == '\\') j++;
                    else if (ch == '"') inString = false;
                    continue;
                }
                if (inChar) { if (ch == '\\') j++; else if (ch == '\'') inChar = false; continue; }

                switch (ch)
                {
                    case '/' when next == '/': inLineComment = true; break;
                    case '/' when next == '*': inBlockComment = true; j++; break;
                    case '@' when next == '"': inString = verbatim = true; j++; break;
                    case '"': inString = true; verbatim = false; break;
                    case '\'': inChar = true; break;
                    case '{': depth++; opened = true; break;
                    case '}':
                        depth--;
                        if (opened && depth <= 0) return i;
                        break;
                }
            }
            // A one-line member (expression-bodied, or a field) ends on its own line.
            if (!opened && line.TrimEnd().EndsWith(';')) return i;
        }
        return Math.Min(lines.Length - 1, start + maxLines);
    }
}

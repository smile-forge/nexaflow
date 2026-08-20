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

    /// <summary>
    /// How far <see cref="BlockEnd"/> will scan for a block's closing brace, and therefore how much of a node
    /// a source read or a content grep can see.
    /// <para>
    /// This is a runaway guard, not a budget to economise on: it exists so an unterminated block in a
    /// malformed file cannot walk the whole file. Sized to this repo - 1,858 own C# files, of which 114 pass
    /// 400 lines, 19 pass 1,000 and 4 pass 2,000 - so 2,000 covers all but a handful of files whole, where
    /// the old 400 truncated the top 6%. Cost of the difference: a full-repo content grep over all 47k code
    /// nodes stays near a second either way, because the work is dominated by node count, not block length.
    /// </para>
    /// <para>
    /// Truncation here is worth being careful about because it is <i>invisible</i> in a grep: a type node
    /// whose class runs past the cap reports no match, which reads exactly like "not present". That is the
    /// same failure the <c>--limit</c>/<c>--scan-cap</c> split was introduced to fix one layer up, and it is
    /// why <see cref="Grep"/> now says when it only saw part of a node.
    /// </para>
    /// </summary>
    public const int BlockScanLines = 2000;

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

    /// <summary>A related node and how sure the edge to it is - inferred edges (a call, a reference) carry
    /// less than extracted ones, and a reader deciding whether to trust a relationship needs to see that.</summary>
    public sealed record Related(GraphNode Node, double Confidence);

    public sealed record RelationGroup(string Relationship, IReadOnlyList<Related> Items)
    {
        /// <summary>Just the nodes, for callers that don't care how sure the edges are.</summary>
        public IReadOnlyList<GraphNode> Nodes => [.. Items.Select(i => i.Node)];
    }

    public static Neighbourhood? Node(KnowledgeGraph g, string id)
    {
        var byId = Index(g);
        if (!byId.TryGetValue(id, out var node)) return null;

        IReadOnlyList<RelationGroup> Group(IEnumerable<GraphEdge> edges, Func<GraphEdge, string> other) =>
            [.. edges.GroupBy(e => e.Relationship)
                     .OrderBy(x => x.Key, StringComparer.Ordinal)
                     .Select(x => new RelationGroup(x.Key,
                         [.. x.Select(e => (Edge: e, Node: byId.GetValueOrDefault(other(e))))
                              .Where(p => p.Node is not null)
                              .Select(p => new Related(p.Node!, p.Edge.Confidence))]))];

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
        IReadOnlyList<GraphNode> OwningFeatures,
        IReadOnlyList<string> OwnedFiles);

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

        return new NodeContext(hood, ReadSource(hood.Node, read, sourceLines), owners,
                               [.. OwnedFiles(g, id).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>The source block a code node points at, budgeted to <paramref name="maxLines"/>.
    /// <para>
    /// Where the block ends comes from the parser when the graph recorded it (<c>endLine</c> — tree-sitter's
    /// own end position, captured at build time) and from <see cref="BlockEnd"/> only when it did not: a file
    /// with no grammar, or a graph built before that metadata existed. Counting braces is the fallback, not
    /// the mechanism.
    /// </para></summary>
    public static SourceBlock? ReadSource(GraphNode node, ReadLines read, int maxLines)
    {
        if (node.FilePath is not { Length: > 0 } rel) return null;
        if (node.Metadata?.GetValueOrDefault("line") is not { } lineText
            || !int.TryParse(lineText, out var startLine)) return null;
        if (read(rel) is not { } lines) return null;

        var s0 = Math.Max(0, startLine - 1);
        var full = ParsedEnd(node, lines, s0) ?? BlockEnd(lines, s0, BlockScanLines);
        var e0 = Math.Min(full, s0 + maxLines - 1);
        var slice = new List<string>();
        for (var i = s0; i <= e0 && i < lines.Length; i++) slice.Add(lines[i]);
        return new SourceBlock(rel, s0 + 1, e0 + 1, Math.Max(0, full - e0), slice);
    }

    /// <summary>
    /// The 0-based last line of a node's block as the PARSER recorded it, or null when the graph has no
    /// <c>endLine</c> for it. Discarded when it disagrees with the file in hand — the graph can be older than
    /// the working tree, and a stale end would silently truncate or overrun; the brace scan re-derives it.
    /// </summary>
    private static int? ParsedEnd(GraphNode node, string[] lines, int start)
    {
        if (node.Metadata?.GetValueOrDefault("endLine") is not { } text
            || !int.TryParse(text, out var endLine)) return null;

        var e0 = endLine - 1;
        return e0 >= start && e0 < lines.Length ? e0 : null;
    }

    // ── Walk ──────────────────────────────────────────────────────────────────

    /// <summary>Every node within <paramref name="hops"/> edges of the start, with its hop distance.
    /// <paramref name="types"/> keeps only those node types (null = all).</summary>
    public static IReadOnlyList<(GraphNode Node, int Hops)>? Walk(KnowledgeGraph g, string id, int hops,
                                                                 IReadOnlySet<string>? types = null)
    {
        var byId = Index(g);
        if (!byId.ContainsKey(id)) return null;

        return [.. Bfs(Adjacency(g), id, hops)
            .Where(kv => byId.ContainsKey(kv.Key) && (types is null || types.Contains(byId[kv.Key].Type)))
            .OrderBy(kv => kv.Value).ThenBy(kv => byId[kv.Key].Type, StringComparer.Ordinal)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (byId[kv.Key], kv.Value))];
    }

    // ── Grep ──────────────────────────────────────────────────────────────────

    public sealed record GrepHit(GraphNode Node, int Line, string Text);

    /// <summary>
    /// The nodes a scoped query may look at: everything when there is no starting node, otherwise either a hop
    /// radius around it or the files its feature owns.
    /// <para>
    /// Shared so the CLI and the assistant cannot disagree about what a scope <i>is</i>. Presentation on top of
    /// it - paging, scan caps, index-vs-content matching - stays with each caller, because those are choices
    /// about output, not about meaning.
    /// </para>
    /// </summary>
    /// <param name="ownedButEmpty">True when <see cref="GrepScope.Owned"/> was asked for but the node owns no
    /// files (it has no snaplinks to code). The scope is then everything, and the caller should say so - a
    /// silent whole-graph search is how "no matches here" turns into a wrong conclusion.</param>
    public static IReadOnlyList<GraphNode> Scope(KnowledgeGraph g, string? fromId, int hops, GrepScope scope,
                                                 out bool ownedButEmpty)
    {
        ownedButEmpty = false;
        var byId = Index(g);
        if (fromId is not { Length: > 0 } || !byId.ContainsKey(fromId)) return g.Nodes;

        if (scope == GrepScope.Owned)
        {
            var owned = OwnedFiles(g, fromId);
            if (owned.Count == 0) { ownedButEmpty = true; return g.Nodes; }
            return [.. g.Nodes.Where(n => n.FilePath is { Length: > 0 } p && owned.Contains(p))];
        }

        return [.. Bfs(Adjacency(g), fromId, hops).Keys.Where(byId.ContainsKey).Select(k => byId[k])];
    }

    /// <summary>What "near <c>from</c>" means to <see cref="Grep"/>.</summary>
    public enum GrepScope
    {
        /// <summary>Every node within a hop radius of the start.</summary>
        Hops,
        /// <summary>Every code node in a file the start's feature owns - see <see cref="OwnedFiles"/>.</summary>
        Owned,
    }

    /// <summary>
    /// The files a feature owns: walk the product subtree under <paramref name="fromId"/> and take the file of
    /// every non-product node its snaplinks reach.
    /// <para>
    /// This exists because hop radius is the wrong distance metric for "this feature's own code". A snaplink
    /// names ONE member, so the sibling members of that member's own type sit two or three hops away, behind
    /// the type node - while two hops in the other direction has already left the feature entirely. There is
    /// no radius that means "this feature and nothing else", and the useful one changes per feature. Ownership
    /// does not: the file a snaplink lands in belongs to the feature, all of it.
    /// </para>
    /// <para>
    /// Starting from a code node instead, the nearest owning features answer first (the same ones
    /// <see cref="Context"/> reports), plus the start's own file - so "grep this feature" works from wherever
    /// you happen to be standing. Any edge out of a product node counts, because the snaplink vocabulary is
    /// open: whitelisting relationships here would silently drop whatever kind gets added next.
    /// </para>
    /// </summary>
    public static IReadOnlySet<string> OwnedFiles(KnowledgeGraph g, string fromId)
    {
        var byId = Index(g);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!byId.TryGetValue(fromId, out var start)) return files;

        var seeds = new HashSet<string>(StringComparer.Ordinal);
        if (start.Type == NodeType.Product)
        {
            var children = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var e in g.Edges.Where(e => e.Relationship == EdgeRelationship.Contains))
            {
                if (!children.TryGetValue(e.Source, out var kids)) children[e.Source] = kids = [];
                kids.Add(e.Target);
            }

            var queue = new Queue<string>();
            queue.Enqueue(fromId);
            seeds.Add(fromId);
            while (queue.Count > 0)
                foreach (var child in children.GetValueOrDefault(queue.Dequeue()) ?? [])
                    if (byId.GetValueOrDefault(child)?.Type == NodeType.Product && seeds.Add(child))
                        queue.Enqueue(child);
        }
        else
        {
            if (start.FilePath is { Length: > 0 } own) files.Add(own);
            var dist = Bfs(Adjacency(g), fromId, 3);
            foreach (var k in dist.Keys
                         .Where(k => byId.GetValueOrDefault(k)?.Type == NodeType.Product)
                         .OrderBy(k => dist[k]).ThenBy(k => k, StringComparer.Ordinal).Take(3))
                seeds.Add(k);
        }

        foreach (var e in g.Edges.Where(e => seeds.Contains(e.Source)))
            if (byId.GetValueOrDefault(e.Target) is { FilePath: { Length: > 0 } path } target
                && target.Type != NodeType.Product)
                files.Add(path);

        return files;
    }

    /// <summary>
    /// Search the <i>source</i> of nodes near a starting point — the thing plain grep cannot do, because it
    /// has no notion of "near". With no start node it searches every code node in the graph.
    /// </summary>
    public static IReadOnlyList<GrepHit> Grep(KnowledgeGraph g, string pattern, ReadLines read,
                                              string? fromId = null, int hops = 2, int limit = 40,
                                              GrepScope scope = GrepScope.Hops)
    {
        Regex regex;
        try { regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
        catch (ArgumentException) { return []; }

        var searched = Scope(g, fromId, hops, scope, out _);

        var hits = new List<GrepHit>();
        foreach (var node in searched.Where(n => n.FilePath is { Length: > 0 }))
        {
            if (hits.Count >= limit) break;
            if (ReadSource(node, read, BlockScanLines) is not { } block) continue;
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
    /// <para>
    /// This is the ONLY copy. The CLI carried a second one that had quietly diverged in three ways - it
    /// clamped the no-closing-brace fallback to 40 lines whatever was asked for, it detected <c>@"</c> by
    /// looking backwards (so a line *starting* with one was missed), and it ended a braceless member at the
    /// first <c>;</c> rather than a line-final one. The last of those was the better rule and is kept below;
    /// the other two were bugs. Two implementations meant `nfi graph` and the in-app assistant could answer
    /// the same question differently, which is exactly what this class exists to prevent.
    /// </para>
    /// <para>
    /// Raw string literals (<c>"""</c>, and any longer fence) are treated as opaque. Read as three ordinary
    /// quotes they toggle an in-string flag on-off-on, which leaves the scanner inside the literal only while
    /// its content happens to hold an EVEN number of quotes; one unpaired quote flips the parity and every
    /// brace after it is counted as code. This repo's test fixtures are full of raw strings containing C#, so
    /// that is not a theoretical case - see <c>BlockEndTests</c>.
    /// </para>
    /// </summary>
    public static int BlockEnd(string[] lines, int start, int maxLines)
    {
        int depth = 0, rawFence = 0;
        bool opened = false, inBlockComment = false, inString = false, inChar = false, verbatim = false;

        // How many quotes start at this position - the fence length opening or closing a raw literal.
        static int QuoteRun(string text, int at)
        {
            var n = 0;
            while (at + n < text.Length && text[at + n] == '"') n++;
            return n;
        }

        // Whether a brace group closing here also ends the declaration. It does when nothing of substance
        // follows it: `void M() { }`, `{ get; set; }`, or either with a trailing comment. It does NOT when the
        // declaration carries on - `{ get; } = new Dictionary<..>` opens an initializer that can run for a
        // hundred lines, and the member ends at ITS semicolon, not at the accessor list's brace.
        static bool DeclarationEndsAfter(string text, int at)
        {
            var rest = text[Math.Min(at + 1, text.Length)..].TrimStart();
            return rest.Length == 0 || rest[0] == ';' || rest.StartsWith("//") || rest.StartsWith("/*");
        }

        for (var i = start; i < lines.Length && i - start <= maxLines; i++)
        {
            var line = lines[i];
            var inLineComment = false;
            for (var j = 0; j < line.Length; j++)
            {
                var ch = line[j];
                var next = j + 1 < line.Length ? line[j + 1] : '\0';

                // Inside a raw literal nothing counts - not a brace, not a comment marker, not a lone quote.
                // Only a run at least as long as the opening fence ends it. Checked first, and spanning lines,
                // because a raw literal is opaque to everything else the scanner knows how to see.
                if (rawFence > 0)
                {
                    if (ch != '"') continue;
                    var run = QuoteRun(line, j);
                    if (run >= rawFence) rawFence = 0;
                    j += run - 1;
                    continue;
                }

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
                    // Three or more quotes open a raw literal whose fence is that length, so a shorter run
                    // inside it is content rather than the end. Two quotes are just an empty string.
                    case '"' when QuoteRun(line, j) >= 3:
                        rawFence = QuoteRun(line, j);
                        j += rawFence - 1;
                        break;
                    case '"': inString = true; verbatim = false; break;
                    case '\'': inChar = true; break;
                    case '{': depth++; opened = true; break;
                    case '}':
                        depth--;
                        if (opened && depth <= 0)
                        {
                            if (DeclarationEndsAfter(line, j)) return i;
                            // The brace group closed but the declaration carries on into an initializer.
                            // Forget that we ever saw a brace, so the terminating ';' below ends the member.
                            opened = false;
                        }
                        break;
                    // A braceless member - a field, or an expression-bodied one - ends at its semicolon.
                    // Testing here rather than on the trimmed line end is what makes a trailing comment
                    // (`private const int X = 1; // why`) terminate correctly instead of running on to the
                    // next block's closing brace.
                    case ';' when !opened && depth == 0: return i;
                }
            }
        }
        return Math.Min(lines.Length - 1, start + maxLines);
    }
}

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

    // ── Orphans ───────────────────────────────────────────────────────────────

    /// <summary>A declaration nothing appears to reach, and the reason that might be fine anyway.</summary>
    /// <param name="Excuse">
    /// Null when the node looks genuinely unreached. Otherwise why the graph cannot see the caller — a test the
    /// runner invokes by reflection, an interface member called through the interface, an entry point. These
    /// are reported separately rather than hidden, because "unreached, but here is why" is a different claim
    /// from "unreached".
    /// </param>
    public sealed record Orphan(GraphNode Node, string? Excuse);

    /// <summary>
    /// Declarations with no incoming reference of any kind — nothing calls, constructs, extends, implements,
    /// tests, documents, binds to or snaplinks them.
    ///
    /// <para>
    /// <c>contains</c> is ignored on purpose: every member is contained by its type and every type by its file,
    /// so counting structure would mean nothing is ever an orphan. What is counted is a *use*.
    /// </para>
    /// <para>
    /// This is a lead, not a verdict, and the honest reasons it can be wrong are worth stating: edges are
    /// name-resolved, so a call the resolver could not place leaves its target looking unused; and anything
    /// reached purely at runtime — reflection by string, a DI container, a serializer reading properties — is
    /// invisible here by construction. Treat a hit as "worth looking at", which is exactly what nothing at all
    /// could tell you before.
    /// </para>
    /// </summary>
    /// <param name="type">Restrict to a node type; defaults to <c>type</c>, which is far and away the least noisy.</param>
    /// <param name="under">
    /// Path prefix to restrict to. Defaults to <c>src/</c> for a reason worth keeping: a submodule under
    /// <c>external/</c> is a whole third-party library, and the parts of it this repo does not call are not
    /// findings — left in, they were every single hit. Pass an empty string to search everything.
    /// </param>
    /// <param name="includeExcused">Also return the nodes that have a reason (tests, interface members, entry points).</param>
    public static IReadOnlyList<Orphan> Orphans(KnowledgeGraph g, string? type = NodeType.Type,
                                                string? under = "src/",
                                                bool includeExcused = false, int limit = 200)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in g.Edges)
            if (e.Relationship != EdgeRelationship.Contains)
                used.Add(e.Target);

        // A hyperedge is a use too: an argument to a call, a parameter or return in a signature, an attribute.
        foreach (var h in g.HyperEdges)
            foreach (var p in h.Endpoints)
                if (p.Role != EndpointRole.Target && p.Role != EndpointRole.Member)
                    used.Add(p.Node);

        // A type whose member is reached is reached. Without this every static utility class is an orphan:
        // `RepoFiles.EnumerateSource(...)` is an edge to the *method*, and nothing ever names the class.
        foreach (var id in used.ToList())
            if (OwningTypeId(id) is { } owner)
                used.Add(owner);

        var index = Index(g);
        var attributes = AttributesByNode(g);
        var inherited = InheritedMemberNames(g, index);

        // A resource key defined in more than one dictionary — a light and a dark theme both declaring
        // AccentBrush. A {StaticResource} reference resolves to one of them, so the others look unreferenced
        // when the truth is that merge order picks the winner at runtime.
        var duplicateKeys = g.Nodes
            .Where(n => n.Type == NodeType.Type
                        && n.Metadata?.GetValueOrDefault("ast") is { } a && a.StartsWith("K:", StringComparison.Ordinal))
            .GroupBy(n => n.Label, StringComparer.Ordinal)
            .Where(gr => gr.Count() > 1 && gr.Any(n => used.Contains(n.Id)))
            .Select(gr => gr.Key)
            .ToHashSet(StringComparer.Ordinal);

        // Two declarations sharing a simple name — Core and Visuals.Common both declaring
        // InverseBoolToVisibilityConverter. Edges are name-resolved, so a reference that could mean either is
        // dropped rather than guessed: neither declaration collects the edge, and neither is evidence of death.
        var ambiguous = g.Nodes
            .Where(n => n.FilePath is not null && n.Type is NodeType.Type or NodeType.Member)
            .GroupBy(n => n.Type + " " + n.Label, StringComparer.Ordinal)
            .Where(gr => gr.Count() > 1)
            .Select(gr => gr.Key)
            .ToHashSet(StringComparer.Ordinal);

        // A type that implements a contract this repo declares. Nothing names an IPageRegistration, an
        // IThemeContribution or an IElevatedOperation: the shell scans assemblies for them at startup, so the
        // only edge that could exist is the one the graph cannot see. Narrow on purpose — the base has to be a
        // declaration in this repo, so implementing a framework interface excuses nothing.
        var contractual = g.Edges
            .Where(e => e.Relationship is EdgeRelationship.Implements or EdgeRelationship.Extends
                        && index.TryGetValue(e.Target, out var t) && t.FilePath is not null)
            .Select(e => e.Source)
            .ToHashSet(StringComparer.Ordinal);

        var found = new List<Orphan>();
        foreach (var n in g.Nodes)
        {
            if (n.FilePath is null) continue;                       // product/external nodes have no declaration
            if (under is { Length: > 0 } prefix
                && !n.FilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (type is not null && n.Type != type) continue;
            if (n.Type is not (NodeType.Type or NodeType.Member)) continue;
            if (used.Contains(n.Id)) continue;

            var excuse = Excuse(n, attributes, inherited, duplicateKeys, ambiguous, contractual);
            if (excuse is not null && !includeExcused) continue;
            found.Add(new Orphan(n, excuse));
            if (found.Count >= limit) break;
        }
        return found;
    }

    /// <summary>Why an apparently-unreached declaration may be reached anyway, or null if nothing excuses it.</summary>
    private static string? Excuse(GraphNode n,
                                  IReadOnlyDictionary<string, HashSet<string>> attributes,
                                  IReadOnlyDictionary<string, HashSet<string>> inherited,
                                  IReadOnlySet<string> duplicateKeys,
                                  IReadOnlySet<string> ambiguous,
                                  IReadOnlySet<string> contractual)
    {
        // A source file in a language the relation extractor does not read — the syntax-highlighting corpus is
        // .js/.py/.rb/.ts. No edge to it can exist, so its absence means nothing at all.
        if (n.FilePath is { } path && !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                                   && !path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            return "a language whose references the extractor does not read yet";

        if (duplicateKeys.Contains(n.Label))
            return "another dictionary defines this key and is referenced; merge order decides at runtime";

        if (ambiguous.Contains(n.Type + " " + n.Label))
            return "another declaration shares this name; a name-resolved reference cannot be attributed to either";

        if (contractual.Contains(n.Id))
            return "implements a contract this repo declares; the shell finds those by scanning assemblies";

        // A XAML anchor is reached in ways nothing here records yet: a UI journey finds an AutomationId by
        // string, and ElementName / TargetName reference an x:Name from inside the XAML itself. Until those
        // are edges, an unreferenced anchor is not evidence of anything.
        if (n.FilePath?.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) == true)
            return "a XAML anchor - a UI test's string id and an ElementName binding are not edges yet";

        if (attributes.TryGetValue(n.Id, out var attrs))
        {
            foreach (var a in attrs)
                if (a.EndsWith("TestMethod", StringComparison.Ordinal)
                    || a.EndsWith("TestClass", StringComparison.Ordinal)
                    || a.EndsWith("TestInitialize", StringComparison.Ordinal)
                    || a.EndsWith("TestCleanup", StringComparison.Ordinal)
                    || a.EndsWith("AssemblyInitialize", StringComparison.Ordinal)
                    || a.EndsWith("ClassInitialize", StringComparison.Ordinal))
                    return "a test, run by reflection";

            foreach (var a in attrs)
                if (a.EndsWith("RelayCommand", StringComparison.Ordinal)
                    || a.EndsWith("ObservableProperty", StringComparison.Ordinal))
                    return "generates a public member the view may bind";
        }

        if (n.Type == NodeType.Member && n.Label == "Main") return "an entry point";

        // Declared on a base or interface: the call goes through that, not through this declaration.
        var owner = OwningTypeId(n.Id);
        if (owner is not null && inherited.TryGetValue(owner, out var names) && names.Contains(n.Label))
            return "declared on a base type or interface";

        return null;
    }

    /// <summary>Attribute names per annotated node, from the <c>annotated</c> hyperedges.</summary>
    private static Dictionary<string, HashSet<string>> AttributesByNode(KnowledgeGraph g)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var h in g.HyperEdges)
        {
            if (h.Relationship != HyperRelationship.Annotated) continue;
            var target = h.Endpoints.FirstOrDefault(p => p.Role == EndpointRole.Target)?.Node;
            var attr = h.Endpoints.FirstOrDefault(p => p.Role == EndpointRole.Attr)?.Node;
            if (target is null || attr is null) continue;
            if (!map.TryGetValue(target, out var set)) map[target] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(attr);
        }
        return map;
    }

    /// <summary>For each type, the member names its bases and interfaces declare — the ones a call reaches indirectly.</summary>
    private static Dictionary<string, HashSet<string>> InheritedMemberNames(
        KnowledgeGraph g, IReadOnlyDictionary<string, GraphNode> index)
    {
        var membersOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var e in g.Edges)
        {
            if (e.Relationship != EdgeRelationship.Contains) continue;
            if (!index.TryGetValue(e.Target, out var target) || target.Type != NodeType.Member) continue;
            if (!membersOf.TryGetValue(e.Source, out var list)) membersOf[e.Source] = list = [];
            list.Add(target.Label);
        }

        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var e in g.Edges)
        {
            if (e.Relationship is not (EdgeRelationship.Extends or EdgeRelationship.Implements)) continue;
            if (!membersOf.TryGetValue(e.Target, out var names)) continue;
            if (!map.TryGetValue(e.Source, out var set)) map[e.Source] = set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in names) set.Add(name);
        }
        return map;
    }

    /// <summary>The type node id owning a member id — <c>code:F.cs#T:A/M:B</c> → <c>code:F.cs#T:A</c>.</summary>
    private static string? OwningTypeId(string memberId)
    {
        var cut = memberId.LastIndexOf('/');
        return cut > 0 ? memberId[..cut] : null;
    }

    // ── Paths ─────────────────────────────────────────────────────────────────

    /// <summary>One hop along a path: the relationship taken and the node arrived at.</summary>
    public sealed record PathStep(string Relationship, GraphNode Node);

    /// <summary>A route between two nodes — the start, then every hop taken to reach the end.</summary>
    public sealed record GraphPath(GraphNode From, IReadOnlyList<PathStep> Steps)
    {
        public int Hops => Steps.Count;
    }

    /// <summary>
    /// Routes from one node to another — the question <c>walk</c> could never answer, because a walk returns
    /// a ball around a single node rather than the ways two nodes connect.
    ///
    /// <para>
    /// <b>Directed by default</b>, which is the useful reading: "what does this entry point reach" follows
    /// edges the way the code does. <paramref name="undirected"/> answers the looser "how are these two
    /// related at all".
    /// </para>
    /// <para>
    /// <b>Containment is never dropped</b> — it is real structure, and in the product tree it is the whole
    /// backbone: a feature contains its UI, which contains a panel. What is meaningless is not the edge but
    /// one <i>traversal shape</i>: walking <i>up</i> a container and straight back <i>down</i> into a sibling.
    /// That move makes every pair of declarations in a file two hops apart via the file, which would drown
    /// every real answer. So undirected search forbids exactly that — a descent immediately after an ascent —
    /// and keeps containment available everywhere else. (Snaplink edges are deliberately kept too: a
    /// <c>tests</c> or <c>documents</c> link is how a path crosses from the product tree into code, which is
    /// usually the most useful hop in the whole route.)
    /// </para>
    /// <para>
    /// Only <b>shortest</b> paths are returned. Enumerating every route between two nodes in a graph this
    /// dense is exponential and unreadable; the shortest ones are the ones that explain the relationship.
    /// </para>
    /// </summary>
    public static IReadOnlyList<GraphPath> Paths(KnowledgeGraph g, string fromId, string toId,
                                                 int maxHops = 6, int limit = 10, bool undirected = false)
    {
        var index = Index(g);
        if (!index.TryGetValue(fromId, out var from) || !index.ContainsKey(toId)) return [];
        if (fromId == toId) return [new GraphPath(from, [])];

        var next = new Dictionary<string, List<(string To, string Rel, bool Ascending)>>(StringComparer.Ordinal);
        void Link(string a, string b, string rel, bool ascending)
        {
            if (!next.TryGetValue(a, out var list)) next[a] = list = [];
            list.Add((b, rel, ascending));
        }
        foreach (var e in g.Edges)
        {
            Link(e.Source, e.Target, e.Relationship, ascending: false);
            if (undirected) Link(e.Target, e.Source, e.Relationship, ascending: true);
        }

        // Up-then-down through a container is the one move that means nothing, so it is the one move barred.
        // The state carries whether the last hop climbed a containment edge; a descent is refused from there.
        bool Barred(bool climbed, string rel, bool ascending) =>
            climbed && !ascending && rel == EdgeRelationship.Contains;

        // Distance from the start, capped — then walk forward taking only hops that close the gap, which
        // yields exactly the shortest routes without enumerating the whole graph.
        var dist = new Dictionary<(string Node, bool Climbed), int> { [(fromId, false)] = 0 };
        var queue = new Queue<(string Node, bool Climbed)>();
        queue.Enqueue((fromId, false));
        while (queue.Count > 0)
        {
            var state = queue.Dequeue();
            var d = dist[state];
            if (d >= maxHops || state.Node == toId) continue;
            if (!next.TryGetValue(state.Node, out var outgoing)) continue;
            foreach (var (to, rel, ascending) in outgoing)
            {
                if (Barred(state.Climbed, rel, ascending)) continue;
                var step = (to, ascending && rel == EdgeRelationship.Contains);
                if (!dist.ContainsKey(step)) { dist[step] = d + 1; queue.Enqueue(step); }
            }
        }

        var target = dist.Where(kv => kv.Key.Node == toId).Select(kv => kv.Value).DefaultIfEmpty(-1).Min();
        if (target < 0) return [];

        var found = new List<GraphPath>();
        var steps = new List<PathStep>();
        void Walk(string current, bool climbed, int depth)
        {
            if (found.Count >= limit) return;
            if (current == toId) { found.Add(new GraphPath(from, [.. steps])); return; }
            if (!next.TryGetValue(current, out var outgoing)) return;

            foreach (var (to, rel, ascending) in outgoing.OrderBy(x => x.To, StringComparer.Ordinal))
            {
                if (Barred(climbed, rel, ascending)) continue;
                var nextClimbed = ascending && rel == EdgeRelationship.Contains;
                if (!dist.TryGetValue((to, nextClimbed), out var d) || d != depth + 1 || d > target) continue;
                if (!index.TryGetValue(to, out var node)) continue;
                steps.Add(new PathStep(ascending ? rel + " (up)" : rel, node));
                Walk(to, nextClimbed, d);
                steps.RemoveAt(steps.Count - 1);
                if (found.Count >= limit) return;
            }
        }
        Walk(fromId, false, 0);
        return found;
    }

    // ── Fan-in / fan-out ──────────────────────────────────────────────────────

    /// <summary>A node with how many edges point at it and how many it sends out.</summary>
    public sealed record Ranking(GraphNode Node, int FanIn, int FanOut);

    /// <summary>
    /// Nodes ordered by how much of the graph points at them (fan-in) or how much they reach (fan-out) — the
    /// difference between "which components are central" being eyeballed from community sizes and being read
    /// off a list.
    ///
    /// <para>
    /// <c>contains</c> never counts. It is pure structure: counting it would rank types by how many members
    /// they declare and files by how many types they hold, which measures size, not centrality — and the
    /// biggest class would win every time regardless of whether anything uses it.
    /// </para>
    /// </summary>
    /// <param name="type">Restrict to a node type; null for all.</param>
    /// <param name="under">Path prefix to restrict to — defaults to everything, unlike orphans, because a
    /// heavily-used third-party type is a legitimate answer to "what is central here".</param>
    public static IReadOnlyList<Ranking> Rank(KnowledgeGraph g, bool byFanIn = true, string? type = null,
                                              string? under = null, int limit = 25)
    {
        var fanIn = new Dictionary<string, int>(StringComparer.Ordinal);
        var fanOut = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var e in g.Edges)
        {
            if (e.Relationship == EdgeRelationship.Contains) continue;
            fanIn[e.Target] = fanIn.GetValueOrDefault(e.Target) + 1;
            fanOut[e.Source] = fanOut.GetValueOrDefault(e.Source) + 1;
        }

        return [.. g.Nodes
            .Where(n => type is null || n.Type == type)
            .Where(n => under is not { Length: > 0 } prefix
                        || (n.FilePath?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ?? false))
            .Select(n => new Ranking(n, fanIn.GetValueOrDefault(n.Id), fanOut.GetValueOrDefault(n.Id)))
            .Where(r => (byFanIn ? r.FanIn : r.FanOut) > 0)
            .OrderByDescending(r => byFanIn ? r.FanIn : r.FanOut)
            .ThenBy(r => r.Node.Id, StringComparer.Ordinal)
            .Take(limit)];
    }
}

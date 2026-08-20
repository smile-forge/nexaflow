using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Services;

namespace Nexaflow.Features.ProductManager.ClientTools;

/// <summary>
/// The knowledge-graph half of the assistant's surface: the product tree crossed with the whole-repo AST and
/// the snaplinks between them.
/// <para>
/// This is the code-discovery path CLAUDE.md calls the primary way to explore the repo, and it answers things
/// grep cannot — who calls or instantiates a type, which project depends on which, the code-behind a view
/// belongs to, and the product feature that owns a given piece of code. Every tool is a read over the built
/// <c>graph.json</c>; only the rebuild writes anything, and it is the one that asks first.
/// </para>
/// </summary>
public static class GraphTools
{
    public static IReadOnlyList<IClientTool> ForRoot(string productRoot)
    {
        return
        [
            new DelegateClientTool("graph_search",
                "Find graph nodes by id or label - a product feature, a type, a member, a file. Best match "
                + "first: an exact label, then a prefix, then a substring. The starting point for every other "
                + "graph call, since they all take a node id.",
                [new ClientToolParameter("term", "Substring to search for."),
                 new ClientToolParameter("type", "Restrict to product | type | member | file | external.", Required: false),
                 new ClientToolParameter("limit", "Maximum rows (default 40).", Required: false, Type: "number")],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(Search(productRoot, a))),

            new DelegateClientTool("graph_context",
                "Everything about one node in a single call: what it is, its source, its immediate "
                + "relationships in both directions, and the product feature(s) that own it. The tool to reach "
                + "for before reading a file - it answers 'what is this and what is it part of' at once.",
                [new ClientToolParameter("node_id", "Node id, e.g. 'product:dicom' or 'code:src/A.cs#Foo'."),
                 new ClientToolParameter("lines", "How many source lines to include (default 60).", Required: false, Type: "number")],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(Context(productRoot, a))),

            new DelegateClientTool("graph_node",
                "One node and every edge touching it, in both directions, grouped by relationship - who it "
                + "calls and who calls it, what it contains and what contains it - plus its hyperedges.",
                [new ClientToolParameter("node_id", "The node id.")],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(Node(productRoot, a))),

            new DelegateClientTool("graph_walk",
                "Every node within N hops of a starting node, grouped by distance - the neighbourhood around "
                + "a change, and what it might affect.",
                [new ClientToolParameter("node_id", "Where to start."),
                 new ClientToolParameter("hops", "How far to walk (default 2).", Required: false, Type: "number")],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(Walk(productRoot, a))),

            new DelegateClientTool("graph_grep",
                "Search the source of nodes near a starting node. Unlike a plain text search this one "
                + "understands 'near' - scoping a regex to the neighbourhood of a type finds the callers that "
                + "matter without the thousand unrelated files that happen to share a word. To search a whole "
                + "FEATURE rather than a radius, pass scope='owned' with the feature's product node.",
                [new ClientToolParameter("pattern", "Regular expression (case-insensitive)."),
                 new ClientToolParameter("from", "Node id to search around; omit to search every code node.", Required: false),
                 new ClientToolParameter("scope",
                     "'hops' (default) searches within 'hops' edges of 'from' - use it for \"near THIS code\". "
                     + "'owned' searches every file the feature's snaplinks land in - use it for \"inside this "
                     + "feature\", where a radius either falls short of the sibling members or overshoots into "
                     + "the next feature.", Required: false),
                 new ClientToolParameter("hops", "Neighbourhood radius when 'from' is given and scope is 'hops' (default 2).", Required: false, Type: "number"),
                 new ClientToolParameter("limit", "Maximum matches (default 40).", Required: false, Type: "number")],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(Grep(productRoot, a))),

            new DelegateClientTool("graph_code",
                "The source block a code node points at - the member or type itself, not the whole file.",
                [new ClientToolParameter("node_id", "A code node id."),
                 new ClientToolParameter("lines", "Maximum lines (default 200).", Required: false, Type: "number")],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(Code(productRoot, a))),

            new DelegateClientTool("graph_stats",
                "The shape of the graph: node counts by type, the commonest relationships, and how many "
                + "edges and hyperedges there are. Worth a call before a big exploration to see what is there.",
                [], ToolSafety.SafeOperation,
                (_, _) => Task.FromResult(Stats(productRoot))),

            new DelegateClientTool("graph_build",
                "Rebuild graph.json from the product tree and the current source. Incremental - only files "
                + "that changed are re-parsed - but it still walks the repo, so it is the one graph tool that "
                + "costs real time and the only one that writes. Call it when the graph looks out of date.",
                [], ToolSafety.RequiresApproval,
                (_, _) => Task.FromResult(Build(productRoot))),
        ];
    }

    // ── Implementations ─────────────────────────────────────────────────────

    private static ToolResult Search(string root, JsonObject a)
    {
        if (!TryLoad(root, out var g, out var error)) return error;
        var term = Str(a, "term");
        if (string.IsNullOrWhiteSpace(term)) return ToolResult.Error("Provide a 'term' to search for.");

        var limit = Int(a, "limit", 40);
        var hits = GraphQuery.Search(g, term, Blank(Str(a, "type")));
        return ToolResult.Ok($"{hits.Count} match(es)", GraphReport.Search(hits, term, limit));
    }

    private static ToolResult Context(string root, JsonObject a)
    {
        if (!TryLoad(root, out var g, out var error)) return error;
        var id = Str(a, "node_id");
        if (string.IsNullOrWhiteSpace(id)) return ToolResult.Error("Provide a 'node_id'.");

        var ctx = GraphQuery.Context(g, id, Reader(root), Int(a, "lines", 60));
        if (ctx is null) return NoNode(id);
        return ToolResult.Ok($"context for {ctx.Neighbourhood.Node.Label}", GraphReport.Context(ctx));
    }

    private static ToolResult Node(string root, JsonObject a)
    {
        if (!TryLoad(root, out var g, out var error)) return error;
        var id = Str(a, "node_id");
        if (string.IsNullOrWhiteSpace(id)) return ToolResult.Error("Provide a 'node_id'.");

        var hood = GraphQuery.Node(g, id);
        if (hood is null) return NoNode(id);
        return ToolResult.Ok($"{hood.Node.Label} and its edges", GraphReport.Node(hood));
    }

    private static ToolResult Walk(string root, JsonObject a)
    {
        if (!TryLoad(root, out var g, out var error)) return error;
        var id = Str(a, "node_id");
        if (string.IsNullOrWhiteSpace(id)) return ToolResult.Error("Provide a 'node_id'.");

        var hops = Int(a, "hops", 2);
        var reached = GraphQuery.Walk(g, id, hops);
        if (reached is null) return NoNode(id);
        return ToolResult.Ok($"{reached.Count} node(s) within {hops} hop(s)", GraphReport.Walk(reached, id, hops));
    }

    private static ToolResult Grep(string root, JsonObject a)
    {
        if (!TryLoad(root, out var g, out var error)) return error;
        var pattern = Str(a, "pattern");
        if (string.IsNullOrWhiteSpace(pattern)) return ToolResult.Error("Provide a 'pattern' to search for.");

        var from = Blank(Str(a, "from"));
        if (from is not null && !GraphQuery.Index(g).ContainsKey(from)) return NoNode(from);

        var scopeText = Blank(Str(a, "scope")) ?? "hops";
        if (scopeText is not ("hops" or "owned"))
            return ToolResult.Error($"'scope' must be 'hops' or 'owned' (got '{scopeText}').");
        if (scopeText == "owned" && from is null)
            return ToolResult.Error("'scope' of 'owned' needs a 'from' node - ownership is relative to one.");
        var scope = scopeText == "owned" ? GraphQuery.GrepScope.Owned : GraphQuery.GrepScope.Hops;

        var hits = GraphQuery.Grep(g, pattern, Reader(root), from, Int(a, "hops", 2), Int(a, "limit", 40), scope);
        return ToolResult.Ok($"{hits.Count} match(es)", GraphReport.Grep(hits, pattern, from, scope));
    }

    private static ToolResult Code(string root, JsonObject a)
    {
        if (!TryLoad(root, out var g, out var error)) return error;
        var id = Str(a, "node_id");
        if (string.IsNullOrWhiteSpace(id)) return ToolResult.Error("Provide a 'node_id'.");
        if (!GraphQuery.Index(g).TryGetValue(id, out var node)) return NoNode(id);

        var block = GraphQuery.ReadSource(node, Reader(root), Int(a, "lines", 200));
        return ToolResult.Ok(block is null ? "no source" : $"{node.Label} source",
                             GraphReport.Source(node, block));
    }

    private static ToolResult Stats(string root)
    {
        if (!TryLoad(root, out var g, out var error)) return error;
        return ToolResult.Ok($"{g.Nodes.Count} node(s)", GraphReport.Stats(g));
    }

    private static ToolResult Build(string root)
    {
        try
        {
            var store = new ProductStore(root);
            var state = store.Load();

            // Incremental: unchanged files keep their cached extraction, so a rebuild after a small edit is
            // cheap. The cache is content-addressed, so reusing it cannot go stale.
            var built = GraphBuilder.BuildWithCache(state, root, new GraphBuildOptions(), store.LoadGraphCache());
            store.SaveGraph(built.Graph);
            store.SaveGraphCache(built.Cache);

            var g = built.Graph;
            return ToolResult.Ok($"rebuilt - {g.Nodes.Count} node(s)",
                $"Rebuilt the knowledge graph: {g.Nodes.Count:N0} node(s), {g.Edges.Count:N0} edge(s), "
              + $"{g.HyperEdges.Count:N0} hyperedge(s).");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Could not rebuild the graph: {ex.Message}");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Source is read from the product root. The CLI additionally prefers the caller's working tree
    /// (so a worktree shows the branch being edited); in-app there is only ever the one checkout open.</summary>
    private static GraphQuery.ReadLines Reader(string root) => rel =>
    {
        try
        {
            var full = System.IO.Path.Combine(root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));
            return System.IO.File.Exists(full)
                ? System.IO.File.ReadAllText(full).Replace("\r\n", "\n").Split('\n')
                : null;
        }
        catch { return null; }
    };

    private static bool TryLoad(string root, out KnowledgeGraph graph, out ToolResult error)
    {
        graph = null!;
        error = default!;
        var loaded = new ProductStore(root).LoadGraph();
        if (loaded is null)
        {
            error = ToolResult.Error(
                "No knowledge graph has been built yet. Call graph_build first (it walks the repo, so it asks "
              + "before running).");
            return false;
        }
        graph = loaded;
        return true;
    }

    private static ToolResult NoNode(string? id) =>
        ToolResult.Error($"No graph node '{id}'. Use graph_search to find one.");

    private static string? Str(JsonObject a, string key) =>
        a.TryGetPropertyValue(key, out var v) ? v?.ToString() : null;

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static int Int(JsonObject a, string key, int fallback) =>
        a.TryGetPropertyValue(key, out var v) && int.TryParse(v?.ToString(), out var n) && n > 0 ? n : fallback;
}

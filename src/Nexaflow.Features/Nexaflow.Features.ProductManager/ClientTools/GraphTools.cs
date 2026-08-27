using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Syntax;

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

            new DelegateClientTool("graph_orphans",
                "Declarations nothing appears to reach - no call, construction, inheritance, test or snaplink "
                + "points at them. A starting point for dead-code hunting, NOT proof: edges are name-resolved, "
                + "the graph records calls and constructions rather than every mention of a type, and anything "
                + "reached only at runtime (reflection, DI, a serializer) leaves no edge. Each hit that has a "
                + "known innocent explanation says so. Verify before deleting anything.",
                [new ClientToolParameter("type", "'type' (default, least noisy) or 'member'.", Required: false),
                 new ClientToolParameter("under", "Path prefix to search; defaults to 'src/' so a third-party "
                                                + "submodule's unused half is not reported. '' searches all.", Required: false),
                 new ClientToolParameter("all", "Also list hits that have a known explanation.", Required: false, Type: "boolean"),
                 new ClientToolParameter("limit", "Maximum rows (default 200).", Required: false, Type: "number")],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(Orphans(productRoot, a))),

            new DelegateClientTool("graph_paths",
                "Routes between two specific nodes - what 'walk' cannot answer, because a walk returns a ball "
                + "around one node rather than the ways two connect. Directed by default, which reads as 'what "
                + "does this reach'; undirected asks the looser 'how are these related at all'. Only shortest "
                + "routes are returned, and each hop names the relationship, so a path reads as an explanation.",
                [new ClientToolParameter("from", "Node id to start from (see graph_search)."),
                 new ClientToolParameter("to", "Node id to reach."),
                 new ClientToolParameter("hops", "Maximum path length (default 6).", Required: false, Type: "number"),
                 new ClientToolParameter("undirected", "Ignore edge direction.", Required: false, Type: "boolean"),
                 new ClientToolParameter("limit", "Maximum routes (default 10).", Required: false, Type: "number")],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(Paths(productRoot, a))),

            new DelegateClientTool("graph_rank",
                "The most-depended-on nodes: fan-in is how much of the repo points at something, fan-out how "
                + "much it reaches. Turns 'which components are central' from an eyeball judgement into a list. "
                + "Containment is never counted - it would rank a type by how many members it declares, which "
                + "measures size rather than importance.",
                [new ClientToolParameter("by", "'fanin' (default) or 'fanout'.", Required: false),
                 new ClientToolParameter("type", "Restrict to a node type: product, file, type, member, external.", Required: false),
                 new ClientToolParameter("under", "Path prefix to restrict to, e.g. 'src/'.", Required: false),
                 new ClientToolParameter("limit", "Maximum rows (default 25).", Required: false, Type: "number")],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(Rank(productRoot, a))),

            new DelegateClientTool("graph_edit",
                "Edit a declaration structurally, addressed by node id rather than by line number - replace "
                + "it, delete it, change its signature without touching its body (or the reverse), rename it, "
                + "insert one beside it, append a member to a type, or rewrite its doc comment. Prefer this "
                + "over rewriting a file: it re-resolves the declaration in the file as it is NOW and refuses "
                + "unless the parser agrees it is still the one the graph named, so a graph that has fallen "
                + "behind cannot overwrite whatever occupies those lines instead. The result is re-parsed and "
                + "the edit is refused if it would leave the file broken. Do not worry about line endings, "
                + "indentation or escaping - write the replacement flush-left with \\n and it lands correctly "
                + "indented with the file's own endings. Use dry_run first to see the hunk.",
                [new ClientToolParameter("node_id", "The code node to edit (see graph_search / graph_context)."),
                 new ClientToolParameter("op",
                     "replace | delete | signature | body | rename | insert_before | insert_after | append | doc. "
                   + "'append' targets a type and adds a member at the end of its body; 'signature' and 'body' "
                   + "each leave the other half byte-for-byte unchanged."),
                 new ClientToolParameter("text",
                     "The new code — or, for 'substitute', what to replace 'find' with. Not needed for 'delete'.",
                     Required: false),
                 new ClientToolParameter("to", "The new name, for 'rename'.", Required: false),
                 new ClientToolParameter("find",
                     "For 'substitute': the text to find, searched only INSIDE this declaration so it cannot "
                   + "run away across the file. Literal unless find_is_regex, and refused unless it matches "
                   + "exactly once - use this instead of rewriting a whole method to change one line.",
                     Required: false),
                 new ClientToolParameter("find_is_regex",
                     "Treat 'find' as a regular expression. Off by default, because a '(' or '.' in a code "
                   + "fragment matching something unintended is the hazard this avoids.",
                     Required: false, Type: "boolean"),
                 new ClientToolParameter("all_occurrences",
                     "Allow 'find' to match more than once. Off by default, so an ambiguous substitution is "
                   + "an error rather than a silent multi-edit.", Required: false, Type: "boolean"),
                 new ClientToolParameter("expect",
                     "Refuse unless the declaration currently contains this text - pin the edit to what you "
                   + "read.", Required: false),
                 new ClientToolParameter("with_trivia",
                     "For 'replace', also replace the doc comment above it. Off by default, so replacing a "
                   + "method keeps its documentation.", Required: false, Type: "boolean"),
                 new ClientToolParameter("dry_run", "Report what would change and write nothing.",
                     Required: false, Type: "boolean")],
                ToolSafety.RequiresApproval,
                (a, _) => Task.FromResult(Edit(productRoot, a))),

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

    private static ToolResult Orphans(string root, JsonObject a)
    {
        if (!TryLoad(root, out var g, out var error)) return error;

        var type = Blank(Str(a, "type")) ?? NodeType.Type;
        if (type is not (NodeType.Type or NodeType.Member))
            return ToolResult.Error($"'type' must be 'type' or 'member' (got '{type}').");

        var under = Str(a, "under") ?? "src/";
        var all = Str(a, "all") is { } flag && bool.TryParse(flag, out var b) && b;

        var orphans = GraphQuery.Orphans(g, type, under, all, Int(a, "limit", 200));
        return ToolResult.Ok($"{orphans.Count} unreached {type}(s)", GraphReport.Orphans(orphans, type, all));
    }

    private static ToolResult Paths(string root, JsonObject a)
    {
        if (!TryLoad(root, out var g, out var error)) return error;

        var from = Str(a, "from");
        var to = Str(a, "to");
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return ToolResult.Error("Provide both 'from' and 'to' node ids (see graph_search).");
        if (!g.Nodes.Any(n => n.Id == from)) return ToolResult.Error($"No node '{from}'. Try graph_search.");
        if (!g.Nodes.Any(n => n.Id == to)) return ToolResult.Error($"No node '{to}'. Try graph_search.");

        var hops = Int(a, "hops", 6);
        var undirected = Str(a, "undirected") is { } flag && bool.TryParse(flag, out var b) && b;
        var paths = GraphQuery.Paths(g, from!, to!, hops, Int(a, "limit", 10), undirected);
        return ToolResult.Ok($"{paths.Count} path(s)", GraphReport.Paths(paths, from!, to!, hops, undirected));
    }

    private static ToolResult Rank(string root, JsonObject a)
    {
        if (!TryLoad(root, out var g, out var error)) return error;

        var by = Blank(Str(a, "by")) ?? "fanin";
        if (by is not ("fanin" or "fanout"))
            return ToolResult.Error($"'by' must be 'fanin' or 'fanout' (got '{by}').");

        var under = Str(a, "under");
        var ranked = GraphQuery.Rank(g, by == "fanin", Blank(Str(a, "type")), under, Int(a, "limit", 25));
        return ToolResult.Ok($"top {ranked.Count} by fan-{(by == "fanin" ? "in" : "out")}",
                             GraphReport.Rank(ranked, by == "fanin", under));
    }

    private static ToolResult Stats(string root)
    {
        if (!TryLoad(root, out var g, out var error)) return error;
        return ToolResult.Ok($"{g.Nodes.Count} node(s)", GraphReport.Stats(g));
    }

    private static ToolResult Edit(string root, JsonObject a)
    {
        if (!TryLoad(root, out var g, out var error)) return error;

        var id = Str(a, "node_id");
        if (string.IsNullOrWhiteSpace(id)) return ToolResult.Error("Provide a 'node_id'.");

        var op = Blank(Str(a, "op"))?.Replace('-', '_').ToLowerInvariant() switch
        {
            "replace"       => StructuralEdit.Op.Replace,
            "delete"        => StructuralEdit.Op.Delete,
            "signature"     => StructuralEdit.Op.Signature,
            "body"          => StructuralEdit.Op.Body,
            "rename"        => StructuralEdit.Op.Rename,
            "insert_before" => StructuralEdit.Op.InsertBefore,
            "insert_after"  => StructuralEdit.Op.InsertAfter,
            "append"        => StructuralEdit.Op.Append,
            "doc"           => StructuralEdit.Op.Doc,
            "substitute" or "sub" => StructuralEdit.Op.Substitute,
            "import" or "using"   => StructuralEdit.Op.Import,
            _               => (StructuralEdit.Op?)null,
        };
        if (op is null)
            return ToolResult.Error(
                $"Unknown 'op' '{Str(a, "op")}'. Expected replace, delete, signature, body, rename, "
              + "insert_before, insert_after, append, doc or substitute.");

        var options = new StructuralEdit.Options(
            Bool(a, "with_trivia"), Blank(Str(a, "expect")),
            Blank(Str(a, "find")), Bool(a, "find_is_regex"), Bool(a, "all_occurrences"));
        var result  = GraphEdit.Plan(g, id!, op.Value, Str(a, "text"), RawReader(root), options, Blank(Str(a, "to")));

        if (!result.Ok) return ToolResult.Error(result.Message);

        var report = new System.Text.StringBuilder();
        foreach (var change in result.Changes)
        {
            report.AppendLine($"--- {change.RelativePath}:{change.Hunk.Line}");
            foreach (var line in change.Hunk.Removed) report.AppendLine($"- {line}");
            foreach (var line in change.Hunk.Added)   report.AppendLine($"+ {line}");
        }
        foreach (var note in result.Notes) report.AppendLine($"note: {note}");

        if (Bool(a, "dry_run"))
        {
            report.AppendLine("Dry run — nothing was written.");
            return ToolResult.Ok($"would {result.Message}", report.ToString());
        }

        foreach (var change in result.Changes)
        {
            var full = FullPath(root, change.RelativePath);
            if (SourceFile.Read(full) is not { } raw)
                return ToolResult.Error($"{change.RelativePath} could not be re-read before writing.");
            if (SourceFile.WriteIfUnchanged(full, change.OriginalText, change.NewText, raw.Encoding) is { } refused)
                return ToolResult.Error(refused);
        }

        report.AppendLine("Rebuild the graph (graph_build) so its record matches the file.");
        return ToolResult.Ok(result.Message, report.ToString());
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

    /// <summary>Raw file text — line endings and BOM intact, because an edit puts them back. The
    /// line-splitting <see cref="Reader"/> above normalises both away, so it cannot be used for editing.</summary>
    private static GraphEdit.ReadText RawReader(string root) => rel => SourceFile.Read(FullPath(root, rel))?.Text;

    private static string FullPath(string root, string rel) =>
        System.IO.Path.Combine(root, rel.Replace('/', System.IO.Path.DirectorySeparatorChar));

    private static bool Bool(JsonObject a, string key) =>
        a.TryGetPropertyValue(key, out var v) && bool.TryParse(v?.ToString(), out var b) && b;

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

using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

namespace Nexaflow.Features.ProductManager.ClientTools;

/// <summary>
/// The AI surface for a product folder. Tools reload <c>.product/</c> on each call (so the model always sees
/// current on-disk state) and write back to <c>tree.json</c>; the live page watches that file, so edits show
/// up in the UI immediately. Read + low-risk edits run without a prompt; structural changes (removing links,
/// adding/removing a node's concerns) are <see cref="ToolSafety.RequiresApproval"/>.
/// </summary>
public static class ProductTools
{
    /// <summary>
    /// Everything the assistant can do to a product folder: the tree commands below, plus the knowledge-graph
    /// commands from <see cref="GraphTools"/>. Both families are the same operations the
    /// <c>nfi</c> CLI exposes, over the same services and rendered by the same reporters.
    /// </summary>
    public static IReadOnlyList<IClientTool> ForRoot(string productRoot) =>
        [.. TreeTools(productRoot), .. GraphTools.ForRoot(productRoot)];

    private static IReadOnlyList<IClientTool> TreeTools(string productRoot)
    {
        ProductState Load() => new ProductStore(productRoot).Load();

        return
        [
            // ── Read ──────────────────────────────────────────────────────────
            new DelegateClientTool("product_survey",
                "Summarise the whole product: node count and the leaf status distribution (by rolled-up effective status).",
                [], ToolSafety.SafeOperation,
                (_, _) => Task.FromResult(ToolResult.Ok("Product survey", Survey(Load())))),

            new DelegateClientTool("product_zoom",
                "Inspect one node and its immediate surroundings — its path from the root (one ring up), its full "
                + "details (status, description, note, concerns, snaplinks), and its children (one ring down). Call "
                + "repeatedly with different node ids to explore the tree a ring at a time. Omit node_id for the product root.",
                [new ClientToolParameter("node_id", "The node id to inspect (omit / empty for the whole product).", Required: false)],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(ToolResult.Ok("Zoom", Zoom(Load(), Str(a, "node_id"))))),

            new DelegateClientTool("product_needs_attention",
                "List the faulted sources: each node that is itself faulted (a faulted leaf, or any node with a faulted concern), with its note.",
                [], ToolSafety.SafeOperation,
                (_, _) => Task.FromResult(ToolResult.Ok("Needs attention", NeedsAttention(Load())))),

            // ── Edit (no prompt) ──────────────────────────────────────────────
            new DelegateClientTool("product_set_node_status",
                "Mark a node 'done' or 'faulted'. Cascades to the subtree: descendant leaves and every concern that "
                + "is currently 'should' are advanced too (deliberate shouldnt/faulted are left alone). Use 'faulted' "
                + "with a note when something is broken. Only 'done' or 'faulted' are allowed.",
                [new ClientToolParameter("node_id", "The node id."),
                 new ClientToolParameter("status", "'done' or 'faulted'.")],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(SetNodeStatus(productRoot, a))),

            new DelegateClientTool("product_edit_node",
                "Edit a node's description and/or note (note = the rationale for shouldnt, or the repro for faulted). "
                + "Pass only the fields you want to change; an empty string clears a field.",
                [new ClientToolParameter("node_id", "The node id."),
                 new ClientToolParameter("description", "New description (optional).", Required: false),
                 new ClientToolParameter("note", "New note / rationale / repro (optional).", Required: false)],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(EditNode(productRoot, a))),

            new DelegateClientTool("product_add_node_snaplink",
                "Attach a snaplink to a node. type=markdown|code|url. target is the file path (relative to the product "
                + "folder) for markdown/code, or the URL for url. detail is optional: for markdown a heading path "
                + "(\"A > B\"), for code a Class or Class.Method.",
                SnaplinkParams(forConcern: false),
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(AddSnaplink(productRoot, a, forConcern: false))),

            new DelegateClientTool("product_set_concern_status",
                "Mark a node's concern 'done' or 'faulted' (e.g. set the 'tests' concern done). Only 'done' or 'faulted' are allowed.",
                [new ClientToolParameter("node_id", "The node id."),
                 new ClientToolParameter("concern", "The concern tag on that node."),
                 new ClientToolParameter("status", "'done' or 'faulted'.")],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(SetConcernStatus(productRoot, a))),

            new DelegateClientTool("product_add_concern_snaplink",
                "Attach a snaplink to one of a node's concerns. Same type/target/detail as product_add_node_snaplink, plus the concern tag.",
                SnaplinkParams(forConcern: true),
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(AddSnaplink(productRoot, a, forConcern: true))),

            // ── Edit (asks first) ─────────────────────────────────────────────
            new DelegateClientTool("product_set_snaplink",
                "Edit one existing snaplink in place — repoint a moved file, fix a renamed class, repair a markdown "
                + "heading path — instead of removing it and adding it back, which loses its status and its position. "
                + "Name it with index (product_zoom numbers them) or match (a substring of its display). Pass only the "
                + "fields to change; clear names fields to unset (\"class,method\"). Omit concern for the node's own links.",
                [new ClientToolParameter("node_id", "The node id."),
                 new ClientToolParameter("concern", "The concern tag, for a link on a concern rather than the node.", Required: false),
                 new ClientToolParameter("index", "Which snaplink, as numbered by product_zoom.", Required: false),
                 new ClientToolParameter("match", "…or a substring of its display; it must match exactly one.", Required: false),
                 new ClientToolParameter("expect", "Optional: text the link must still contain, so a renumbered list is refused rather than silently edited.", Required: false),
                 new ClientToolParameter("doc", "New file path.", Required: false),
                 new ClientToolParameter("class", "New class name.", Required: false),
                 new ClientToolParameter("method", "New method name.", Required: false),
                 new ClientToolParameter("ast", "New structure path.", Required: false),
                 new ClientToolParameter("target", "New target (node id for a node link, URL for a url link).", Required: false),
                 new ClientToolParameter("title_path", "New markdown heading path (\"A > B\").", Required: false),
                 new ClientToolParameter("status", "New status for the link.", Required: false),
                 new ClientToolParameter("clear", "Comma-separated fields to unset: class,method,ast,doc,target,title-path,status.", Required: false)],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(SetSnaplink(productRoot, a))),

            new DelegateClientTool("product_remove_node_snaplink",
                "Remove one snaplink from a node. Name it with index (product_zoom numbers them) or match (a "
                + "substring of its display, which must match exactly one). all=true removes every one — say it "
                + "outright; naming nothing removes nothing.",
                RemoveSnaplinkParams(forConcern: false),
                ToolSafety.RequiresApproval,
                (a, _) => Task.FromResult(RemoveSnaplink(productRoot, a, forConcern: false))),

            new DelegateClientTool("product_remove_concern_snaplink",
                "Remove one snaplink from a node's concern. Same index / match / all=true addressing as "
                + "product_remove_node_snaplink.",
                RemoveSnaplinkParams(forConcern: true),
                ToolSafety.RequiresApproval,
                (a, _) => Task.FromResult(RemoveSnaplink(productRoot, a, forConcern: true))),

            new DelegateClientTool("product_add_concern",
                "Attach a concern to a node (it must be one of the product's defined concerns). Starts at 'should'.",
                [new ClientToolParameter("node_id", "The node id."),
                 new ClientToolParameter("concern", "A defined concern tag.")],
                ToolSafety.RequiresApproval,
                (a, _) => Task.FromResult(AddConcern(productRoot, a))),

            new DelegateClientTool("product_remove_concern",
                "Remove a concern from a node (drops the link and its snaplinks).",
                [new ClientToolParameter("node_id", "The node id."),
                 new ClientToolParameter("concern", "The concern tag to remove.")],
                ToolSafety.RequiresApproval,
                (a, _) => Task.FromResult(RemoveConcern(productRoot, a))),

            // ── Navigate ──────────────────────────────────────────────────────
            // Everything above edits a node the caller already knows the id of. These are how it finds one.
            new DelegateClientTool("product_find",
                "Find nodes whose id, title or description contains a term — the way to turn a feature name "
                + "into node ids. Returns each match with the path that locates it.",
                [new ClientToolParameter("term", "Substring to search for.")],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(Find(productRoot, a))),

            new DelegateClientTool("product_query",
                "Filter nodes by subtree, concern, status and shape — 'which leaves under this feature still "
                + "owe a test', 'which nodes claim tests=done with nothing backing them'. Every filter is "
                + "optional; 'unbacked' needs 'concern' to mean anything.",
                [new ClientToolParameter("under", "Limit to this node's subtree.", Required: false),
                 new ClientToolParameter("concern", "Keep nodes carrying this concern; shows its status + snaplink count.", Required: false),
                 new ClientToolParameter("status", "should | done | shouldnt | faulted.", Required: false),
                 new ClientToolParameter("shape", "'leaf' or 'panel' to keep only one.", Required: false),
                 new ClientToolParameter("unbacked", "true to keep only nodes carrying the concern with NO snaplink.", Required: false, Type: "boolean")],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(Query(productRoot, a))),

            new DelegateClientTool("product_tree",
                "The whole subtree under a node as an indented outline — 'show me this entire feature'. "
                + "Set full=true to include each node's description, note and snaplinks rather than a link count.",
                [new ClientToolParameter("node_id", "Root of the subtree to print."),
                 new ClientToolParameter("depth", "Cap the walk (the node itself is 0).", Required: false, Type: "number"),
                 new ClientToolParameter("full", "true to include about/note/snaplinks.", Required: false, Type: "boolean")],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(Tree(productRoot, a))),

            // ── Check ─────────────────────────────────────────────────────────
            // Without these the model can set tests=done and never learn it just made a broken claim.
            new DelegateClientTool("product_validate",
                "Check every snaplink still points at a real target — file exists, markdown heading resolves, "
                + "class/method still declared, URL well formed — and that no concern requiring a snaplink is "
                + "done with nothing backing it. These are gating: the installer build fails on them.",
                [], ToolSafety.SafeOperation,
                (_, _) => Task.FromResult(Validate(productRoot))),

            new DelegateClientTool("product_lint",
                "Check the modelling rules — missing UI/Functionality backbone, a leaf with no tests concern, "
                + "a panel that carries one, a tests=done naming no test. Advisory: nothing here fails a build.",
                [new ClientToolParameter("under", "Limit to this feature's subtree.", Required: false)],
                ToolSafety.SafeOperation,
                (a, _) => Task.FromResult(Lint(productRoot, a))),

            // ── Structure ─────────────────────────────────────────────────────
            // Approval-gated: these change the shape of the tree rather than the contents of one node.
            new DelegateClientTool("product_add_node",
                "Add a child node. The id defaults to a slug of the title. Default concerns are attached, "
                + "so this is how a leaf grows sub-nodes when it turns out to be several behaviours.",
                [new ClientToolParameter("parent_id", "The parent node id."),
                 new ClientToolParameter("title", "Title for the new node."),
                 new ClientToolParameter("node_id", "Explicit id (defaults to a slug of the title).", Required: false),
                 new ClientToolParameter("description", "What the node covers.", Required: false),
                 new ClientToolParameter("status", "should | done | shouldnt | faulted (default should).", Required: false)],
                ToolSafety.RequiresApproval,
                (a, _) => Task.FromResult(AddNode(productRoot, a))),

            new DelegateClientTool("product_move_node",
                "Reparent a node and its subtree. Refuses a move that would make a cycle.",
                [new ClientToolParameter("node_id", "The node to move."),
                 new ClientToolParameter("new_parent_id", "Its new parent.")],
                ToolSafety.RequiresApproval,
                (a, _) => Task.FromResult(MoveNode(productRoot, a))),

            new DelegateClientTool("product_rename_node",
                "Change a node's id, retargeting its parent, its children and every node-type snaplink that "
                + "points at it. Ids are one flat global namespace, so this is how a too-generic one is "
                + "specialised. It cannot reach a [CoversNode(\"old-id\")] in test source — update those too.",
                [new ClientToolParameter("node_id", "The current id."),
                 new ClientToolParameter("new_id", "The new id.")],
                ToolSafety.RequiresApproval,
                (a, _) => Task.FromResult(RenameNode(productRoot, a))),

            new DelegateClientTool("product_remove_node",
                "Delete a node. Refuses a node that still has children unless recursive=true, which deletes "
                + "the whole subtree.",
                [new ClientToolParameter("node_id", "The node to delete."),
                 new ClientToolParameter("recursive", "true to delete its descendants too.", Required: false, Type: "boolean")],
                ToolSafety.RequiresApproval,
                (a, _) => Task.FromResult(RemoveNode(productRoot, a))),

            new DelegateClientTool("product_remap_snaplinks",
                "Rewrite snaplink paths after a rename or move — an exact file, or a directory prefix. The "
                + "safe way to follow code that moved, instead of re-pointing each link by hand.",
                [new ClientToolParameter("old_path", "The path (or directory prefix) as recorded today."),
                 new ClientToolParameter("new_path", "What it should become."),
                 new ClientToolParameter("class", "Also set this class on every affected link.", Required: false),
                 new ClientToolParameter("method", "Also set this method on every affected link.", Required: false)],
                ToolSafety.RequiresApproval,
                (a, _) => Task.FromResult(Remap(productRoot, a))),

            new DelegateClientTool("product_doctor",
                "Check the tree's own structure: every child id resolves, every node is listed by its parent, "
                + "and no snaplink points into a linked git worktree. Set fix=true to repair what it can — "
                + "dropping dangling child ids and re-rooting worktree paths onto the repo's own copy.",
                [new ClientToolParameter("fix", "true to apply the repairs rather than just report them.", Required: false, Type: "boolean")],
                ToolSafety.RequiresApproval,
                (a, _) => Task.FromResult(Doctor(productRoot, a))),
        ];
    }

    // ── Navigate / check ────────────────────────────────────────────────────
    //
    // Each of these is the same service call the CLI verb makes, rendered by the same ProductReport, so the
    // model reads exactly what a terminal would print. Anything the CLI can answer, the assistant can too.

    private static ToolResult Find(string root, JsonObject a)
    {
        var term = Str(a, "term");
        if (string.IsNullOrWhiteSpace(term)) return ToolResult.Error("Provide a 'term' to search for.");
        var hits = ProductQuery.Find(new ProductStore(root).Load(), term);
        return ToolResult.Ok($"{hits.Count} match(es) for '{term}'", ProductReport.Find(hits, term));
    }

    private static ToolResult Query(string root, JsonObject a)
    {
        var state = new ProductStore(root).Load();

        var under = Blank(Str(a, "under") ?? string.Empty);
        if (under is not null && !state.Nodes.ContainsKey(under)) return NoNode(under);

        Status? status = null;
        if (Blank(Str(a, "status") ?? string.Empty) is { } statusText)
        {
            if (!Enum.TryParse<Status>(statusText, ignoreCase: true, out var parsed))
                return ToolResult.Error($"Unknown status '{statusText}' (should | done | shouldnt | faulted).");
            status = parsed;
        }

        var shape = Blank(Str(a, "shape") ?? string.Empty)?.ToLowerInvariant();
        bool? leafOnly = shape switch { "leaf" => true, "panel" => false, null => null, _ => null };
        if (shape is not null and not "leaf" and not "panel")
            return ToolResult.Error($"Unknown shape '{shape}' (leaf | panel).");

        var concern = Blank(Str(a, "concern") ?? string.Empty);
        var hits = ProductQuery.Query(state, under, concern, status, leafOnly);

        if (Bool(a, "unbacked"))
        {
            if (concern is null) return ToolResult.Error("'unbacked' needs 'concern' — which concern is unbacked?");
            hits = [.. hits.Where(h => h.ConcernSnaplinks == 0)];
        }

        return ToolResult.Ok($"{hits.Count} node(s)", ProductReport.Query(hits, concern));
    }

    private static ToolResult Tree(string root, JsonObject a)
    {
        var id = Str(a, "node_id");
        if (string.IsNullOrWhiteSpace(id)) return ToolResult.Error("Provide a 'node_id' to print the subtree of.");

        int? depth = null;
        if (a["depth"] is { } d && int.TryParse(d.ToString(), out var parsed))
        {
            if (parsed < 0) return ToolResult.Error("'depth' must be zero or more.");
            depth = parsed;
        }

        var rows = ProductQuery.Outline(new ProductStore(root).Load(), id, depth);
        if (rows is null) return NoNode(id);
        return ToolResult.Ok($"{rows.Count} node(s) under {id}", ProductReport.Outline(rows, Bool(a, "full")));
    }

    private static ToolResult Validate(string root)
    {
        if (!ProductStore.Exists(root)) return ToolResult.Ok("no product", $"No .product/ under {root} — nothing to validate.");
        var state = new ProductStore(root).Load();
        var report = SnaplinkValidator.Validate(state, root, [root]);
        return ToolResult.Ok(report.IsClean ? "snaplinks OK" : $"{report.IssueCount} broken snaplink(s)",
                             ProductReport.Validate(report));
    }

    private static ToolResult Lint(string root, JsonObject a)
    {
        var store = new ProductStore(root);
        var state = store.Load();
        var under = Blank(Str(a, "under") ?? string.Empty);
        if (under is not null && !state.Nodes.ContainsKey(under)) return NoNode(under);

        // Absent manifest (never scanned, or a clean checkout) just means one fewer rule runs.
        var findings = StructureLinter.Lint(state, under, store.LoadTestCoverage());
        return ToolResult.Ok(findings.Count == 0 ? "follows the modelling rules" : $"{findings.Count} finding(s)",
                             ProductReport.Lint(findings, under));
    }

    // ── Structure ───────────────────────────────────────────────────────────

    private static ToolResult AddNode(string root, JsonObject a)
    {
        var parentId = Str(a, "parent_id");
        var title = Str(a, "title");
        if (string.IsNullOrWhiteSpace(parentId)) return ToolResult.Error("Provide a 'parent_id'.");
        if (string.IsNullOrWhiteSpace(title)) return ToolResult.Error("Provide a 'title'.");

        var store = new ProductStore(root);
        var state = store.Load();
        if (!state.Nodes.TryGetValue(parentId, out var parent)) return NoNode(parentId);

        string id;
        if (Blank(Str(a, "node_id") ?? string.Empty) is { } explicitId)
        {
            id = Slug(explicitId);
            if (state.Nodes.ContainsKey(id)) return ToolResult.Error($"Node id '{id}' already exists.");
        }
        else id = UniqueId(state, Slug(title));

        var status = ParseStatus(Str(a, "status"));
        if (status is null) return ToolResult.Error("'status' must be should | done | shouldnt | faulted.");

        // The default concerns are what make a new node lint-clean from the start.
        var defaults = state.Product.Concerns.Where(c => c.IsDefault).Select(c => c.Name).ToList();
        state.Nodes[id] = new ProductNode
        {
            Title = title,
            Description = Blank(Str(a, "description") ?? string.Empty),
            Status = status.Value,
            Parent = parentId,
            Children = [],
            Concerns = defaults.Count > 0
                ? [.. defaults.Select(n => new ConcernLink { Tag = n, Status = Status.Should })]
                : null,
        };
        parent.Children.Add(id);
        store.SaveTree(state.Nodes);
        return ToolResult.Ok($"added {id}", $"Added node '{id}' under '{parentId}': {title}.");
    }

    private static ToolResult MoveNode(string root, JsonObject a)
    {
        var id = Str(a, "node_id");
        var newParent = Str(a, "new_parent_id");
        var store = new ProductStore(root);
        var state = store.Load();
        if (id is null || !state.Nodes.ContainsKey(id)) return NoNode(id);
        if (newParent is null || !state.Nodes.ContainsKey(newParent)) return NoNode(newParent);

        if (!ProductTreeOps.Reparent(state, id, newParent))
            return ToolResult.Error($"Cannot move '{id}' under '{newParent}' — that would make a cycle.");

        store.SaveTree(state.Nodes);
        return ToolResult.Ok($"moved {id}", $"Moved '{id}' under '{newParent}'.");
    }

    private static ToolResult RenameNode(string root, JsonObject a)
    {
        var oldId = Str(a, "node_id");
        var newId = Str(a, "new_id");
        if (string.IsNullOrWhiteSpace(oldId) || string.IsNullOrWhiteSpace(newId))
            return ToolResult.Error("Provide both 'node_id' and 'new_id'.");

        var store = new ProductStore(root);
        var state = store.Load();
        return ProductTreeOps.Rename(state, oldId, newId) switch
        {
            ProductTreeOps.RenameError.NoSuchNode => NoNode(oldId),
            ProductTreeOps.RenameError.IdTaken => ToolResult.Error($"Node id '{newId}' already exists."),
            ProductTreeOps.RenameError.IdInvalid => ToolResult.Error($"'{newId}' is not a valid node id."),
            _ => Saved(store, state, $"renamed to {newId}",
                       $"Renamed '{oldId}' to '{newId}' — parent, children and node snaplinks retargeted. "
                     + $"Any [CoversNode(\"{oldId}\")] in test source still needs updating."),
        };
    }

    private static ToolResult RemoveNode(string root, JsonObject a)
    {
        var id = Str(a, "node_id");
        var store = new ProductStore(root);
        var state = store.Load();
        if (id is null || !state.Nodes.ContainsKey(id)) return NoNode(id);

        var recursive = Bool(a, "recursive");
        var removed = ProductTreeOps.Remove(state, id, recursive);
        if (removed is null)
            return ToolResult.Error($"'{id}' still has children — pass recursive=true to delete the whole subtree.");

        return Saved(store, state, $"removed {removed.Count} node(s)",
                     removed.Count == 1
                         ? $"Removed '{id}'."
                         : $"Removed '{id}' and {removed.Count - 1} descendant(s): {string.Join(", ", removed)}.");
    }

    private static ToolResult Remap(string root, JsonObject a)
    {
        var oldPath = Str(a, "old_path");
        var newPath = Str(a, "new_path");
        if (string.IsNullOrWhiteSpace(oldPath) || string.IsNullOrWhiteSpace(newPath))
            return ToolResult.Error("Provide both 'old_path' and 'new_path'.");

        var store = new ProductStore(root);
        var state = store.Load();
        var changed = SnaplinkRemapper.Remap(state, oldPath, newPath,
                                             Blank(Str(a, "class") ?? string.Empty),
                                             Blank(Str(a, "method") ?? string.Empty));
        if (changed == 0) return ToolResult.Ok("nothing to remap", $"No snaplink records '{oldPath}'.");

        return Saved(store, state, $"remapped {changed} snaplink(s)",
                     $"Rewrote {changed} snaplink(s) from '{oldPath}' to '{newPath}'.");
    }

    private static ToolResult Doctor(string root, JsonObject a)
    {
        var fix = Bool(a, "fix");
        var store = new ProductStore(root);
        var state = store.Load();

        var repairs = ProductTreeOps.RepairChildren(state, apply: fix);
        var orphans = state.Nodes
            .Where(kv => kv.Value.Parent is { } p && !state.Nodes.ContainsKey(p))
            .Select(kv => $"{kv.Key}: parent '{kv.Value.Parent}' does not exist")
            .ToList();
        var worktreeLinks = SnaplinkRemapper.NormalizeWorktreePaths(state, root);

        if (repairs.Count == 0 && orphans.Count == 0 && worktreeLinks.Count == 0)
            return ToolResult.Ok("tree structure OK",
                "Tree structure OK - every child id resolves, every node is listed by its parent, and no "
              + "snaplink points into a linked worktree.");

        var sb = new StringBuilder();
        foreach (var r in repairs)
            sb.AppendLine($"  {r.Parent}\n      before: [{string.Join(", ", r.Before)}]"
                        + $"\n      after:  [{string.Join(", ", r.After)}]"
                        + (r.Dropped.Count > 0 ? $"\n      dropped (unrecoverable): {string.Join(", ", r.Dropped)}" : ""));
        foreach (var o in orphans)
            sb.AppendLine($"  {o} - re-parent it with product_move_node (structural, not a children[] issue).");
        if (worktreeLinks.Count > 0)
        {
            sb.AppendLine($"  {worktreeLinks.Count} snaplink(s) point into a linked git worktree:");
            foreach (var (before, after) in worktreeLinks.DistinctBy(c => c.Before).OrderBy(c => c.Before, StringComparer.Ordinal))
                sb.AppendLine($"      {before}\n        -> {after}");
        }

        if (!fix)
        {
            sb.Append("Nothing was changed. Call again with fix=true to apply the repairs.");
            return ToolResult.Ok("repairs available", sb.ToString());
        }

        store.SaveTree(state.Nodes);
        sb.Append("Applied.");
        return ToolResult.Ok("repaired", sb.ToString());
    }

    /// <summary>Persists <paramref name="state"/> and reports success — the tail of every structural edit.</summary>
    private static ToolResult Saved(ProductStore store, ProductState state, string summary, string detail)
    {
        store.SaveTree(state.Nodes);
        return ToolResult.Ok(summary, detail);
    }

    private static bool Bool(JsonObject a, string key) =>
        a[key] is { } v && bool.TryParse(v.ToString(), out var b) && b;

    private static Status? ParseStatus(string? s) => string.IsNullOrWhiteSpace(s)
        ? Status.Should
        : Enum.TryParse<Status>(s, ignoreCase: true, out var parsed) ? parsed : null;

    /// <summary>A node id from free text: lowercase, non-alphanumerics to single hyphens.</summary>
    private static string Slug(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        var slug = sb.ToString();
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Trim('-');
    }

    private static string UniqueId(ProductState s, string seed)
    {
        if (!s.Nodes.ContainsKey(seed)) return seed;
        for (var i = 2; ; i++)
            if (!s.Nodes.ContainsKey($"{seed}-{i}")) return $"{seed}-{i}";
    }

    // ── Read renderers ──────────────────────────────────────────────────────

    /// <summary>The page-context description: purpose + the focused node's two visible rings + its details.</summary>
    public static string Describe(ProductState s, string? focusedId)
    {
        var sb = new StringBuilder()
            .AppendLine("Product Manager — tracks a product as a tree of component nodes, each with a status: "
                + "should (planned/todo), shouldnt (deliberately won't-do), done (shipped), faulted (broken). "
                + "Cross-cutting 'concerns' (e.g. tests, a11y) attach to nodes with their own status; snaplinks tie "
                + "nodes/concerns to docs/code/URLs. The sunburst shows two rings from the current focus; status "
                + "rolls up (faulted > should > done > shouldnt) and bundles a node's own concerns. Faulted items are "
                + "leaks that must not be lost.")
            .AppendLine()
            .Append(Survey(s));

        sb.AppendLine().AppendLine(Zoom(s, focusedId));

        var leaks = NeedsAttention(s);
        if (!leaks.StartsWith("Nothing")) sb.AppendLine().Append(leaks);
        return sb.ToString().TrimEnd();
    }

    public static string Survey(ProductState s)
    {
        var leaves = Leaves(s);
        int Count(Status st) => leaves.Count(id => ProductAggregator.EffectiveStatus(s, id) == st);
        var done = Count(Status.Done);
        return new StringBuilder()
            .AppendLine($"Product '{s.Product.Product}': {s.Nodes.Count} node(s), {leaves.Count} leaf component(s).")
            .Append($"  done {done}/{leaves.Count} · should {Count(Status.Should)} · "
                  + $"shouldnt {Count(Status.Shouldnt)} · faulted {Count(Status.Faulted)}")
            .ToString();
    }

    /// <summary>A node's neighbourhood: path up to the root, full details, and its children one ring down.</summary>
    public static string Zoom(ProductState s, string? nodeId)
    {
        var sb = new StringBuilder();
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            sb.AppendLine("Focus: the whole product (root).");
            sb.AppendLine("Children (top-level nodes):");
            foreach (var id in ProductAggregator.Roots(s)) sb.AppendLine("  " + ChildLine(s, id));
            return sb.ToString().TrimEnd();
        }

        if (!s.Nodes.TryGetValue(nodeId, out var node))
            return $"No node '{nodeId}'. Use product_zoom (no id) or product_survey to find node ids.";

        sb.AppendLine($"Focus: {string.Join(" › ", ProductAggregator.BreadcrumbPath(s, nodeId))}  (id: {nodeId})");
        var kids = node.Children.Where(s.Nodes.ContainsKey).ToList();
        sb.AppendLine(kids.Count == 0
            ? $"  status: {Disp(node.Status)}  (leaf)"
            : $"  status: {Disp(ProductAggregator.DerivedStatusExcludingConcerns(s, nodeId))}  (derived from children) · "
              + $"arc shows {Disp(ProductAggregator.EffectiveStatus(s, nodeId))}");
        sb.AppendLine($"  description: {Or(node.Description)}");
        sb.AppendLine($"  note: {Or(node.Note)}");
        sb.AppendLine($"  concerns: {Concerns(node)}");
        sb.AppendLine($"  snaplinks: {Snaplinks(node.Snaplinks)}");

        if (kids.Count > 0)
        {
            sb.AppendLine("Children (one ring down):");
            foreach (var id in kids) sb.AppendLine("  " + ChildLine(s, id));
        }
        return sb.ToString().TrimEnd();
    }

    public static string NeedsAttention(ProductState s)
    {
        var faulted = s.Nodes.Keys.Where(id => ProductAggregator.IsLocallyFaulted(s, id)).ToList();
        if (faulted.Count == 0) return "Nothing faulted — no leaks.";
        var sb = new StringBuilder("Faulted (needs attention):").AppendLine();
        foreach (var id in faulted)
        {
            var n = s.Nodes[id];
            sb.AppendLine($"  - {id} \"{n.Title}\"" + (string.IsNullOrWhiteSpace(n.Note) ? "" : $" — {n.Note}"));
        }
        return sb.ToString().TrimEnd();
    }

    private static string ChildLine(ProductState s, string id)
    {
        var n = s.Nodes[id];
        var kidCount = n.Children.Count(c => s.Nodes.ContainsKey(c));
        var shape = kidCount == 0 ? "leaf" : $"{kidCount} child{(kidCount == 1 ? "" : "ren")}";
        return $"- {id} \"{n.Title}\" [{Disp(ProductAggregator.EffectiveStatus(s, id))}] ({shape})";
    }

    /// <summary>
    /// A node's concerns, each with the links attached to it — listed rather than counted, because a concern's
    /// snaplinks are numbered separately from the node's own and an edit has to name which of the two it means.
    /// </summary>
    private static string Concerns(ProductNode n)
    {
        if (n.Concerns is not { Count: > 0 }) return "—";
        return string.Join(", ", n.Concerns.Select(c =>
            $"{c.Tag}[{Disp(c.Status)}]" + (c.Snaplinks is { Count: > 0 } ? $"({Snaplinks(c.Snaplinks)})" : "")));
    }

    /// <summary>
    /// A node's or concern's links, numbered. The number is what <c>product_set_snaplink</c> and the remove
    /// tools address a link by, so a listing that did not show it left them reachable only by substring.
    /// </summary>
    private static string Snaplinks(List<Snaplink>? links) =>
        links is { Count: > 0 }
            ? string.Join(", ", links.Select((l, i) => $"#{i} [{l.Type}] {l.Display}"))
            : "—";

    // ── Mutations ───────────────────────────────────────────────────────────

    private static ToolResult SetNodeStatus(string root, JsonObject a)
    {
        var id = Str(a, "node_id");
        if (ParseDoneOrFaulted(Str(a, "status")) is not { } status)
            return ToolResult.Error("status must be 'done' or 'faulted'.");
        var store = new ProductStore(root);
        var s = store.Load();
        if (id is null || !s.Nodes.ContainsKey(id)) return NoNode(id);
        ProductTreeOps.CascadeStatus(s, id, status);
        store.SaveTree(s.Nodes);
        return ToolResult.Ok($"{id} → {Disp(status)}", $"Set '{id}' (and its 'should' descendants/concerns) to {Disp(status)}.");
    }

    private static ToolResult EditNode(string root, JsonObject a) => WithNode(root, a, (_, node) =>
    {
        var desc = Str(a, "description");
        var note = Str(a, "note");
        if (desc is null && note is null) return ToolResult.Error("Pass 'description' and/or 'note'.");
        if (desc is not null) node.Description = Blank(desc);
        if (note is not null) node.Note = Blank(note);
        return ToolResult.Ok($"Edited {NodeId(a)}", $"Updated {NodeId(a)}.");
    });

    private static ToolResult SetConcernStatus(string root, JsonObject a) => WithNode(root, a, (_, node) =>
    {
        if (ParseDoneOrFaulted(Str(a, "status")) is not { } status)
            return ToolResult.Error("status must be 'done' or 'faulted'.");
        if (FindConcern(node, Str(a, "concern")) is not { } link) return NoConcern(a);
        link.Status = status;
        return ToolResult.Ok($"{link.Tag} → {Disp(status)}", $"Set concern '{link.Tag}' on {NodeId(a)} to {Disp(status)}.");
    });

    private static ToolResult AddSnaplink(string root, JsonObject a, bool forConcern) => WithNode(root, a, (_, node) =>
    {
        if (BuildSnaplink(Str(a, "type"), Str(a, "target"), Str(a, "detail")) is not { } link)
            return ToolResult.Error("type must be markdown|code|url and target must be set.");
        if (forConcern)
        {
            if (FindConcern(node, Str(a, "concern")) is not { } c) return NoConcern(a);
            (c.Snaplinks ??= []).Add(link);
            return ToolResult.Ok($"+snaplink on {c.Tag}", $"Added [{link.Type}] {link.Display} to concern '{c.Tag}'.");
        }
        (node.Snaplinks ??= []).Add(link);
        return ToolResult.Ok($"+snaplink on {NodeId(a)}", $"Added [{link.Type}] {link.Display} to {NodeId(a)}.");
    });

    /// <summary>
    /// Removes one named snaplink, or — asked outright — all of them. Three ways to name it, because the
    /// index a listing shows is a position that any other edit renumbers, and a substring is what the model
    /// actually reads back from <see cref="Zoom"/>.
    /// <para>
    /// A substring that matches several links is refused rather than resolved to the first one. Taking the
    /// first is how a caller that meant one link removed a different one and could not tell — the same shape
    /// as the omitted selector that used to mean "all of them".
    /// </para>
    /// </summary>
    private static ToolResult RemoveSnaplink(string root, JsonObject a, bool forConcern) => WithNode(root, a, (s, node) =>
    {
        var id = Str(a, "node_id")!;
        string? tag = null;
        if (forConcern)
        {
            if (FindConcern(node, Str(a, "concern")) is not { } c) return NoConcern(a);
            tag = c.Tag;
        }

        if (ProductTreeOps.SnaplinksOf(s, id, tag) is not { Count: > 0 } links)
            return ToolResult.Error($"{NodeId(a)} has no snaplinks to remove.");

        if (Bool(a, "all"))
            return ToolResult.Ok("Cleared snaplinks",
                $"Removed all {ProductTreeOps.ClearSnaplinks(s, id, tag)} snaplink(s) from {Where(a, tag)}.");

        var chosen = Chosen(a, links);
        if (chosen.Error is { } why) return ToolResult.Error(why);

        var (link, index) = chosen.Hit!.Value;
        ProductTreeOps.RemoveSnaplink(s, id, tag, index);
        return ToolResult.Ok("Removed snaplink", $"Removed #{index} [{link.Type}] {link.Display} from {Where(a, tag)}.");
    });

    /// <summary>
    /// Edits one existing snaplink in place — the repair that removing and re-adding cannot make without
    /// losing the link's other fields, its status and its position. Pass only the fields to change; 'clear'
    /// names fields to unset.
    /// </summary>
    private static ToolResult SetSnaplink(string root, JsonObject a) => WithNode(root, a, (s, node) =>
    {
        var id = Str(a, "node_id")!;
        string? tag = null;
        if (Str(a, "concern") is { Length: > 0 })
        {
            if (FindConcern(node, Str(a, "concern")) is not { } c) return NoConcern(a);
            tag = c.Tag;
        }

        if (ProductTreeOps.SnaplinksOf(s, id, tag) is not { Count: > 0 } links)
            return ToolResult.Error($"{Where(a, tag)} has no snaplinks to edit — add one first.");

        var chosen = Chosen(a, links);
        if (chosen.Error is { } why) return ToolResult.Error(why);
        var (link, index) = chosen.Hit!.Value;

        // An index is a position, and anything that adds or removes a link renumbers the rest — so the listing
        // it was read from is always older than the edit. 'expect' pins the edit to what was read.
        if (Str(a, "expect") is { Length: > 0 } expect
            && !new[] { link.Doc, link.Class, link.Method, link.Ast, link.Target, link.Type }
                    .Any(f => f is not null && f.Contains(expect, StringComparison.Ordinal)))
            return ToolResult.Error($"Snaplink #{index} no longer contains '{expect}' (it is now {link.Display}) — re-read it with product_zoom.");

        Status? status = null;
        if (Str(a, "status") is { Length: > 0 } st)
        {
            if (ParseStatus(st) is not { } parsed) return ToolResult.Error($"Unknown status '{st}'.");
            status = parsed;
        }

        var fields = new (string Key, Action<Snaplink, string> Assign)[]
        {
            ("doc",        (l, v) => l.Doc = v),
            ("class",      (l, v) => l.Class = v),
            ("method",     (l, v) => l.Method = v),
            ("ast",        (l, v) => l.Ast = v),
            ("target",     (l, v) => l.Target = v),
            ("title_path", (l, v) => l.TitlePath =
                 [.. v.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)]),
        };
        var given = fields.Where(f => Str(a, f.Key) is not null).ToList();
        var clear = Str(a, "clear") is { Length: > 0 } names
            ? names.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            : [];

        if (given.Count == 0 && clear.Length == 0 && status is null)
            return ToolResult.Error("Nothing to change — pass a field (" + string.Join('/', fields.Select(f => f.Key))
                                  + "/status), or 'clear' naming fields to unset.");

        // Resolved and applied by the shared op, which refuses an unknown 'clear' field having assigned nothing.
        if (!ProductTreeOps.SetSnaplink(s, id, index, tag, link =>
            {
                foreach (var (key, assign) in given) assign(link, Str(a, key)!);
                if (status is not null) link.Status = status;
            }, clear))
            return ToolResult.Error("'clear' names a field a snaplink does not have "
                                  + "(class|method|ast|doc|target|title-path|status).");

        return ToolResult.Ok("Edited snaplink", $"Snaplink #{index} on {Where(a, tag)} is now [{link.Type}] {link.Display}.");
    });

    /// <summary>
    /// The one link an 'index' or 'match' argument names, or the refusal that says why it names none — the
    /// shared half of editing and removing, so both answer a bad handle the same way.
    /// </summary>
    private static ((Snaplink Link, int Index)? Hit, string? Error) Chosen(JsonObject a, List<Snaplink> links)
    {
        if (Str(a, "index") is { Length: > 0 } raw)
            return int.TryParse(raw, out var i)
                ? i >= 0 && i < links.Count
                    ? ((links[i], i), null)
                    : (null, $"No snaplink #{i} — there {(links.Count == 1 ? "is 1" : $"are {links.Count}")}; see product_zoom.")
                : (null, $"'index' must be a number (got '{raw}').");

        var match = Str(a, "match");
        if (string.IsNullOrWhiteSpace(match))
            return (null, "Say which snaplink: 'index' (product_zoom numbers them) or 'match' (a substring of its display).");

        var hits = links.Select((l, i) => (Link: l, Index: i))
                        .Where(x => x.Link.Display.Contains(match, StringComparison.OrdinalIgnoreCase))
                        .ToList();
        return hits.Count switch
        {
            0 => (null, $"No snaplink matching '{match}'."),
            1 => (hits[0], null),
            _ => (null, $"'{match}' matches {hits.Count} snaplinks ("
                      + string.Join("; ", hits.Select(h => $"#{h.Index} {h.Link.Display}"))
                      + ") — narrow it, or pass 'index'."),
        };
    }

    private static string Where(JsonObject a, string? tag) =>
        tag is null ? NodeId(a) : $"{NodeId(a)} concern '{tag}'";

    private static ToolResult AddConcern(string root, JsonObject a) => WithNode(root, a, (s, node) =>
    {
        var tag = Str(a, "concern")?.Trim();
        if (string.IsNullOrWhiteSpace(tag)) return ToolResult.Error("Pass 'concern'.");
        if (s.Product.Concerns.All(c => !string.Equals(c.Name, tag, StringComparison.OrdinalIgnoreCase)))
            return ToolResult.Error($"'{tag}' is not a defined concern. Defined: {string.Join(", ", s.Product.Concerns.Select(c => c.Name))}.");
        node.Concerns ??= [];
        if (node.Concerns.Any(c => string.Equals(c.Tag, tag, StringComparison.OrdinalIgnoreCase)))
            return ToolResult.Error($"{NodeId(a)} already has concern '{tag}'.");
        node.Concerns.Add(new ConcernLink { Tag = tag, Status = Status.Should });
        return ToolResult.Ok($"+concern {tag}", $"Added concern '{tag}' to {NodeId(a)} (should).");
    });

    private static ToolResult RemoveConcern(string root, JsonObject a) => WithNode(root, a, (_, node) =>
    {
        if (FindConcern(node, Str(a, "concern")) is not { } link) return NoConcern(a);
        node.Concerns!.Remove(link);
        if (node.Concerns is { Count: 0 }) node.Concerns = null;
        return ToolResult.Ok($"-concern {link.Tag}", $"Removed concern '{link.Tag}' from {NodeId(a)}.");
    });

    // ── Plumbing ────────────────────────────────────────────────────────────

    /// <summary>Loads the product, finds the node by <c>node_id</c>, runs <paramref name="edit"/>, and saves on success.</summary>
    private static ToolResult WithNode(string root, JsonObject a, Func<ProductState, ProductNode, ToolResult> edit)
    {
        var id = Str(a, "node_id");
        var store = new ProductStore(root);
        var s = store.Load();
        if (id is null || !s.Nodes.TryGetValue(id, out var node)) return NoNode(id);
        var result = edit(s, node);
        if (result.Success) store.SaveTree(s.Nodes);
        return result;
    }

    private static readonly ConcernLink Missing = new() { Tag = "" };

    private static ConcernLink? FindConcern(ProductNode node, string? tag) =>
        string.IsNullOrWhiteSpace(tag) ? null
        : node.Concerns?.FirstOrDefault(c => string.Equals(c.Tag, tag, StringComparison.OrdinalIgnoreCase));

    private static Snaplink? BuildSnaplink(string? type, string? target, string? detail)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        target = target.Trim();
        switch (type?.Trim().ToLowerInvariant())
        {
            case "url":
                return new Snaplink { Type = "url", Target = target, Status = Status.Should };
            case "md":
            case "markdown":
                return new Snaplink
                {
                    Type = "markdown", Doc = target, Status = Status.Should,
                    TitlePath = string.IsNullOrWhiteSpace(detail) ? null
                        : detail.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList()
                };
            case "code":
                var parts = (detail ?? "").Split('.', 2);
                return new Snaplink
                {
                    Type = "code", Doc = target, Status = Status.Should,
                    Class = string.IsNullOrWhiteSpace(detail) ? null : parts[0].Trim(),
                    Method = parts.Length > 1 ? parts[1].Trim() : null
                };
            default:
                return null;
        }
    }

    private static IReadOnlyList<ClientToolParameter> SnaplinkParams(bool forConcern)
    {
        var list = new List<ClientToolParameter> { new("node_id", "The node id.") };
        if (forConcern) list.Add(new ClientToolParameter("concern", "The concern tag on that node."));
        list.Add(new ClientToolParameter("type", "markdown | code | url."));
        list.Add(new ClientToolParameter("target", "File path (relative to the product folder) for markdown/code, or the URL for url."));
        list.Add(new ClientToolParameter("detail", "Optional: markdown heading path (\"A > B\"), or code Class / Class.Method.", Required: false));
        return list;
    }

    private static IReadOnlyList<ClientToolParameter> RemoveSnaplinkParams(bool forConcern)
    {
        var list = new List<ClientToolParameter> { new("node_id", "The node id.") };
        if (forConcern) list.Add(new ClientToolParameter("concern", "The concern tag on that node."));
        list.Add(new ClientToolParameter("index", "Which snaplink, as numbered by product_zoom.", Required: false));
        list.Add(new ClientToolParameter("match", "…or a substring of its display; it must match exactly one.", Required: false));
        list.Add(new ClientToolParameter("all", "true to remove every snaplink here. Takes no index and no match.", Required: false, Type: "boolean"));
        return list;
    }

    private static Status? ParseDoneOrFaulted(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "done" => Status.Done,
        "faulted" => Status.Faulted,
        _ => null
    };

    private static List<string> Leaves(ProductState s) =>
        s.Nodes.Where(kv => kv.Value.Children.All(c => !s.Nodes.ContainsKey(c))).Select(kv => kv.Key).ToList();

    private static ToolResult NoNode(string? id) => ToolResult.Error($"No node '{id}'. Use product_zoom / product_survey to find node ids.");
    private static ToolResult NoConcern(JsonObject a) => ToolResult.Error($"No concern '{Str(a, "concern")}' on {NodeId(a)}.");
    private static string NodeId(JsonObject a) => $"'{Str(a, "node_id")}'";
    private static string Disp(Status s) => s.ToString().ToLowerInvariant();
    private static string Or(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string? Str(JsonObject a, string key)
    {
        if (!a.TryGetPropertyValue(key, out var node) || node is null) return null;
        try { return node.GetValue<string>(); } catch { return node.ToString(); }
    }
}

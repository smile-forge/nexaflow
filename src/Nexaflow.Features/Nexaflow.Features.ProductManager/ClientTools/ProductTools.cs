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
    public static IReadOnlyList<IClientTool> ForRoot(string productRoot)
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
            new DelegateClientTool("product_remove_node_snaplink",
                "Remove a snaplink from a node. match is a substring of the snaplink's display (see product_zoom); the first match is removed.",
                [new ClientToolParameter("node_id", "The node id."),
                 new ClientToolParameter("match", "Substring of the snaplink to remove.")],
                ToolSafety.RequiresApproval,
                (a, _) => Task.FromResult(RemoveSnaplink(productRoot, a, forConcern: false))),

            new DelegateClientTool("product_remove_concern_snaplink",
                "Remove a snaplink from one of a node's concerns. match is a substring of the snaplink's display.",
                [new ClientToolParameter("node_id", "The node id."),
                 new ClientToolParameter("concern", "The concern tag."),
                 new ClientToolParameter("match", "Substring of the snaplink to remove.")],
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
        ];
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

    private static string Concerns(ProductNode n)
    {
        if (n.Concerns is not { Count: > 0 }) return "—";
        return string.Join(", ", n.Concerns.Select(c =>
            $"{c.Tag}[{Disp(c.Status)}]" + (c.Snaplinks is { Count: > 0 } sl ? $"({sl.Count} snaplink{(sl.Count == 1 ? "" : "s")})" : "")));
    }

    private static string Snaplinks(List<Snaplink>? links) =>
        links is { Count: > 0 } ? string.Join(", ", links.Select(l => $"[{l.Type}] {l.Display}")) : "—";

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

    private static ToolResult RemoveSnaplink(string root, JsonObject a, bool forConcern) => WithNode(root, a, (_, node) =>
    {
        var match = Str(a, "match");
        if (string.IsNullOrWhiteSpace(match)) return ToolResult.Error("Pass 'match'.");
        List<Snaplink>? list = forConcern ? (FindConcern(node, Str(a, "concern")) ?? Missing).Snaplinks : node.Snaplinks;
        if (forConcern && FindConcern(node, Str(a, "concern")) is null) return NoConcern(a);
        var hit = list?.FirstOrDefault(l => l.Display.Contains(match, StringComparison.OrdinalIgnoreCase));
        if (hit is null) return ToolResult.Error($"No snaplink matching '{match}'.");
        list!.Remove(hit);
        return ToolResult.Ok("Removed snaplink", $"Removed [{hit.Type}] {hit.Display}.");
    });

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

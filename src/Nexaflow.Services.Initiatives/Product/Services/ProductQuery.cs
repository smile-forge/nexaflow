using Nexaflow.Services.Initiatives.Product.Model;

namespace Nexaflow.Services.Initiatives.Product.Services;

/// <summary>
/// Read-only lookups over a loaded tree — the "where is feature X, and where's its code/tests/docs" index.
/// Pure functions over <see cref="ProductState"/> so both the CLI and any tooling share one implementation.
/// </summary>
public static class ProductQuery
{
    /// <summary>A node id + title in the ancestor chain (root first).</summary>
    public readonly record struct Crumb(string Id, string Title);

    /// <summary>A search hit: the node plus the path that locates it.</summary>
    public sealed record Hit(string Id, string Title, Status Status, IReadOnlyList<Crumb> Path);

    /// <summary>A snaplink grouped for display: which bucket it belongs to and a one-line target.</summary>
    public sealed record Link(string Kind, string Display);

    /// <summary>A <see cref="Query"/> match: the node, whether it's a leaf, its <b>derived</b> status (the
    /// rolled-up value the UI shows — a leaf's own status, a parent's roll-up), and — when the query filtered
    /// on a concern — that concern link's status and how many snaplinks back it.</summary>
    public sealed record QueryHit(
        string Id, string Title, IReadOnlyList<Crumb> Path, bool IsLeaf,
        Status NodeStatus, Status? ConcernStatus, int ConcernSnaplinks);

    /// <summary>Everything <c>describe</c> shows about one node.</summary>
    public sealed record Detail(
        string Id, string Title, Status Status, string? Description, string? Note,
        IReadOnlyList<Crumb> Path,
        IReadOnlyList<(string Tag, Status Status)> Concerns,
        IReadOnlyList<Link> Snaplinks,
        IReadOnlyList<(string Id, string Title, Status Status)> Children);

    /// <summary>Root → node ancestor chain (guards against a malformed parent cycle).</summary>
    public static IReadOnlyList<Crumb> PathTo(ProductState state, string id)
    {
        var chain = new List<Crumb>();
        var seen = new HashSet<string>();
        for (var cur = id; cur is not null && state.Nodes.TryGetValue(cur, out var n) && seen.Add(cur); cur = n.Parent)
            chain.Insert(0, new Crumb(cur, n.Title));
        return chain;
    }

    /// <summary>Nodes whose id, title or description contains <paramref name="term"/> (case-insensitive). The
    /// reported <see cref="Hit.Status"/> is the <b>derived</b> status the UI shows (a parent rolls up its
    /// children), not the meaningless stored field a parent carries — see <see cref="DisplayStatus"/>.</summary>
    public static IReadOnlyList<Hit> Find(ProductState state, string term)
    {
        var hits = new List<Hit>();
        foreach (var (id, node) in state.Nodes)
        {
            if (Contains(id, term) || Contains(node.Title, term) || Contains(node.Description, term))
                hits.Add(new Hit(id, node.Title, DisplayStatus(state, id), PathTo(state, id)));
        }
        return [.. hits.OrderBy(h => h.Title, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The status to <em>show</em> for a node — the detail-pane pill
    /// (<see cref="ProductAggregator.DerivedStatusExcludingConcerns"/>): a leaf → its stored status; a parent →
    /// the roll-up of its children (whose own concerns fold in). This is what the UI displays, so <c>find</c> /
    /// <c>query</c> / <c>describe</c> report it instead of a parent's stored field, which is never shown.</summary>
    public static Status DisplayStatus(ProductState state, string id) =>
        ProductAggregator.DerivedStatusExcludingConcerns(state, id);

    /// <summary>Full detail for one node, or null when the id is unknown.</summary>
    public static Detail? Describe(ProductState state, string id)
    {
        if (!state.Nodes.TryGetValue(id, out var node)) return null;

        var concerns = (node.Concerns ?? []).Select(c => (c.Tag, c.Status)).ToList();

        var links = new List<Link>();
        foreach (var s in node.Snaplinks ?? []) links.Add(ToLink(s));
        // concern-level snaplinks are evidence too — label them by the concern they satisfy.
        foreach (var c in node.Concerns ?? [])
            foreach (var s in c.Snaplinks ?? [])
                links.Add(ToLink(s, c.Tag));

        var children = (node.Children ?? [])
            .Where(state.Nodes.ContainsKey)
            .Select(cid => (cid, state.Nodes[cid].Title, DisplayStatus(state, cid)))
            .ToList();

        return new Detail(id, node.Title, DisplayStatus(state, id), node.Description, node.Note,
            PathTo(state, id), concerns, links, children);
    }

    /// <summary>
    /// Lists nodes filtered by subtree, concern, status and leaf-ness — the "which leaves still owe a test"
    /// query the CLI's <c>query</c> command exposes. <paramref name="under"/> limits to that node's subtree
    /// (the node itself + all descendants); <paramref name="concern"/> keeps only nodes that carry that concern
    /// tag and reports its link status + snaplink count; <paramref name="status"/> matches the concern's status
    /// when a concern is given, otherwise the node's <b>derived</b> status (a parent rolls up its children —
    /// see <see cref="DisplayStatus"/>); <paramref name="leafOnly"/> keeps leaves (true) or parents (false), or
    /// both (null). Results are ordered by tree path for stable, readable output.
    /// </summary>
    public static IReadOnlyList<QueryHit> Query(
        ProductState state, string? under = null, string? concern = null,
        Status? status = null, bool? leafOnly = null)
    {
        var ids = under is { Length: > 0 } ? Subtree(state, under) : state.Nodes.Keys;

        var hits = new List<QueryHit>();
        foreach (var id in ids)
        {
            if (!state.Nodes.TryGetValue(id, out var node)) continue;

            var isLeaf = !(node.Children ?? []).Any(state.Nodes.ContainsKey);
            if (leafOnly is { } lo && lo != isLeaf) continue;

            ConcernLink? link = null;
            if (concern is { Length: > 0 })
            {
                link = (node.Concerns ?? []).FirstOrDefault(c =>
                    string.Equals(c.Tag, concern, StringComparison.OrdinalIgnoreCase));
                if (link is null) continue;   // node doesn't carry this concern at all
            }

            var display = DisplayStatus(state, id);
            if (status is { } want && (link?.Status ?? display) != want) continue;

            hits.Add(new QueryHit(id, node.Title, PathTo(state, id), isLeaf,
                display, link?.Status, link?.Snaplinks?.Count ?? 0));
        }

        return [.. hits.OrderBy(
            h => string.Join("/", h.Path.Select(c => c.Title)), StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The ids in a node's subtree — the node itself plus every descendant — cycle-safe.</summary>
    private static IEnumerable<string> Subtree(ProductState state, string root)
    {
        var stack = new Stack<string>();
        var seen = new HashSet<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!seen.Add(id) || !state.Nodes.TryGetValue(id, out var n)) continue;
            yield return id;
            foreach (var c in n.Children ?? []) stack.Push(c);
        }
    }

    /// <summary>Buckets a snaplink as test / code / doc / url and renders a one-line target.</summary>
    private static Link ToLink(Snaplink s, string? concern = null)
    {
        var kind = s.Type switch
        {
            "url" => "url",
            "markdown" => "doc",
            _ when s.Doc is not null && s.Doc.Contains("Nexaflow.Tests", StringComparison.OrdinalIgnoreCase) => "test",
            _ => "code"
        };
        // Keep the bucket for grouping; note the concern (if any) inline so it doesn't fragment the groups.
        var display = concern is null ? s.Display : $"({concern}) {s.Display}";
        return new Link(kind, display);
    }

    private static bool Contains(string? haystack, string term) =>
        haystack is not null && haystack.Contains(term, StringComparison.OrdinalIgnoreCase);
}

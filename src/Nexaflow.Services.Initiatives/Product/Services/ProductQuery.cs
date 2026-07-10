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

    /// <summary>Nodes whose id, title or description contains <paramref name="term"/> (case-insensitive).</summary>
    public static IReadOnlyList<Hit> Find(ProductState state, string term)
    {
        var hits = new List<Hit>();
        foreach (var (id, node) in state.Nodes)
        {
            if (Contains(id, term) || Contains(node.Title, term) || Contains(node.Description, term))
                hits.Add(new Hit(id, node.Title, node.Status, PathTo(state, id)));
        }
        return [.. hits.OrderBy(h => h.Title, StringComparer.OrdinalIgnoreCase)];
    }

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
            .Select(cid => (cid, state.Nodes[cid].Title, state.Nodes[cid].Status))
            .ToList();

        return new Detail(id, node.Title, node.Status, node.Description, node.Note,
            PathTo(state, id), concerns, links, children);
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

namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>
/// A component in the product's tree of truth. The node itself carries a <see cref="Status"/> — there
/// is no separate "items" list; capturing a fragment means adding a node in some status. The node id is
/// the key in <c>tree.json</c>'s <c>nodes</c> map, not a field here. Parent dashboards roll up the
/// statuses of their descendant leaves.
/// </summary>
/// <remarks>
/// Optional collections (<see cref="Concerns"/>, <see cref="Snaplinks"/>) and nullable strings are
/// omitted on save so a bare node (title + status) emits no noise; <see cref="Children"/> always
/// serializes as an array.
/// </remarks>
public sealed class ProductNode
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Status Status { get; set; } = Status.Should;

    /// <summary>Why it's in this status — the rationale for <c>shouldnt</c>, the repro for <c>faulted</c>.</summary>
    public string? Note { get; set; }

    public string? Parent { get; set; }
    public List<string> Children { get; set; } = [];

    /// <summary>This node's links to cross-cutting concerns, each carrying a status.</summary>
    public List<ConcernLink>? Concerns { get; set; }

    /// <summary>Loose, typed connections to docs/code/etc.</summary>
    public List<Snaplink>? Snaplinks { get; set; }

    /// <summary>A copy of this node, down to every concern link and snaplink — see <see cref="ProductState.Copy"/>.</summary>
    public ProductNode Copy() => new()
    {
        Title = Title, Description = Description, Status = Status, Note = Note,
        Parent = Parent, Children = [.. Children],
        Concerns = Concerns is null ? null : [.. Concerns.Select(c => c.Copy())],
        Snaplinks = Snaplinks is null ? null : [.. Snaplinks.Select(l => l.Copy())],
    };
}

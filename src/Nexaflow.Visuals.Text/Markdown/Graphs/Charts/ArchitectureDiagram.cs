namespace Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

/// <summary>The side of a service/group box an edge attaches to.</summary>
public enum ArchSide { Left, Right, Top, Bottom }

/// <summary>A group box (<c>group</c>) that visually contains services and, optionally, nested groups.</summary>
public sealed class ArchGroup
{
    public required string Id { get; init; }
    public string? Icon  { get; set; }
    public string  Title { get; set; } = string.Empty;
    /// <summary>Enclosing group id (from <c>in {parent}</c>), or null at the top level.</summary>
    public string? ParentId { get; set; }

    public string Display => string.IsNullOrEmpty(Title) ? Id : Title;
}

/// <summary>A service node (<c>service</c>) or a routing <c>junction</c> (when <see cref="IsJunction"/>).</summary>
public sealed class ArchService
{
    public required string Id { get; init; }
    public string? Icon    { get; set; }
    public string  Title   { get; set; } = string.Empty;
    /// <summary>Owning group id (from <c>in {group}</c>), or null when ungrouped.</summary>
    public string? GroupId { get; set; }
    public bool    IsJunction { get; set; }

    public string Display => string.IsNullOrEmpty(Title) ? Id : Title;
}

/// <summary>A directed edge anchored to a specific side of each endpoint.  Either endpoint may be a
/// group (the <c>{group}</c> suffix) rather than a service.</summary>
public sealed class ArchEdge
{
    public required string FromId { get; init; }
    public ArchSide FromSide { get; set; }
    public bool     FromIsGroup { get; set; }

    public required string ToId { get; init; }
    public ArchSide ToSide { get; set; }
    public bool     ToIsGroup { get; set; }

    /// <summary>Arrowhead at the source end (the <c>&lt;</c> modifier).</summary>
    public bool StartArrow { get; set; }
    /// <summary>Arrowhead at the target end (the <c>&gt;</c> modifier).</summary>
    public bool EndArrow   { get; set; }
}

/// <summary>An <c>align row</c> (same y) or <c>align column</c> (same x) constraint over a set of ids.</summary>
public sealed class ArchAlignment
{
    public bool IsRow { get; init; }
    public List<string> Ids { get; } = [];
}

/// <summary>
/// Data model for a Mermaid <c>architecture-beta</c> diagram: groups, the services (and junctions) inside
/// them, and the side-anchored edges between them.  Rendered by a dedicated grid renderer (not the
/// Sugiyama pipeline).  Front-matter <c>config.architecture</c> options live on <see cref="Config"/>.
/// </summary>
public sealed class ArchitectureDiagram
{
    public string Title { get; set; } = string.Empty;
    public List<ArchGroup>     Groups     { get; } = [];
    /// <summary>Services and junctions (junctions carry <see cref="ArchService.IsJunction"/>).</summary>
    public List<ArchService>   Services   { get; } = [];
    public List<ArchEdge>      Edges      { get; } = [];
    public List<ArchAlignment> Alignments { get; } = [];
    public ArchitectureConfig  Config     { get; set; } = new();

    public ArchService? FindService(string id) =>
        Services.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

    public ArchGroup? FindGroup(string id) =>
        Groups.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.Ordinal));
}

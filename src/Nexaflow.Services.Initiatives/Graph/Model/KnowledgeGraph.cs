namespace Nexaflow.Services.Initiatives.Graph.Model;

/// <summary>
/// The <c>graph.json</c> root — the portable, resolved graph the viewer and CLI consume. Per-file content hashes
/// are NOT stored here; they live in the sibling <c>graph-cache.json</c> so this file stays a clean output.
/// Positions are never stored — layout is a pure view concern recomputed at load.
/// </summary>
public sealed class KnowledgeGraph
{
    public List<GraphNode> Nodes { get; set; } = [];
    public List<GraphEdge> Edges { get; set; } = [];
    public List<GraphHyperEdge> HyperEdges { get; set; } = [];
    public GraphMetadata Metadata { get; set; } = new();
}

/// <summary>Summary counts + provenance stamp for a <see cref="KnowledgeGraph"/>.</summary>
public sealed class GraphMetadata
{
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }
    public int HyperEdgeCount { get; set; }
    public int CommunityCount { get; set; }

    /// <summary>ISO-8601, stamped by the caller (CLI / BuildGraphTask) — the builder itself stays clock-free
    /// so its output is byte-deterministic for tests.</summary>
    public string? GeneratedAt { get; set; }

    /// <summary>Extractor schema version (<see cref="GraphSchema.Version"/>) — a bump forces a full re-extract.</summary>
    public int SchemaVersion { get; set; } = GraphSchema.Version;

    /// <summary><c>whole_repo</c> | <c>product_anchored</c>.</summary>
    public string? Scope { get; set; }

    public string? ProductName { get; set; }
}

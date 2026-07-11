namespace Nexaflow.Services.Initiatives.Graph.Model;

/// <summary>
/// A binary, directed relationship between two nodes. Structural edges (<c>contains</c>/<c>extends</c>/
/// <c>implements</c>/<c>imports</c>) are extracted (confidence 1.0); <c>references</c>/<c>instantiates</c> are
/// name-resolved and carry a rung of the <see cref="GraphConfidence"/> ladder. Richer n-ary relationships live
/// in <see cref="GraphHyperEdge"/>.
/// </summary>
public sealed class GraphEdge
{
    /// <summary>Source node id.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Target node id.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>One of <see cref="EdgeRelationship"/> (open vocabulary).</summary>
    public string Relationship { get; set; } = string.Empty;

    /// <summary>Incremented when the same (source, target, relationship) is emitted again (merge count).</summary>
    public double Weight { get; set; } = 1.0;

    public double Confidence { get; set; } = GraphConfidence.Extracted;

    /// <summary>Provenance — the repo-relative file whose syntax asserts this edge (for incremental prune);
    /// <c>"product"</c> for snaplink-derived edges.</summary>
    public string? ProvenanceFile { get; set; }

    public Dictionary<string, string>? Metadata { get; set; }
}

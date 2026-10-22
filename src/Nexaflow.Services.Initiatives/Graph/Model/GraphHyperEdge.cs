namespace Nexaflow.Services.Initiatives.Graph.Model;

/// <summary>
/// An n-ary relationship over ≥2 roled endpoints — the model addition graphify lacks. A <c>calls</c> hyperedge
/// carries (caller, callee, arg…); <c>signature</c> carries (member, return, param…); <c>annotated</c> carries
/// (target, attr, named-arg…). The <see cref="GraphifyExport"/> downcast decomposes each into binary edges.
/// </summary>
public sealed class GraphHyperEdge
{
    /// <summary>One of <see cref="HyperRelationship"/>.</summary>
    public string Relationship { get; set; } = string.Empty;

    public List<HyperEndpoint> Endpoints { get; set; } = [];

    public double Weight { get; set; } = 1.0;

    /// <summary>Aggregate confidence — the primary resolved endpoint's (callee / return+params / attr).</summary>
    public double Confidence { get; set; } = GraphConfidence.Extracted;

    /// <summary>Provenance — the repo-relative file whose syntax asserts this hyperedge.</summary>
    public string? ProvenanceFile { get; set; }

    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>A hyperedge equal to this one that shares nothing it could change — see <see cref="GraphNode.Copy"/>.</summary>
    public GraphHyperEdge Copy() => new()
    {
        Relationship = Relationship,
        Endpoints = [.. Endpoints.Select(e => new HyperEndpoint { Node = e.Node, Role = e.Role, Ordinal = e.Ordinal, Confidence = e.Confidence })],
        Weight = Weight, Confidence = Confidence, ProvenanceFile = ProvenanceFile,
        Metadata = Metadata is null ? null : new Dictionary<string, string>(Metadata, Metadata.Comparer),
    };
}

/// <summary>One roled endpoint of a <see cref="GraphHyperEdge"/>.</summary>
public sealed class HyperEndpoint
{
    /// <summary>Target node id.</summary>
    public string Node { get; set; } = string.Empty;

    /// <summary>One of <see cref="EndpointRole"/>.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Position within an ordered role (argument / parameter index); null when unordered.</summary>
    public int? Ordinal { get; set; }

    /// <summary>Per-endpoint resolution confidence (the aggregate lives on the hyperedge); null when trivially certain.</summary>
    public double? Confidence { get; set; }
}

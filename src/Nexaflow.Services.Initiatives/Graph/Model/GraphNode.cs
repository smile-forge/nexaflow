namespace Nexaflow.Services.Initiatives.Graph.Model;

/// <summary>
/// One vertex of the knowledge graph — a product node, a code file, a declared type/member, or a synthesized
/// <c>external</c> stub for an unresolved reference. Serialized with <c>ProductJson.Options</c> (snake_case), so
/// the on-disk shape matches graphify's (<c>file_path</c>, <c>node_count</c>) for a clean downcast.
/// </summary>
public sealed class GraphNode
{
    /// <summary>Stable id: <c>product:&lt;id&gt;</c> | <c>file:&lt;relpath&gt;</c> | <c>code:&lt;relpath&gt;#&lt;astpath&gt;</c> | <c>external:&lt;name&gt;</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>One of <see cref="NodeType"/> (open vocabulary).</summary>
    public string Type { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>Repo-relative, forward-slash-normalized. Null for product/external nodes.</summary>
    public string? FilePath { get; set; }

    /// <summary>Tree-sitter grammar id (<c>c-sharp</c>, <c>typescript</c>, …). Null for product/external nodes.</summary>
    public string? Language { get; set; }

    /// <summary>Community id from detection (see <c>CommunityDetector</c>); null before detection runs.</summary>
    public int? Community { get; set; }

    /// <summary>1.0 when extracted; lower on the <see cref="GraphConfidence"/> ladder when inferred.</summary>
    public double Confidence { get; set; } = GraphConfidence.Extracted;

    /// <summary>Provenance — the repo-relative file that declared this node (for incremental prune/recreate);
    /// <c>"product"</c> for tree nodes, null for synthesized <c>external</c> nodes.</summary>
    public string? Source { get; set; }

    /// <summary>Open bag: <c>status</c>, <c>kind</c>, <c>line</c>, <c>ast</c>, <c>node_id</c>, …</summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

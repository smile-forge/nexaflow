using System.Collections.Generic;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Syntax;

namespace Nexaflow.Services.Initiatives.Graph;

/// <summary>
/// Per-file incremental build state, persisted to the gitignored <c>.product/graph-cache.json</c> sidecar. A code
/// file that hasn't changed (same content hash) never gets re-parsed — its cached <see cref="FileContribution"/>
/// (the tree-sitter-extracted nodes, structural edges, unresolved bases, and raw relation sites) is reused verbatim.
/// The <b>global</b> passes — inheritance/reference/hyperedge resolution and community detection — always re-run over
/// the assembled contributions, so a change in one file correctly re-points inferred edges asserted by an unchanged
/// one. <see cref="SchemaVersion"/> stamps the extractor: bump <see cref="GraphSchema.Version"/> and every cache is
/// discarded for a clean re-extract.
/// </summary>
public sealed class GraphCache
{
    public int SchemaVersion { get; set; } = GraphSchema.Version;

    /// <summary>Repo-relative path → its last extracted contribution (keyed for prune-on-delete).</summary>
    public Dictionary<string, FileContribution> Files { get; set; } = new(System.StringComparer.Ordinal);
}

/// <summary>One code file's cached extraction — everything the whole-repo code pass produces for it, before any
/// cross-file resolution. Reused as-is on a hash hit.</summary>
public sealed class FileContribution
{
    /// <summary>Content hash (MD5 of the decoded text) — the change-detection key.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>File + type + member nodes (and any import-target file nodes) this file declares.</summary>
    public List<GraphNode> Nodes { get; set; } = [];

    /// <summary>Structural edges local to this file: <c>contains</c> (file→type→member) + resolved <c>imports</c>.</summary>
    public List<GraphEdge> Edges { get; set; } = [];

    /// <summary>Unresolved base types (resolved to <c>extends</c>/<c>implements</c> against the global type index).</summary>
    public List<CachedBase> Bases { get; set; } = [];

    // Raw (unresolved) relation sites — resolved globally each build into inferred edges + hyperedges.
    public List<RawRef> Refs { get; set; } = [];
    public List<RawSignature> Signatures { get; set; } = [];
    public List<RawAttribute> Attributes { get; set; } = [];
    public List<RawCall> Calls { get; set; } = [];
    public List<RawFileRef> FileRefs { get; set; } = [];
}

/// <summary>An unresolved base of a type: the declaring type's node id + the bare base name + whether it looks like
/// an interface (the <c>I</c>-prefix convention). Resolved to a real type node or an <c>external:</c> node globally.</summary>
public sealed class CachedBase
{
    public string TypeId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsInterface { get; set; }
}

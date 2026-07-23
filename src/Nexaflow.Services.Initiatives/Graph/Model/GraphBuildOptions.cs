namespace Nexaflow.Services.Initiatives.Graph.Model;

/// <summary>How much of the repo the builder walks.</summary>
public enum GraphScope
{
    /// <summary>AST every code file in the repo (default).</summary>
    WholeRepo,
    /// <summary>Only snaplinked files + their 1-hop import/inheritance neighbours.</summary>
    ProductAnchored,
}

/// <summary>Community-detection algorithm. Louvain (+ component split) ships; Leiden is a documented v2.</summary>
public enum CommunityAlgorithm
{
    Louvain,
}

/// <summary>The current extractor/schema stamp. Bump when extraction changes so caches force a full re-extract.</summary>
public static class GraphSchema
{
    public const int Version = 2;   // v2: string-literal file mentions added to the extractor
}

/// <summary>Inputs to <c>GraphBuilder.Build</c>. Kept a value bag so <c>Build</c> is a pure function of its inputs.</summary>
public sealed class GraphBuildOptions
{
    public GraphScope Scope { get; set; } = GraphScope.WholeRepo;

    /// <summary>Reuse unchanged files' cached contributions (see <c>GraphCache</c>).</summary>
    public bool Incremental { get; set; } = true;

    /// <summary>Emit type→member <c>contains</c> nodes/edges. False → files+types only (coarser, cheaper at scale).</summary>
    public bool IncludeMembers { get; set; } = true;

    /// <summary>Whole-repo guard: stop after this many code files.</summary>
    public int MaxFiles { get; set; } = 20_000;

    /// <summary>
    /// Directory to read the <b>source</b> from when building the code/structured/asset layers. Null (default)
    /// reads from the product root — unchanged for a normal checkout and CI. Set it to a linked worktree's root
    /// so the graph reflects the branch you're editing (files not yet in the main checkout included). Repo-relative
    /// node ids are identical either way (same repo layout), so the content-addressed <c>GraphCache</c> re-parses
    /// only the files that actually differ, not the whole tree. The product tree, snaplinks and output location
    /// always stay on the product root.
    /// </summary>
    public string? CodeRoot { get; set; }

    /// <summary>ISO-8601 timestamp the caller stamps into <c>Metadata.GeneratedAt</c>; the builder never reads the clock.</summary>
    public string? GeneratedAt { get; set; }

    public CommunityAlgorithm CommunityAlgorithm { get; set; } = CommunityAlgorithm.Louvain;
}

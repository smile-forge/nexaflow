namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>
/// The derived <c>.product/test-coverage.json</c> — the current mapping of product node → the tests that
/// <em>declare</em> they cover it (via <c>[CoversNode]</c>), produced by reflecting the built test
/// assemblies. It is not truth about the tree (the tree stays authoritative); it is the cross-check the
/// Integrity page reconciles against to surface "a test declares this node but the tree has no link". Safe
/// to delete — regenerate with <c>nfi scan-tests</c>.
/// </summary>
public sealed class TestCoverageManifest
{
    /// <summary>ISO-8601 timestamp of the scan that produced this manifest.</summary>
    public string Generated { get; set; } = string.Empty;

    public int ScannedAssemblies { get; set; }

    /// <summary>node id → the tests declaring coverage of it (many tests may map to one node).</summary>
    public Dictionary<string, List<TestRef>> Coverage { get; set; } = [];

    /// <summary>Test classes that opted out of a declaration via <c>[NoCoverage]</c> — bookkeeping only.</summary>
    public List<NoCoverageRef> NoCoverage { get; set; } = [];

    /// <summary>
    /// The assemblies this scan read, as they were when it read them. This is what lets a reader tell a
    /// current manifest from one the build has moved past, rather than trusting whatever is on disk:
    /// without it the file carries a <see cref="Generated"/> timestamp and no way to know what that
    /// timestamp is still true of.
    /// </summary>
    public List<ScannedAssemblyRef> Assemblies { get; set; } = [];
}

/// <summary>
/// One assembly a scan read, identified by size and write time rather than by content — enough to answer
/// "is this still the file I reflected?" with a single stat, which is what keeps the check cheap enough to
/// run on every validate. A mismatch means rescan; it never means anything is broken.
/// </summary>
public sealed class ScannedAssemblyRef
{
    /// <summary>Repo-relative, forward-slash path of the assembly.</summary>
    public string Path { get; set; } = string.Empty;

    public long Size { get; set; }

    /// <summary>ISO-8601 UTC write time of the assembly at the moment it was scanned.</summary>
    public string WriteTimeUtc { get; set; } = string.Empty;
}

/// <summary>A test class explicitly declaring it maps to no product node, with the stated reason.</summary>
public sealed class NoCoverageRef
{
    public string Assembly { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

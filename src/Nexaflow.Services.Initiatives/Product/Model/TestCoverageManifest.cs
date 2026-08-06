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
}

/// <summary>A test class explicitly declaring it maps to no product node, with the stated reason.</summary>
public sealed class NoCoverageRef
{
    public string Assembly { get; set; } = string.Empty;
    public string Class { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Declares that this test (class or method) backs a product-tree node, identified by its id. This is the
/// committed, source-controlled record of test↔node coverage: <c>scan-tests</c> harvests it into the
/// coverage manifest, the Integrity page offers to turn it into a <c>tests</c>-concern snaplink, and the
/// coverage guard / analyzer reject a stale id. Repeatable (a class may cover several nodes) and valid on
/// a method for finer-grained mapping than its class.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class CoversNodeAttribute(string nodeId) : Attribute
{
    /// <summary>The product-tree node id this test backs (a key in <c>.product/tree.json</c>'s node map).</summary>
    public string NodeId { get; } = nodeId;
}

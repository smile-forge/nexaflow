namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>
/// The fully-loaded in-memory product: what <see cref="Services.ProductStore"/> reads from a
/// <c>.product/</c> folder. The tree is a flat map keyed by node id (reparenting is a single
/// parent/children edit).
/// </summary>
public sealed class ProductState
{
    public ProductDocument Product { get; set; } = new();
    public Dictionary<string, ProductNode> Nodes { get; set; } = [];

    /// <summary>
    /// A deep copy — nothing in it is shared with this instance. It is what a mutation is applied to: a verb
    /// edits the state it is handed and only the write decides whether to keep it, so handing out the live
    /// instance means a <em>refused</em> edit still stands in memory for the next command to persist.
    /// </summary>
    public ProductState Copy() => new()
    {
        Product = Product.Copy(),
        Nodes   = Nodes.ToDictionary(kv => kv.Key, kv => kv.Value.Copy(), StringComparer.Ordinal),
    };
}

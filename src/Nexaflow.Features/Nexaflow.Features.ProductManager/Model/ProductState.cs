namespace Nexaflow.Features.ProductManager.Model;

/// <summary>
/// The fully-loaded in-memory product: what <see cref="Services.ProductStore"/> reads from a
/// <c>.product/</c> folder. The tree is a flat map keyed by node id (reparenting is a single
/// parent/children edit).
/// </summary>
public sealed class ProductState
{
    public ProductDocument Product { get; set; } = new();
    public Dictionary<string, ProductNode> Nodes { get; set; } = [];
}

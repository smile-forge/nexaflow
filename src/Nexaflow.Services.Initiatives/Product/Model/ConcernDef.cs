namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>
/// A defined cross-cutting concern in the product vocabulary. <see cref="IsDefault"/> concerns are
/// auto-attached (as <c>should</c>) to every newly created node.
/// </summary>
public sealed class ConcernDef
{
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

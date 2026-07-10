namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>
/// A defined cross-cutting concern in the product vocabulary. <see cref="IsDefault"/> concerns are
/// auto-attached (as <c>should</c>) to every newly created node.
/// </summary>
public sealed class ConcernDef
{
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    /// <summary>
    /// When true, a node's link to this concern must carry at least one snaplink once the link reaches a
    /// terminal status (<c>done</c>/<c>faulted</c>) — an unbacked "done" is a validation failure. Lets a
    /// concern like <c>tests</c> demand proof (a link to the test) rather than an unverifiable claim.
    /// </summary>
    public bool RequiresSnaplink { get; set; }
}

namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>
/// A frozen, committed export of the product at a point in time — written to the export dir as
/// <c>&lt;version&gt;.json</c> and (optionally) tagged in git. Self-describing so it can be re-opened and
/// listed without the live <c>.product/</c> folder (which is gitignored). Read-only once taken.
/// </summary>
public sealed class ProductSnapshot
{
    public string Version { get; set; } = string.Empty;
    public string? Date { get; set; }
    public string? Tag { get; set; }
    public ProductDocument Product { get; set; } = new();
    public Dictionary<string, ProductNode> Nodes { get; set; } = [];
}

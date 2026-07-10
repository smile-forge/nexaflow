namespace Nexaflow.Services.Initiatives.Product.Model;

/// <summary>
/// <c>product.json</c> — the product name plus the reusable concern-tag vocabulary the survey checks
/// against. Concerns subsume artifacts ("tests"/"demo"/"docs" are just concerns); each node links to a
/// concern with a status. Adding a concern is a one-line data edit.
/// </summary>
public sealed class ProductDocument
{
    public string Product { get; set; } = string.Empty;

    /// <summary>The defined cross-cutting concern vocabulary; <c>is_default</c> ones auto-attach to new nodes.</summary>
    public List<ConcernDef> Concerns { get; set; } =
    [
        new() { Name = "accessibility" }, new() { Name = "i18n" }, new() { Name = "telemetry" },
        new() { Name = "theming" }, new() { Name = "tests", IsDefault = true, RequiresSnaplink = true },
        new() { Name = "demo" }, new() { Name = "docs" }
    ];

    /// <summary>Folder (relative to the product root) where committed snapshots + PRODUCT.md are written.</summary>
    public string ExportDir { get; set; } = "docs/product";
}

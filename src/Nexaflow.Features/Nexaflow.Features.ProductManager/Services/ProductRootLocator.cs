namespace Nexaflow.Features.ProductManager.Services;

/// <summary>
/// Resolves which folder a Product tab acts on. A product is simply the folder its <c>.product/</c>
/// directory sits in — there is no configured default — so the only source is the <c>path</c> page param
/// (supplied by the folder viewlet or the opener).
/// </summary>
public static class ProductRootLocator
{
    public static string? Resolve(Dictionary<string, string>? pageParams)
    {
        var path = pageParams?.GetValueOrDefault("path");
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}

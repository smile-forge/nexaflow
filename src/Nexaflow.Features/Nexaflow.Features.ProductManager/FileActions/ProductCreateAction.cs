using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.ProductManager.FileActions;

/// <summary>
/// "New → Product": makes the <em>current</em> folder a product by creating a gitignored
/// <c>.product/</c> (product.json + tree.json) in it, seeded with the typed <b>product name</b> (the
/// name names the product, not a new folder). The folder <em>is</em> the product; opening it gives the
/// Product Manager tab (via <c>ProductViewlet</c>). Discovered cross-assembly by the file-system feature
/// registry — no reference to WindowsFileSystem is needed (both contracts live in <c>Features.Common</c>).
/// </summary>
public sealed class ProductCreateAction : IFileCreateAction, ICacheable
{
    public IconRef Icon         => IconRef.Emoji("🧭");
    public string DisplayName   => Str.Get("ProductManager.Action.NewProduct");
    public string FileExtension => string.Empty;   // the typed text is the product name, not a filename

    public Task<string?> CreateItemAsync(string folderPath, string fileName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
            return Task.FromResult<string?>(null);
        if (ProductStore.Exists(folderPath))
            throw new InvalidOperationException(Str.Get("ProductManager.Action.AlreadyProduct"));

        new ProductStore(folderPath).Initialize(fileName.Trim());
        return Task.FromResult<string?>(ProductStore.DotProductDir(folderPath));
    }
}

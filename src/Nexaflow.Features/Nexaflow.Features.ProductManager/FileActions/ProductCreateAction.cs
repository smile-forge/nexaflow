using Nexaflow.Features.Common;
using Nexaflow.Services.Initiatives.Product.Services;

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
    public string Icon          => "🧭";
    public string DisplayName   => "Product";
    public string FileExtension => string.Empty;   // the typed text is the product name, not a filename

    public string? Create(string folderPath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || string.IsNullOrWhiteSpace(fileName))
            return null;
        if (ProductStore.Exists(folderPath))
            throw new InvalidOperationException("This folder is already a product (it has a .product folder).");

        new ProductStore(folderPath).Initialize(fileName.Trim());
        return ProductStore.DotProductDir(folderPath);
    }
}

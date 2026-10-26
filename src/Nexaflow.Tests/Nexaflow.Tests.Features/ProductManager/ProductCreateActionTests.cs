using System.IO;
using Nexaflow.Features.ProductManager.FileActions;
using Nexaflow.Services.Initiatives.Product.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

[TestClass]
[CoversNode("product-create-action")]
public class ProductCreateActionTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(TestTemp.Root, $"pmcreate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task Create_InitializesProductInCurrentFolder_NamedByInput()
    {
        var path = await new ProductCreateAction().CreateItemAsync(_root, "My App");

        Assert.AreEqual(ProductStore.DotProductDir(_root), path);  // .product/ in the current folder
        Assert.IsTrue(ProductStore.Exists(_root));
        Assert.AreEqual("My App", new ProductStore(_root).Load().Product.Product);  // input names the product
    }

    [TestMethod]
    public async Task Create_RejectsBlankName()
        => Assert.IsNull(await new ProductCreateAction().CreateItemAsync(_root, "   "));

    [TestMethod]
    public async Task Create_OnExistingProduct_Throws()
    {
        await new ProductCreateAction().CreateItemAsync(_root, "First");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => new ProductCreateAction().CreateItemAsync(_root, "Second"));
    }
}

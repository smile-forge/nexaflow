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
        _root = Path.Combine(Path.GetTempPath(), $"pmcreate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Teardown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public void Create_InitializesProductInCurrentFolder_NamedByInput()
    {
        var path = new ProductCreateAction().Create(_root, "My App");

        Assert.AreEqual(ProductStore.DotProductDir(_root), path);  // .product/ in the current folder
        Assert.IsTrue(ProductStore.Exists(_root));
        Assert.AreEqual("My App", new ProductStore(_root).Load().Product.Product);  // input names the product
    }

    [TestMethod]
    public void Create_RejectsBlankName()
        => Assert.IsNull(new ProductCreateAction().Create(_root, "   "));

    [TestMethod]
    public void Create_OnExistingProduct_Throws()
    {
        new ProductCreateAction().Create(_root, "First");
        Assert.ThrowsExactly<InvalidOperationException>(() => new ProductCreateAction().Create(_root, "Second"));
    }
}

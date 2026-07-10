using Nexaflow.Services.Initiatives.Product.Services;

namespace Nexaflow.Tests.Features.ProductManager;

[TestClass]
public class ProductRootLocatorTests
{
    [TestMethod]
    public void Resolve_ReturnsPathParam()
    {
        Assert.AreEqual(@"C:\repo",
            ProductRootLocator.Resolve(new Dictionary<string, string> { ["path"] = @"C:\repo" }));
    }

    [TestMethod]
    public void Resolve_NoParam_ReturnsNull()
    {
        Assert.IsNull(ProductRootLocator.Resolve(null));
        Assert.IsNull(ProductRootLocator.Resolve(new Dictionary<string, string>()));
    }

    [TestMethod]
    public void Resolve_BlankPath_ReturnsNull()
    {
        Assert.IsNull(ProductRootLocator.Resolve(new Dictionary<string, string> { ["path"] = "   " }));
    }
}

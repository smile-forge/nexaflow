using Nexaflow.Services.Initiatives.Product.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Product;

[TestClass]
[CoversNode("data-model")]
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

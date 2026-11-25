using System.IO;
using Nexaflow.Features.ProductManager.ViewModels;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Localization;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>The product viewlet over a folder: the product's name, how far along it is, and the way into it.</summary>
[TestClass]
[CoversNode("product-viewlet")]
public class ProductViewletViewModelTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup() => _root = TestTemp.CreateSubdirectory("pm-viewlet-").FullName;

    [TestMethod]
    public void AProduct_IsNamed_AndItsProgressSummed()
    {
        new ProductStore(_root).Initialize("Acme");
        var viewlet = new ProductViewletViewModel(new IntegrityShellStub(), _root);

        viewlet.Refresh();

        Assert.AreEqual(Str.Format("ProductManager.Viewlet.TitleFormat", "Acme"), viewlet.Title);
        Assert.AreNotEqual("…", viewlet.Summary);
        Assert.AreNotEqual(Str.Get("ProductManager.Viewlet.ReadFailed"), viewlet.Summary);
    }

    [TestMethod]
    public void AProductThatCannotBeRead_SaysSo()
    {
        var store = new ProductStore(_root);
        store.Initialize("Acme");
        File.WriteAllText(store.TreeFilePath, "{ this is not a tree");
        var viewlet = new ProductViewletViewModel(new IntegrityShellStub(), _root);

        viewlet.Refresh();

        Assert.AreEqual(Str.Get("ProductManager.Viewlet.ReadFailed"), viewlet.Summary);
    }

    [TestMethod]
    public void Opening_OpensTheProductOfThisFolder()
    {
        var shell = new IntegrityShellStub();

        new ProductViewletViewModel(shell, _root).OpenCommand.Execute(null);

        Assert.AreEqual(1, shell.Opened.Count);
        Assert.AreEqual("ProductManager", shell.Opened[0].Kind);
        Assert.AreEqual(_root, shell.Opened[0].Params?["path"]);
    }
}

using System.IO;
using System.Linq;
using Nexaflow.Features.ProductManager.ViewModels;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// The Product (Sunburst) tab's view-model, driven headlessly through <see cref="IntegrityShellStub"/>
/// (its WatchFile is a no-op and its background queue runs inline, so the VM builds fully in the ctor with
/// no dispatcher). Covers the surfaces that aren't the AI tools or a sub-overlay: the sunburst arc build,
/// the node-detail properties pane, and the Settings overlay's export-dir + concern vocabulary.
/// </summary>
[TestClass]
public class ProductViewModelTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pmvm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var store = new ProductStore(_root);
        store.Initialize("Demo");
        store.SaveProduct(new ProductDocument
        {
            Product = "Demo",
            Concerns = [new ConcernDef { Name = "tests", IsDefault = true }, new ConcernDef { Name = "a11y" }],
        });
        // Two top-level nodes so the arc's first ring has a known, asserted shape.
        store.SaveTree(new Dictionary<string, ProductNode>
        {
            ["a"] = new() { Title = "Alpha", Description = "the alpha node", Note = "why", Status = Status.Should },
            ["b"] = new() { Title = "Beta", Status = Status.Done },
        });
    }

    [TestCleanup]
    public void Teardown() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private ProductViewModel Open() =>
        new(new ProductStore(_root), new ProductGit(_root), _root, new IntegrityShellStub());

    [TestMethod]
    [CoversNode("sunburst-view")]
    public void Load_BuildsTheSunburstArcAndScopeCaptionFromTheTree()
    {
        var vm = Open();

        Assert.IsTrue(vm.HasNodes, "a non-empty tree should report HasNodes");
        Assert.IsNotNull(vm.SunburstRoot, "the arc should be built from the loaded tree");
        Assert.AreEqual("Demo", vm.SunburstRoot!.Label, "the centre is the product");
        Assert.AreEqual(2, vm.SunburstRoot.Children.Count, "the first ring is the two top-level nodes");
        StringAssert.Contains(vm.Caption, "done", "the scope caption summarises the status split");
    }

    [TestMethod]
    [CoversNode("product-detail-pane")]
    [CoversNode("product-properties")]
    public void SelectingANode_PopulatesTheDetailPane_AndTheRootClearsIt()
    {
        var vm = Open();

        vm.NavigateTo("a");
        Assert.IsTrue(vm.IsNodeSelected);
        Assert.IsFalse(vm.IsRootSelected);
        Assert.AreEqual("Alpha", vm.DetailTitle);
        Assert.AreEqual("the alpha node", vm.DetailDescription);
        Assert.AreEqual("why", vm.DetailNote);

        vm.NavigateTo(null);   // back to the product root — the pane shows the roll-up, not a node's properties
        Assert.IsTrue(vm.IsRootSelected);
        Assert.IsFalse(vm.IsNodeSelected);
        Assert.AreEqual(string.Empty, vm.DetailTitle);
    }

    [TestMethod]
    [CoversNode("product-settings")]
    public void Settings_ExposeTheConcernVocabulary_ThenPersistAnEdit()
    {
        var vm = Open();

        vm.ShowSettingsCommand.Execute(null);
        Assert.IsTrue(vm.SettingsVisible);
        CollectionAssert.AreEquivalent(
            new[] { "tests", "a11y" },
            vm.SettingsConcerns.Select(r => r.Name).ToArray(),
            "the overlay lists the product's current concern vocabulary");

        vm.NewSettingsConcern = "perf";
        vm.AddSettingsConcernCommand.Execute(null);
        CollectionAssert.Contains(vm.SettingsConcerns.Select(r => r.Name).ToArray(), "perf");

        vm.ConfirmSettingsCommand.Execute(null);
        Assert.IsFalse(vm.SettingsVisible);
        var saved = new ProductStore(_root).Load().Product.Concerns.Select(c => c.Name).ToArray();
        CollectionAssert.Contains(saved, "perf", "the new concern is persisted to product.json");
    }
}

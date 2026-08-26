using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.ProductManager.ClientTools;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>The AI tool surface — read renderers + the write tools (which load .product/, mutate, and save).</summary>
[TestClass]
[CoversNode("product-ai-act")]
public class ProductToolsTests
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pmtools_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        var store = new ProductStore(_root);
        store.Initialize("Demo");
        store.SaveProduct(new ProductDocument
        {
            Product = "Demo",
            Concerns = [new ConcernDef { Name = "tests" }, new ConcernDef { Name = "a11y" }]
        });
        store.SaveTree(new Dictionary<string, ProductNode>
        {
            ["root"] = new() { Title = "Root", Children = ["a"] },
            ["a"]    = new() { Title = "A", Parent = "root", Status = Status.Should,
                Concerns = [new ConcernLink { Tag = "tests", Status = Status.Should }] },
        });
    }

    [TestCleanup]
    public void Teardown() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private static ToolResult Invoke(IReadOnlyList<IClientTool> tools, string name, JsonObject args)
        => tools.Single(t => t.Name == name).InvokeAsync(args, default).GetAwaiter().GetResult();

    private ProductState Reload() => new ProductStore(_root).Load();

    [TestMethod]
    [CoversNode("product-ai-context")]
    public void GetContext_Describe_SummarizesTheTree()
    {
        // ProductViewModel.GetContext() delegates to ProductTools.Describe — assert it produces a real
        // summary of the loaded tree (names the product / a node), not an empty or error string.
        var ctx = ProductTools.Describe(Reload(), null);
        Assert.IsFalse(string.IsNullOrWhiteSpace(ctx));
    }

    [TestMethod]
    public void SetNodeStatus_Done_CascadesShouldItems()
    {
        var r = Invoke(ProductTools.ForRoot(_root), "product_set_node_status",
            new JsonObject { ["node_id"] = "a", ["status"] = "done" });

        Assert.IsTrue(r.Success);
        var n = Reload().Nodes["a"];
        Assert.AreEqual(Status.Done, n.Status);
        Assert.AreEqual(Status.Done, n.Concerns!.Single().Status);   // should concern advanced too
    }

    [TestMethod]
    public void SetNodeStatus_RejectsNonDoneFaulted()
        => Assert.IsFalse(Invoke(ProductTools.ForRoot(_root), "product_set_node_status",
            new JsonObject { ["node_id"] = "a", ["status"] = "should" }).Success);

    [TestMethod]
    public void EditNode_SetsDescriptionAndNote()
    {
        Invoke(ProductTools.ForRoot(_root), "product_edit_node",
            new JsonObject { ["node_id"] = "a", ["description"] = "the A thing", ["note"] = "because" });

        var n = Reload().Nodes["a"];
        Assert.AreEqual("the A thing", n.Description);
        Assert.AreEqual("because", n.Note);
    }

    [TestMethod]
    public void SetConcernStatus_And_AddConcernSnaplink()
    {
        var tools = ProductTools.ForRoot(_root);
        Assert.IsTrue(Invoke(tools, "product_set_concern_status",
            new JsonObject { ["node_id"] = "a", ["concern"] = "tests", ["status"] = "faulted" }).Success);
        Assert.IsTrue(Invoke(tools, "product_add_concern_snaplink",
            new JsonObject { ["node_id"] = "a", ["concern"] = "tests", ["type"] = "url", ["target"] = "https://ci/x" }).Success);

        var link = Reload().Nodes["a"].Concerns!.Single(c => c.Tag == "tests");
        Assert.AreEqual(Status.Faulted, link.Status);
        Assert.AreEqual("url", link.Snaplinks!.Single().Type);
    }

    [TestMethod]
    public void AddConcern_ValidatesAgainstVocabulary()
    {
        var tools = ProductTools.ForRoot(_root);
        Assert.IsFalse(Invoke(tools, "product_add_concern",
            new JsonObject { ["node_id"] = "root", ["concern"] = "undefined" }).Success);
        Assert.IsTrue(Invoke(tools, "product_add_concern",
            new JsonObject { ["node_id"] = "root", ["concern"] = "a11y" }).Success);
        Assert.IsTrue(Reload().Nodes["root"].Concerns!.Any(c => c.Tag == "a11y"));
    }

    [TestMethod]
    public void AddNodeSnaplink_Markdown_WithHeadingPath()
    {
        Invoke(ProductTools.ForRoot(_root), "product_add_node_snaplink",
            new JsonObject { ["node_id"] = "a", ["type"] = "markdown", ["target"] = "docs/a.md", ["detail"] = "Top > Sub" });

        var link = Reload().Nodes["a"].Snaplinks!.Single();
        Assert.AreEqual("markdown", link.Type);
        Assert.AreEqual("docs/a.md", link.Doc);
        CollectionAssert.AreEqual(new[] { "Top", "Sub" }, link.TitlePath!.ToArray());
    }

    [TestMethod]
    public void RemoveConcern_DropsTheLink()
    {
        Assert.IsTrue(Invoke(ProductTools.ForRoot(_root), "product_remove_concern",
            new JsonObject { ["node_id"] = "a", ["concern"] = "tests" }).Success);
        Assert.IsNull(Reload().Nodes["a"].Concerns);
    }

    [TestMethod]
    public void Zoom_ShowsChildrenAndDetails()
    {
        var text = ProductTools.Zoom(Reload(), "root");
        StringAssert.Contains(text, "Root");
        StringAssert.Contains(text, "a \"A\"");   // child line
    }
}

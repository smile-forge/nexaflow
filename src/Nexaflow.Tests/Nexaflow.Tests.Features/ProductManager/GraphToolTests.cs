using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.ProductManager.ClientTools;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// The knowledge-graph tools — the code-discovery half of the assistant's surface.
/// <para>
/// These answer what plain text search cannot. <c>graph_grep</c> is the clearest case: scoping a regex to the
/// neighbourhood of a node finds the callers that matter, where the same regex over the repo returns every
/// file that happens to share a word. And <c>graph_context</c> answers "what is this and what owns it" in one
/// call, which is the question you actually have before changing a piece of code.
/// </para>
/// </summary>
[TestClass]
[CoversNode("product-ai-act-graph")]
public class GraphToolTests
{
    private string _root = string.Empty;

    /// <summary>A small product plus a hand-built graph over two real source files, so the source-reading
    /// tools have something true to read rather than a fixture that only looks like code.</summary>
    [TestInitialize]
    public void CreateProductAndGraph()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexa-graphtools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "src"));

        File.WriteAllText(Path.Combine(_root, "src", "Widget.cs"), """
            namespace Demo;

            public class Widget
            {
                public void Spin()
                {
                    Wobble();
                }
            }
            """.Replace("\r\n", "\n"));

        File.WriteAllText(Path.Combine(_root, "src", "Gadget.cs"), """
            namespace Demo;

            public class Gadget
            {
                public void Use(Widget w) => w.Spin();
            }
            """.Replace("\r\n", "\n"));

        var store = new ProductStore(_root);
        store.Initialize("GraphDemo");
        store.SaveTree(new Dictionary<string, ProductNode>
        {
            ["root"] = new() { Title = "Root", Children = ["widget"] },
            ["widget"] = new() { Title = "Widget feature", Parent = "root", Children = [] },
        });

        var graph = new KnowledgeGraph
        {
            Nodes =
            [
                new GraphNode { Id = "product:widget", Type = NodeType.Product, Label = "Widget feature" },
                new GraphNode
                {
                    Id = "code:src/Widget.cs#T:Widget", Type = NodeType.Type, Label = "Widget",
                    FilePath = "src/Widget.cs",
                    Metadata = new Dictionary<string, string> { ["line"] = "3", ["kind"] = "class" },
                },
                new GraphNode
                {
                    Id = "code:src/Gadget.cs#T:Gadget", Type = NodeType.Type, Label = "Gadget",
                    FilePath = "src/Gadget.cs",
                    Metadata = new Dictionary<string, string> { ["line"] = "3", ["kind"] = "class" },
                },
            ],
            Edges =
            [
                new GraphEdge { Source = "product:widget", Target = "code:src/Widget.cs#T:Widget", Relationship = "implemented_by" },
                new GraphEdge { Source = "code:src/Gadget.cs#T:Gadget", Target = "code:src/Widget.cs#T:Widget", Relationship = "calls" },
            ],
        };
        store.SaveGraph(graph);
    }

    [TestCleanup]
    public void RemoveProduct() { try { Directory.Delete(_root, recursive: true); } catch { } }

    private Task<ToolResult> Run(string tool, JsonObject? args = null) =>
        ProductTools.ForRoot(_root).Single(t => t.Name == tool).InvokeAsync(args ?? [], CancellationToken.None);

    // ── The surface ───────────────────────────────────────────────────────────

    [TestMethod]
    public void EveryGraphCliVerbHasATool()
    {
        var names = ProductTools.ForRoot(_root).Select(t => t.Name).ToHashSet();

        foreach (var (verb, tool) in new[]
                 {
                     ("graph search", "graph_search"), ("graph context", "graph_context"),
                     ("graph node", "graph_node"), ("graph walk", "graph_walk"),
                     ("graph grep", "graph_grep"), ("graph code", "graph_code"),
                     ("graph stats", "graph_stats"), ("graph build", "graph_build"),
                 })
            Assert.IsTrue(names.Contains(tool), $"CLI verb '{verb}' has no '{tool}' tool");
    }

    [TestMethod]
    public void OnlyTheRebuildAsksFirst()
    {
        var tools = ProductTools.ForRoot(_root)
            .Where(t => t.Name.StartsWith("graph_")).ToDictionary(t => t.Name, t => t.Safety);

        foreach (var read in new[] { "graph_search", "graph_context", "graph_node",
                                     "graph_walk", "graph_grep", "graph_code", "graph_stats" })
            Assert.AreEqual(ToolSafety.SafeOperation, tools[read], $"{read} only reads the built graph");

        Assert.AreEqual(ToolSafety.RequiresApproval, tools["graph_build"],
                        "a rebuild walks the whole repo and writes graph.json - it asks");
    }

    // ── Search ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Search_FindsANodeAndGivesItsId()
    {
        var r = await Run("graph_search", new JsonObject { ["term"] = "Widget" });

        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "code:src/Widget.cs#T:Widget",
                              "the id is what every other graph call takes");
    }

    [TestMethod]
    public async Task Search_CanBeNarrowedToOneKindOfNode()
    {
        var all = await Run("graph_search", new JsonObject { ["term"] = "Widget" });
        var products = await Run("graph_search", new JsonObject { ["term"] = "Widget", ["type"] = "product" });

        StringAssert.Contains(all.ModelText, "code:src/Widget.cs#T:Widget");
        Assert.IsFalse(products.ModelText.Contains("code:src/Widget.cs#T:Widget"),
                       "restricting to product nodes drops the code ones");
        StringAssert.Contains(products.ModelText, "product:widget");
    }

    [TestMethod]
    public async Task Search_AMissMatchesNothing_AndSaysSo()
    {
        var r = await Run("graph_search", new JsonObject { ["term"] = "NoSuchThingAnywhere" });

        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "No graph nodes match");
    }

    // ── Context: the one-shot view ────────────────────────────────────────────

    [TestMethod]
    public async Task Context_AnswersWhatItIs_ItsSource_AndWhatOwnsIt()
    {
        var r = await Run("graph_context", new JsonObject { ["node_id"] = "code:src/Widget.cs#T:Widget" });

        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "src/Widget.cs", "where it lives");
        StringAssert.Contains(r.ModelText, "public void Spin", "its actual source, not just its name");
        StringAssert.Contains(r.ModelText, "calls", "who reaches it");
        StringAssert.Contains(r.ModelText, "owning feature(s)",
                              "the product feature that owns the code is the thing you want before changing it");
        StringAssert.Contains(r.ModelText, "Widget feature");
    }

    [TestMethod]
    public async Task Context_OfAnUnknownNodePointsBackAtSearch()
    {
        var r = await Run("graph_context", new JsonObject { ["node_id"] = "code:nope#T:Nope" });

        Assert.IsTrue(r.IsError);
        StringAssert.Contains(r.ModelText, "graph_search", "an error that names the way out of it");
    }

    // ── Relationships ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Node_ShowsEdgesInBothDirections()
    {
        var r = await Run("graph_node", new JsonObject { ["node_id"] = "code:src/Widget.cs#T:Widget" });

        Assert.IsFalse(r.IsError);
        // Widget is called BY Gadget and implements the product feature — incoming edges are the half a
        // plain text search can never give you.
        StringAssert.Contains(r.ModelText, "Gadget");
        StringAssert.Contains(r.ModelText, "Widget feature");
    }

    [TestMethod]
    public async Task Walk_GroupsTheNeighbourhoodByDistance()
    {
        var r = await Run("graph_walk", new JsonObject { ["node_id"] = "product:widget", ["hops"] = 2 });

        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "hop(s)");
        StringAssert.Contains(r.ModelText, "Gadget", "two hops out: product -> Widget -> Gadget");
    }

    // ── Grep: the thing plain search cannot do ────────────────────────────────

    [TestMethod]
    public async Task Grep_ScopedToANeighbourhood_FindsSourceNearTheStartingPoint()
    {
        var r = await Run("graph_grep", new JsonObject
        {
            ["pattern"] = "Spin", ["from"] = "product:widget", ["hops"] = 2,
        });

        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "Spin");
        StringAssert.Contains(r.ModelText, "src/", "each hit names the file and line it came from");
    }

    [TestMethod]
    public async Task Grep_AMalformedRegexIsAnAnswer_NotACrash()
    {
        var r = await Run("graph_grep", new JsonObject { ["pattern"] = "([unclosed" });

        Assert.IsFalse(r.IsError, "a bad pattern reports no matches rather than throwing at the model");
    }

    // ── Source ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Code_ReturnsTheBlock_NotTheWholeFile()
    {
        var r = await Run("graph_code", new JsonObject { ["node_id"] = "code:src/Widget.cs#T:Widget" });

        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "public class Widget");
        StringAssert.Contains(r.ModelText, "Spin");
        Assert.IsFalse(r.ModelText.Contains("namespace Demo"),
                       "the block starts at the type, not the top of the file");
    }

    [TestMethod]
    public async Task Code_OfANodeWithNoSourceSaysSo()
    {
        var r = await Run("graph_code", new JsonObject { ["node_id"] = "product:widget" });

        StringAssert.Contains(r.ModelText, "not a code node");
    }

    // ── Stats ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Stats_ReportsTheShapeOfTheGraph()
    {
        var r = await Run("graph_stats");

        Assert.IsFalse(r.IsError);
        StringAssert.Contains(r.ModelText, "node(s)");
        StringAssert.Contains(r.ModelText, "edge(s)");
    }

    // ── The missing-graph case ────────────────────────────────────────────────

    [TestMethod]
    public async Task WithNoGraphBuiltYet_TheToolsSayHowToGetOne()
    {
        var bare = Path.Combine(Path.GetTempPath(), "nexa-nograph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bare);
        new ProductStore(bare).Initialize("Bare");
        try
        {
            var r = await ProductTools.ForRoot(bare).Single(t => t.Name == "graph_search")
                                      .InvokeAsync(new JsonObject { ["term"] = "x" }, CancellationToken.None);

            Assert.IsTrue(r.IsError);
            StringAssert.Contains(r.ModelText, "graph_build",
                                  "\"no graph\" has to name the tool that makes one, or the model just retries");
        }
        finally { try { Directory.Delete(bare, recursive: true); } catch { } }
    }
}

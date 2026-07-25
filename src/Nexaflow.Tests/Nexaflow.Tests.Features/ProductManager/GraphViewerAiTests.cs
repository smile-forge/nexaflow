using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.ProductManager.Graph.ViewModels;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// Covers the GraphViewer AI surface: the read-only client tools (<c>read_graph</c> dumps the visible
/// neighbourhood — focus, summary, neighbours with their kind + relationship to the focus; <c>focus_node</c>
/// re-centres on a node by id or label substring), plus the file-scoped security context. Driven over a tiny
/// real <c>graph.json</c> so <see cref="GraphViewModel.LoadAsync"/> indexes and focuses exactly as in the app.
/// UI-marshalled focus mutation runs inline via <see cref="RunningShell"/>.
/// </summary>
[TestClass]
public class GraphViewerAiTests
{
    /// <summary>A shell whose RunOnUiAsync actually runs the action inline — the substitute's default returns
    /// a null Task and never invokes the delegate, which would no-op every UI-marshalled focus change.</summary>
    private static IShellServices RunningShell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>())
             .Returns(ci => { ci.Arg<Action>()(); return Task.CompletedTask; });
        return shell;
    }

    /// <summary>Writes a minimal graph.json — a product root that contains a code file (which contains a type)
    /// and a sub-product — using the same <see cref="ProductJson.Options"/> the real loader reads with.</summary>
    private static string WriteGraph(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), "nexagraph_ai_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var graph = new KnowledgeGraph
        {
            Nodes =
            [
                new GraphNode { Id = "product:root",       Type = NodeType.Product, Label = "Nexaflow" },
                new GraphNode { Id = "product:sub",        Type = NodeType.Product, Label = "Sub Feature" },
                new GraphNode { Id = "file:graph/A.cs",    Type = NodeType.File,    Label = "A.cs", FilePath = "graph/A.cs" },
                new GraphNode { Id = "code:graph/A.cs#Foo", Type = NodeType.Type,   Label = "Foo",  FilePath = "graph/A.cs" },
            ],
            Edges =
            [
                new GraphEdge { Source = "product:root", Target = "file:graph/A.cs",     Relationship = EdgeRelationship.Contains },
                new GraphEdge { Source = "product:root", Target = "product:sub",          Relationship = EdgeRelationship.Contains },
                new GraphEdge { Source = "file:graph/A.cs", Target = "code:graph/A.cs#Foo", Relationship = EdgeRelationship.Contains },
            ],
        };

        var path = Path.Combine(dir, "graph.json");
        File.WriteAllText(path, JsonSerializer.Serialize(graph, ProductJson.Options));
        return path;
    }

    [TestMethod]
    [CoversNode("product-ai-act")]
    [CoversNode("product-ai-context-graph")]
    public async Task AiSurface_ToolsContextAndScope()
    {
        var path = WriteGraph(out var dir);
        try
        {
            var vm = new GraphViewModel(path, shell: RunningShell());
            await vm.LoadAsync();

            Assert.IsTrue(vm.IsLoaded);
            Assert.IsFalse(vm.HasError, vm.ErrorMessage);

            // ── scope is the PRODUCT ROOT, not graph.json ──
            // The sunburst, the integrity page and this canvas are three views of one tree, so they share a
            // scope; pinning two of them must not read as two unrelated datasets.
            Assert.AreEqual(vm.ProductRoot, vm.GetSecurityContext());

            // ── context names the graph and the focused neighbourhood ──
            var ctx = vm.GetContext();
            StringAssert.Contains(ctx, vm.FileName);
            StringAssert.Contains(ctx, "Nexaflow");   // focus label

            // ── the graph gets the whole product surface, plus its own two view tools ──
            // Every Product view exposes the same commands because they act on the same tree; only tools that
            // act on what is *rendered* are view-specific.
            var tools = vm.GetClientTools();
            var names = tools.Select(t => t.Name).ToList();
            CollectionAssert.IsSubsetOf(new[] { "product_find", "product_query", "product_tree",
                                                "product_validate", "product_lint", "product_add_node" },
                                        names,
                                        "the graph view must offer the same product tools as the other views");
            CollectionAssert.Contains(names, "read_graph");
            CollectionAssert.Contains(names, "focus_node");

            // The view tools are auto-run reads/camera-moves — nothing commits to disk.
            foreach (var viewTool in new[] { "read_graph", "focus_node" })
                Assert.AreEqual(ToolSafety.SafeOperation, tools.Single(t => t.Name == viewTool).Safety);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    [CoversNode("product-ai-act-read-graph")]
    public async Task ReadGraph_ReportsFocusSummaryAndNeighbours()
    {
        var path = WriteGraph(out var dir);
        try
        {
            var vm = new GraphViewModel(path, shell: RunningShell());
            await vm.LoadAsync();
            Assert.AreEqual("Nexaflow", vm.FocusLabel, "load focuses the product root");

            var read = vm.GetClientTools().Single(t => t.Name == "read_graph");
            var r = await read.InvokeAsync(new JsonObject(), CancellationToken.None);

            Assert.IsFalse(r.IsError, r.ModelText);
            // focus + whole-graph summary
            StringAssert.Contains(r.ModelText, "Nexaflow");
            StringAssert.Contains(r.ModelText, "4 nodes");
            // neighbours with their kinds
            StringAssert.Contains(r.ModelText, "A.cs");
            StringAssert.Contains(r.ModelText, "Sub Feature");
            StringAssert.Contains(r.ModelText, "Foo");
            // relationship to the focus — direct edge named, deeper node by hop distance
            StringAssert.Contains(r.ModelText, "contains");
            StringAssert.Contains(r.ModelText, "2 hop");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    [CoversNode("product-ai-act-focus-node")]
    public async Task FocusNode_ReCentresOnMatchedNode()
    {
        var path = WriteGraph(out var dir);
        try
        {
            var vm = new GraphViewModel(path, shell: RunningShell());
            await vm.LoadAsync();
            Assert.AreEqual("Nexaflow", vm.FocusLabel);

            var focus = vm.GetClientTools().Single(t => t.Name == "focus_node");

            // ── match by label substring ──
            var byLabel = await focus.InvokeAsync(new JsonObject { ["node"] = "Foo" }, CancellationToken.None);
            Assert.IsFalse(byLabel.IsError, byLabel.ModelText);
            Assert.AreEqual("Foo", vm.FocusLabel, "focus_node re-centres the graph on the matched node");
            Assert.AreEqual("code:graph/A.cs#Foo", vm.FocusNodeId);
            StringAssert.Contains(byLabel.ModelText, "Foo");

            // ── match by exact id ──
            var byId = await focus.InvokeAsync(new JsonObject { ["node"] = "product:sub" }, CancellationToken.None);
            Assert.IsFalse(byId.IsError, byId.ModelText);
            Assert.AreEqual("Sub Feature", vm.FocusLabel);

            // ── no match is a recoverable error, not a throw ──
            var miss = await focus.InvokeAsync(new JsonObject { ["node"] = "does-not-exist" }, CancellationToken.None);
            Assert.IsTrue(miss.IsError);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

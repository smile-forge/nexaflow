using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Nexaflow.Features.ProductManager.Graph.Layout;
using Nexaflow.Features.ProductManager.Graph.Loaders;
using Nexaflow.Features.ProductManager.Graph.ViewModels;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Services.Initiatives.Graph.Store;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// The Graph viewer reads a graph archive into a <see cref="KnowledgeGraph"/> (same reader the builder
/// writes with) and the view-model builds bound node/edge collections + a deterministic layout. These headless
/// tests cover that load/build/layout contract; on-screen render + pan/zoom are covered by the UI smoke.
/// </summary>
[TestClass]
public class GraphViewerTests
{
    private static KnowledgeGraph Sample()
    {
        var g = new KnowledgeGraph();
        g.Nodes.Add(new GraphNode { Id = "product:root", Type = NodeType.Product, Label = "Root" });
        g.Nodes.Add(new GraphNode { Id = "file:a.cs", Type = NodeType.File, Label = "a.cs", FilePath = "a.cs", Language = "c-sharp" });
        g.Nodes.Add(new GraphNode { Id = "code:a.cs#T:A", Type = NodeType.Type, Label = "A" });
        g.Edges.Add(new GraphEdge { Source = "product:root", Target = "file:a.cs", Relationship = EdgeRelationship.References });
        g.Edges.Add(new GraphEdge { Source = "file:a.cs", Target = "code:a.cs#T:A", Relationship = EdgeRelationship.Contains });
        g.Metadata.NodeCount = 3;
        g.Metadata.EdgeCount = 2;
        return g;
    }

    private static string WriteTemp(KnowledgeGraph g)
    {
        var path = Path.Combine(Path.GetTempPath(), $"graph-{Guid.NewGuid():N}.bin");
        GraphArchive.Write(path, new GraphSnapshot { Graph = g });
        return path;
    }

    [TestMethod]
    [CoversNode("graph-load")]
    public void GraphLoader_RoundTrips_NodesEdgesAndTypes()
    {
        var path = WriteTemp(Sample());
        try
        {
            var g = new GraphLoader().Load(path);
            Assert.AreEqual(3, g.Nodes.Count);
            Assert.AreEqual(2, g.Edges.Count);
            Assert.AreEqual(NodeType.Product, g.Nodes[0].Type);
            Assert.AreEqual("c-sharp", g.Nodes[1].Language);
            Assert.AreEqual(EdgeRelationship.Contains, g.Edges[1].Relationship);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [CoversNode("graph-load")]
    public async Task ViewModel_Load_BuildsBoundCollections_WithFinitePositions()
    {
        var path = WriteTemp(Sample());
        try
        {
            var vm = new GraphViewModel(path);
            await vm.LoadAsync();

            Assert.IsTrue(vm.IsLoaded);
            Assert.IsFalse(vm.HasError, vm.ErrorMessage);
            Assert.AreEqual(3, vm.Nodes.Count);
            Assert.AreEqual(2, vm.Edges.Count);

            foreach (var n in vm.Nodes)
            {
                Assert.IsFalse(double.IsNaN(n.X) || double.IsNaN(n.Y), "layout assigns finite positions");
                Assert.AreEqual(n.X + n.Size / 2, n.CenterX, 1e-9, "centre tracks top-left + size");
            }
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [CoversNode("graph-load")]
    public async Task ViewModel_EmptyGraph_ReportsErrorButStillReady()
    {
        var path = WriteTemp(new KnowledgeGraph());
        try
        {
            var vm = new GraphViewModel(path);
            await vm.LoadAsync();

            Assert.IsTrue(vm.IsLoaded, "the AI send-gate must release even on an empty/failed load");
            Assert.IsTrue(vm.HasError);
            Assert.AreEqual(0, vm.Nodes.Count);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [CoversNode("graph-realise")]
    public async Task ViewModel_ShowsHopRadiusNeighbourhood_AndRecentersOnSelection()
    {
        // A chain: product:root — file:1 — file:2 — file:3
        var g = new KnowledgeGraph();
        g.Nodes.Add(new GraphNode { Id = "product:root", Type = NodeType.Product, Label = "Root" });
        for (var i = 1; i <= 3; i++)
            g.Nodes.Add(new GraphNode { Id = $"file:{i}.cs", Type = NodeType.File, Label = $"{i}.cs", FilePath = $"{i}.cs" });
        g.Edges.Add(new GraphEdge { Source = "product:root", Target = "file:1.cs", Relationship = EdgeRelationship.References });
        g.Edges.Add(new GraphEdge { Source = "file:1.cs", Target = "file:2.cs", Relationship = EdgeRelationship.Imports });
        g.Edges.Add(new GraphEdge { Source = "file:2.cs", Target = "file:3.cs", Relationship = EdgeRelationship.Imports });

        var path = WriteTemp(g);
        try
        {
            var vm = new GraphViewModel(path);
            await vm.LoadAsync();

            // Starts focused AND selected on the product root.
            Assert.AreEqual("product:root", vm.FocusNodeId);
            Assert.AreEqual("product:root", vm.SelectedNode?.Id);

            vm.HopRadius = 6;   // whole chain (independent of the default depth)
            Assert.AreEqual(4, vm.Nodes.Count);

            // Depth 1 from the root → only the root + its immediate neighbour.
            vm.HopRadius = 1;
            CollectionAssert.AreEquivalent(new[] { "product:root", "file:1.cs" }, vm.Nodes.Select(n => n.Id).ToArray());

            // Selecting a node re-centres the neighbourhood on it.
            vm.HopRadius = 6;
            vm.SelectedNode = vm.Nodes.First(n => n.Id == "file:2.cs");
            Assert.AreEqual("file:2.cs", vm.FocusNodeId);

            vm.HopRadius = 1;
            CollectionAssert.AreEquivalent(new[] { "file:1.cs", "file:2.cs", "file:3.cs" }, vm.Nodes.Select(n => n.Id).ToArray());
            Assert.IsFalse(vm.Nodes.Any(n => n.Id == "product:root"), "the root is 2 hops from file:2 → excluded at depth 1");
        }
        finally { File.Delete(path); }
    }

    // A root with two peers in community 0 and two in community 1 — used for the Segments rail.
    private static KnowledgeGraph TwoCommunityStar()
    {
        var g = new KnowledgeGraph();
        g.Nodes.Add(new GraphNode { Id = "product:root", Type = NodeType.Product, Label = "Root", Community = 0 });
        g.Nodes.Add(new GraphNode { Id = "file:a.cs", Type = NodeType.File, Label = "a.cs", FilePath = "a.cs", Community = 0 });
        g.Nodes.Add(new GraphNode { Id = "file:b.cs", Type = NodeType.File, Label = "b.cs", FilePath = "b.cs", Community = 1 });
        g.Nodes.Add(new GraphNode { Id = "file:c.cs", Type = NodeType.File, Label = "c.cs", FilePath = "c.cs", Community = 1 });
        foreach (var t in new[] { "file:a.cs", "file:b.cs", "file:c.cs" })
            g.Edges.Add(new GraphEdge { Source = "product:root", Target = t, Relationship = EdgeRelationship.References });
        return g;
    }

    [TestMethod]
    [CoversNode("graph-segments")]
    public async Task ViewModel_SegmentsRail_ListsCommunities_AndTogglesTheirVisibility()
    {
        var path = WriteTemp(TwoCommunityStar());
        try
        {
            var vm = new GraphViewModel(path);
            await vm.LoadAsync();
            vm.HopRadius = 3;   // whole star in view

            // The rail lists both communities present in the neighbourhood.
            Assert.IsTrue(vm.HasCommunities);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, vm.Communities.Select(c => c.Id).ToArray());
            Assert.AreEqual(4, vm.Nodes.Count);

            // Hide community 1 → both its nodes drop out; community 0 stays.
            vm.Communities.First(c => c.Id == 1).IsVisible = false;
            CollectionAssert.AreEquivalent(new[] { "product:root", "file:a.cs" }, vm.Nodes.Select(n => n.Id).ToArray());
            Assert.IsFalse(vm.Edges.Any(e => e.To.Id is "file:b.cs" or "file:c.cs"), "edges into a hidden community are dropped too");

            // Show-all restores everything and re-checks every row.
            vm.ShowAllCommunitiesCommand.Execute(null);
            Assert.AreEqual(4, vm.Nodes.Count);
            Assert.IsTrue(vm.Communities.All(c => c.IsVisible));
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [CoversNode("graph-segments")]
    public async Task ViewModel_SegmentsRail_NeverHidesTheFocusNode()
    {
        var path = WriteTemp(TwoCommunityStar());
        try
        {
            var vm = new GraphViewModel(path);
            await vm.LoadAsync();
            vm.HopRadius = 3;

            // Hiding the focus's OWN community still keeps the focus on screen — only its peers drop.
            vm.Communities.First(c => c.Id == 0).IsVisible = false;
            Assert.IsTrue(vm.Nodes.Any(n => n.Id == "product:root"), "the focus node is never hidden by a community toggle");
            Assert.IsFalse(vm.Nodes.Any(n => n.Id == "file:a.cs"), "its community-0 peer is hidden");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [CoversNode("graph-hyperedges")]
    public async Task ViewModel_RealisesHyperEdges_WhenEndpointsVisible_AndTogglesThem()
    {
        // root — file:a.cs — code:A — code:A/M:build ; a signature hyperedge joins build to the return type W.
        var g = new KnowledgeGraph();
        g.Nodes.Add(new GraphNode { Id = "product:root", Type = NodeType.Product, Label = "Root" });
        g.Nodes.Add(new GraphNode { Id = "file:a.cs", Type = NodeType.File, Label = "a.cs", FilePath = "a.cs" });
        g.Nodes.Add(new GraphNode { Id = "code:a.cs#T:A", Type = NodeType.Type, Label = "A" });
        g.Nodes.Add(new GraphNode { Id = "code:a.cs#T:A/M:build", Type = NodeType.Member, Label = "build" });
        g.Nodes.Add(new GraphNode { Id = "code:a.cs#T:W", Type = NodeType.Type, Label = "W" });
        g.Edges.Add(new GraphEdge { Source = "product:root", Target = "file:a.cs", Relationship = EdgeRelationship.References });
        g.Edges.Add(new GraphEdge { Source = "file:a.cs", Target = "code:a.cs#T:A", Relationship = EdgeRelationship.Contains });
        g.Edges.Add(new GraphEdge { Source = "code:a.cs#T:A", Target = "code:a.cs#T:A/M:build", Relationship = EdgeRelationship.Contains });
        g.HyperEdges.Add(new GraphHyperEdge
        {
            Relationship = HyperRelationship.Signature,
            Endpoints =
            [
                new HyperEndpoint { Node = "code:a.cs#T:A/M:build", Role = EndpointRole.Member },
                new HyperEndpoint { Node = "code:a.cs#T:W", Role = EndpointRole.Return },
            ],
        });

        var path = WriteTemp(g);
        try
        {
            var vm = new GraphViewModel(path);
            await vm.LoadAsync();
            Assert.IsTrue(vm.HasHyperEdges);
            Assert.IsTrue(vm.ShowSegmentsRail, "the rail shows for the hyperedge toggle even with no communities");

            vm.HopRadius = 6;   // build + W both reachable (W only via the hyperedge)
            Assert.AreEqual(1, vm.HyperEdges.Count);
            Assert.AreEqual(HyperRelationship.Signature, vm.HyperEdges[0].Relationship);
            Assert.AreEqual(2, vm.HyperEdges[0].Spokes.Count);

            // Toggle off → no hyperedges drawn; back on → restored.
            vm.ShowHyperEdges = false;
            Assert.AreEqual(0, vm.HyperEdges.Count);
            vm.ShowHyperEdges = true;
            Assert.AreEqual(1, vm.HyperEdges.Count);

            // Out of neighbourhood → not drawn (only root + file:a.cs visible at depth 1).
            vm.HopRadius = 1;
            Assert.AreEqual(0, vm.HyperEdges.Count, "a hyperedge with an out-of-range endpoint isn't drawn");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [CoversNode("graph-lod")]
    public async Task ViewModel_ZoomLod_HidesFinerKinds_WhenZoomedOut()
    {
        // root — file:a.cs — type A — member go
        var g = new KnowledgeGraph();
        g.Nodes.Add(new GraphNode { Id = "product:root", Type = NodeType.Product, Label = "Root" });
        g.Nodes.Add(new GraphNode { Id = "file:a.cs", Type = NodeType.File, Label = "a.cs", FilePath = "a.cs" });
        g.Nodes.Add(new GraphNode { Id = "code:a.cs#T:A", Type = NodeType.Type, Label = "A" });
        g.Nodes.Add(new GraphNode { Id = "code:a.cs#T:A/M:go", Type = NodeType.Member, Label = "go" });
        g.Edges.Add(new GraphEdge { Source = "product:root", Target = "file:a.cs", Relationship = EdgeRelationship.References });
        g.Edges.Add(new GraphEdge { Source = "file:a.cs", Target = "code:a.cs#T:A", Relationship = EdgeRelationship.Contains });
        g.Edges.Add(new GraphEdge { Source = "code:a.cs#T:A", Target = "code:a.cs#T:A/M:go", Relationship = EdgeRelationship.Contains });

        var path = WriteTemp(g);
        try
        {
            var vm = new GraphViewModel(path);
            await vm.LoadAsync();
            vm.HopRadius = 6;
            GraphNodeViewModel Node(string id) => vm.Nodes.First(n => n.Id == id);

            vm.ApplyLod(1.0);   // zoomed in → everything shows
            Assert.IsTrue(vm.Nodes.All(n => n.LodVisible));

            vm.ApplyLod(0.2);   // zoomed out → members + types drop, files + product stay
            Assert.IsTrue(Node("product:root").LodVisible);
            Assert.IsTrue(Node("file:a.cs").LodVisible);
            Assert.IsFalse(Node("code:a.cs#T:A").LodVisible, "types hide below 0.28");
            Assert.IsFalse(Node("code:a.cs#T:A/M:go").LodVisible, "members hide below 0.55");

            // An edge is hidden when either endpoint is LOD-hidden; kept when both survive.
            Assert.IsFalse(vm.Edges.First(e => e.To.Id == "code:a.cs#T:A/M:go").LodVisible);
            Assert.IsTrue(vm.Edges.First(e => e.To.Id == "file:a.cs").LodVisible);
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [CoversNode("graph-cap")]
    public async Task ViewModel_CapsRealizedNodes_KeepsFocus_AndReportsTheRest()
    {
        // A star far larger than the realized-node cap (700): root + 900 files, all one hop out.
        var g = new KnowledgeGraph();
        g.Nodes.Add(new GraphNode { Id = "product:root", Type = NodeType.Product, Label = "Root" });
        for (var i = 0; i < 900; i++)
        {
            g.Nodes.Add(new GraphNode { Id = $"file:{i}.cs", Type = NodeType.File, Label = $"{i}.cs", FilePath = $"{i}.cs" });
            g.Edges.Add(new GraphEdge { Source = "product:root", Target = $"file:{i}.cs", Relationship = EdgeRelationship.References });
        }

        var path = WriteTemp(g);
        try
        {
            var vm = new GraphViewModel(path);
            await vm.LoadAsync();
            vm.HopRadius = 1;   // root + 900 files = 901 in range, capped to 700

            Assert.AreEqual(700, vm.Nodes.Count, "the realized set is capped");
            Assert.IsTrue(vm.Nodes.Any(n => n.Id == "product:root"), "the focus is never capped out");
            Assert.AreEqual(201, vm.HiddenByCap, "the overflow is counted (901 - 700)");
            Assert.IsFalse(string.IsNullOrEmpty(vm.CapNote), "and surfaced, not silently dropped");
        }
        finally { File.Delete(path); }
    }

    [TestMethod]
    [CoversNode("graph-layout")]
    public void HybridLayout_IsDeterministic_PinsFocus_AndFinite()
    {
        var nodes = new List<LayoutNode>
        {
            new("product:root", true, 0, 0),   // the focus, pinned at the origin
            new("product:a", true),
            new("file:x.cs", false),
            new("code:x.cs#T:X", false),
        };
        var edges = new List<(string, string)>
        {
            ("product:root", "product:a"),
            ("product:a", "file:x.cs"),
            ("file:x.cs", "code:x.cs#T:X"),
        };
        var hop = new Dictionary<string, int>
        {
            ["product:root"] = 0, ["product:a"] = 1, ["file:x.cs"] = 2, ["code:x.cs#T:X"] = 3,
        };

        var a = HybridLayout.Compute(nodes, edges, hop, default);
        var b = HybridLayout.Compute(nodes, edges, hop, default);

        Assert.AreEqual(a["file:x.cs"], b["file:x.cs"], "same inputs → identical layout (deterministic)");
        Assert.AreEqual((0.0, 0.0), a["product:root"], "the pinned focus stays at the origin");
        foreach (var (_, p) in a)
            Assert.IsFalse(double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y),
                "positions are finite");
    }

    [TestMethod]
    [CoversNode("graph-layout")]
    public void Quadtree_HandlesManyCoincidentPoints_WithoutStackOverflow()
    {
        // Coincident bodies used to subdivide forever (stack overflow). The min-cell guard keeps them as a bucket.
        var tree = new Quadtree(0, 0, 4096);
        for (var i = 0; i < 200; i++) tree.Insert(12.5, -7.25);

        double fx = 0, fy = 0;
        tree.Repulsion(500, 500, 140 * 140, 0.81, ref fx, ref fy);
        Assert.IsFalse(double.IsNaN(fx) || double.IsNaN(fy) || double.IsInfinity(fx) || double.IsInfinity(fy));
    }

    [TestMethod]
    [CoversNode("graph-layout")]
    public void HybridLayout_DenseSameHopRing_CompletesWithFinitePositions()
    {
        // 500 nodes all one hop from the root — a dense ring that stresses the quadtree with clustered seeds.
        var nodes = new List<LayoutNode> { new("product:root", true, 0, 0) };
        var edges = new List<(string, string)>();
        var hop = new Dictionary<string, int> { ["product:root"] = 0 };
        for (var i = 0; i < 500; i++)
        {
            var id = $"file:{i}.cs";
            nodes.Add(new LayoutNode(id, false));
            hop[id] = 1;
            edges.Add(("product:root", id));
        }

        var pos = HybridLayout.Compute(nodes, edges, hop, default);
        Assert.AreEqual(501, pos.Count);
        foreach (var (_, p) in pos)
            Assert.IsFalse(double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y));
    }
}

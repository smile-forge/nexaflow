using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Communities;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// The structural graph build: product containment + snaplink links into code + file→type→member containment +
/// inheritance, all extracted (confidence 1.0). Runs against a throwaway repo root with one real C# file so the
/// tree-sitter outline is exercised end-to-end. Determinism is part of the contract (byte-identical output for a
/// fixed timestamp), so the CLI/incremental cache diff cleanly.
/// </summary>
[TestClass]
public class GraphBuilderTests
{
    private static (string Root, ProductState State) Setup()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexgraph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Sample.cs"),
            "namespace Demo;\npublic class Widget : IThing\n{\n    public void Run() { }\n}\npublic interface IThing { }\n");

        var state = new ProductState
        {
            Product = new ProductDocument { Product = "Demo" },
            Nodes = new Dictionary<string, ProductNode>
            {
                ["root"] = new ProductNode { Title = "Root", Children = { "widget" } },
                ["widget"] = new ProductNode
                {
                    Title = "Widget",
                    Parent = "root",
                    Snaplinks = new List<Snaplink>
                    {
                        new() { Type = "code", Doc = "Sample.cs", Class = "Widget", Method = "Run" },
                    },
                },
            },
        };
        return (root, state);
    }

    private static KnowledgeGraph Build(string root, ProductState state) =>
        GraphBuilder.Build(state, root, new GraphBuildOptions { GeneratedAt = "T" });

    [TestMethod]
    [CoversNode("graphviewer")]
    public void Build_ProductContainment_SnaplinkResolution_CodeLayer_AndInheritance()
    {
        var (root, state) = Setup();
        try
        {
            var g = Build(root, state);

            // Product layer + containment.
            Assert.IsTrue(g.Nodes.Any(n => n.Id == "product:root" && n.Type == NodeType.Product));
            Assert.IsTrue(g.Edges.Any(e => e is { Source: "product:root", Target: "product:widget", Relationship: EdgeRelationship.Contains }));

            // Snaplink with Class+Method resolves to the specific member node.
            const string method = "code:Sample.cs#T:Widget/M:Run";
            Assert.IsTrue(g.Nodes.Any(n => n.Id == method && n.Type == NodeType.Member), "snaplink resolves to the method node");
            Assert.IsTrue(g.Edges.Any(e => e.Source == "product:widget" && e.Target == method), "product node links to the resolved code node");

            // Code layer: file → type → member containment.
            Assert.IsTrue(g.Nodes.Any(n => n.Id == "file:Sample.cs" && n.Type == NodeType.File));
            Assert.IsTrue(g.Edges.Any(e => e is { Source: "file:Sample.cs", Target: "code:Sample.cs#T:Widget", Relationship: EdgeRelationship.Contains }));

            // Inheritance: Widget implements IThing (interface declared in the same file → resolved, not external).
            Assert.IsTrue(g.Edges.Any(e => e.Source == "code:Sample.cs#T:Widget" && e.Relationship == EdgeRelationship.Implements),
                "Widget implements IThing");

            // Every node/edge carries provenance for incremental prune.
            Assert.IsTrue(g.Nodes.Single(n => n.Id == "code:Sample.cs#T:Widget").Source == "Sample.cs");
            Assert.AreEqual("whole_repo", g.Metadata.Scope);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [CoversNode("graphviewer")]
    public void Build_IsByteDeterministic_ForFixedTimestamp()
    {
        var (root, state) = Setup();
        try
        {
            var a = JsonSerializer.Serialize(Build(root, state), ProductJson.Options);
            var b = JsonSerializer.Serialize(Build(root, state), ProductJson.Options);
            Assert.AreEqual(a, b, "same inputs + fixed timestamp → byte-identical graph");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [CoversNode("graphviewer")]
    public void Build_InfersCalls_AndInstantiations_WithConfidence()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexgraph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Calls.cs"),
            "namespace Demo;\npublic class Service\n{\n    public void Start() { var w = new Widget(); w.Run(); Help(); }\n" +
            "    public void Help() { }\n}\npublic class Widget { public void Run() { } }\n");

        var state = new ProductState
        {
            Product = new ProductDocument { Product = "Demo" },
            Nodes = new Dictionary<string, ProductNode> { ["root"] = new ProductNode { Title = "Root" } },
        };

        try
        {
            var g = GraphBuilder.Build(state, root, new GraphBuildOptions { GeneratedAt = "T" });
            const string start = "code:Calls.cs#T:Service/M:Start";

            Assert.IsTrue(g.Edges.Any(e => e.Source == start && e.Relationship == EdgeRelationship.Calls
                && e.Target == "code:Calls.cs#T:Widget/M:Run"), "Start() calls Widget.Run");
            Assert.IsTrue(g.Edges.Any(e => e.Source == start && e.Relationship == EdgeRelationship.Calls
                && e.Target == "code:Calls.cs#T:Service/M:Help"), "Start() calls Help");

            // `new Widget()` → instantiates the Widget type (verifies object_creation_expression matches this grammar).
            var inst = g.Edges.FirstOrDefault(e => e.Source == start && e.Relationship == EdgeRelationship.Instantiates
                && e.Target == "code:Calls.cs#T:Widget");
            Assert.IsNotNull(inst, "Start() instantiates Widget");
            Assert.AreEqual(GraphConfidence.NearCertain, inst!.Confidence, "same-file resolution is near-certain");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [CoversNode("graphviewer")]
    public void Build_ExtractsHyperEdges_SignatureAnnotatedAndCalls()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexgraph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Hyper.cs"),
            "namespace Demo;\npublic class Service\n{\n    [Track]\n" +
            "    public Widget Build(Widget input) { Consume(new Widget()); return new Widget(); }\n" +
            "    public void Consume(Widget w) { }\n}\n" +
            "public class Widget { }\npublic class TrackAttribute : System.Attribute { }\n");

        var state = new ProductState
        {
            Product = new ProductDocument { Product = "Demo" },
            Nodes = new Dictionary<string, ProductNode> { ["root"] = new ProductNode { Title = "Root" } },
        };

        try
        {
            var g = GraphBuilder.Build(state, root, new GraphBuildOptions { GeneratedAt = "T" });
            const string build = "code:Hyper.cs#T:Service/M:Build";
            const string widget = "code:Hyper.cs#T:Widget";

            // signature(Build) → return Widget + param Widget (void return on Consume is skipped).
            var sig = g.HyperEdges.FirstOrDefault(h => h.Relationship == HyperRelationship.Signature
                && h.Endpoints.Any(e => e is { Role: EndpointRole.Member, Node: build }));
            Assert.IsNotNull(sig, "Build has a signature hyperedge");
            Assert.IsTrue(sig!.Endpoints.Any(e => e is { Role: EndpointRole.Return, Node: widget }), "return type resolves to Widget");
            Assert.IsTrue(sig.Endpoints.Any(e => e is { Role: EndpointRole.Param, Node: widget }), "parameter type resolves to Widget");

            // annotated(Build) → TrackAttribute (C# drops the `Attribute` suffix at the use site).
            var ann = g.HyperEdges.FirstOrDefault(h => h.Relationship == HyperRelationship.Annotated
                && h.Endpoints.Any(e => e is { Role: EndpointRole.Target, Node: build }));
            Assert.IsNotNull(ann, "Build carries an annotated hyperedge");
            Assert.IsTrue(ann!.Endpoints.Any(e => e is { Role: EndpointRole.Attr, Node: "code:Hyper.cs#T:TrackAttribute" }),
                "attribute resolves to TrackAttribute via the +Attribute suffix");

            // calls(Build → Consume, arg new Widget()).
            var call = g.HyperEdges.FirstOrDefault(h => h.Relationship == HyperRelationship.Calls
                && h.Endpoints.Any(e => e is { Role: EndpointRole.Caller, Node: build }));
            Assert.IsNotNull(call, "Build's call passing a `new Widget()` is a calls hyperedge");
            Assert.IsTrue(call!.Endpoints.Any(e => e is { Role: EndpointRole.Callee, Node: "code:Hyper.cs#T:Service/M:Consume" }));
            Assert.IsTrue(call.Endpoints.Any(e => e is { Role: EndpointRole.Arg, Node: widget }), "the new Widget() argument is an arg endpoint");

            Assert.AreEqual(g.HyperEdges.Count, g.Metadata.HyperEdgeCount);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [CoversNode("graphviewer")]
    public void Build_LinksXamlToCodeBehind_AndCsprojDependencies()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexgraph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // A WPF view + its code-behind, and two projects with a reference + a package.
        File.WriteAllText(Path.Combine(root, "Widget.xaml"),
            "<UserControl x:Class=\"Demo.Widget\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" />\n");
        File.WriteAllText(Path.Combine(root, "Widget.xaml.cs"), "namespace Demo;\npublic partial class Widget { }\n");
        File.WriteAllText(Path.Combine(root, "A.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n" +
            "    <ProjectReference Include=\"B.csproj\" />\n    <PackageReference Include=\"SomePackage\" Version=\"1.0.0\" />\n" +
            "  </ItemGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(root, "B.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");

        var state = new ProductState
        {
            Product = new ProductDocument { Product = "Demo" },
            Nodes = new Dictionary<string, ProductNode> { ["root"] = new ProductNode { Title = "Root" } },
        };

        try
        {
            var g = GraphBuilder.Build(state, root, new GraphBuildOptions { GeneratedAt = "T" });

            // XAML view → its code-behind class (x:Class), high-certainty.
            Assert.IsTrue(g.Nodes.Any(n => n.Id == "file:Widget.xaml"), "the .xaml is a node");
            Assert.IsTrue(g.Edges.Any(e => e is { Source: "file:Widget.xaml", Target: "code:Widget.xaml.cs#T:Widget", Relationship: EdgeRelationship.ViewOf }),
                "view_of links the .xaml to its code-behind type");

            // csproj → project + package dependencies.
            Assert.IsTrue(g.Edges.Any(e => e is { Source: "file:A.csproj", Target: "file:B.csproj", Relationship: EdgeRelationship.DependsOn }),
                "depends_on links A.csproj → B.csproj");
            Assert.IsTrue(g.Edges.Any(e => e is { Source: "file:A.csproj", Target: "external:SomePackage", Relationship: EdgeRelationship.DependsOn }),
                "depends_on links A.csproj → the NuGet package");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [CoversNode("graphviewer")]
    public void Build_LinksStringLiteralFileMentions_UniqueOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexgraph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        Directory.CreateDirectory(Path.Combine(root, "a"));
        Directory.CreateDirectory(Path.Combine(root, "b"));
        File.WriteAllText(Path.Combine(root, "Loader.cs"),
            "namespace Demo;\npublic class Loader\n{\n" +
            "    public void Load() { Use(\"assets/brand.png\"); Use(\"dup.txt\"); }\n    void Use(string s) { }\n}\n");
        File.WriteAllBytes(Path.Combine(root, "assets", "brand.png"), new byte[] { 1, 2, 3 });   // an unparseable asset
        File.WriteAllText(Path.Combine(root, "a", "dup.txt"), "x");   // two files share a name → the mention is ambiguous
        File.WriteAllText(Path.Combine(root, "b", "dup.txt"), "y");

        var state = new ProductState
        {
            Product = new ProductDocument { Product = "Demo" },
            Nodes = new Dictionary<string, ProductNode> { ["root"] = new ProductNode { Title = "Root" } },
        };

        try
        {
            var g = GraphBuilder.Build(state, root, new GraphBuildOptions { GeneratedAt = "T" });
            const string load = "code:Loader.cs#T:Loader/M:Load";

            Assert.IsTrue(g.Nodes.Any(n => n.Id == "file:assets/brand.png"), "the unparseable asset gets a bare file node");
            Assert.IsTrue(g.Edges.Any(e => e is { Source: load, Target: "file:assets/brand.png", Relationship: EdgeRelationship.Mentions }),
                "a uniquely-resolving file literal becomes a mentions edge");
            Assert.IsFalse(g.Edges.Any(e => e.Source == load && e.Relationship == EdgeRelationship.Mentions && e.Target.EndsWith("dup.txt")),
                "an ambiguous filename (two dup.txt) is dropped, never guessed");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [CoversNode("graphviewer")]
    public void CommunityDetector_SeparatesTwoClusters_Deterministically()
    {
        var g = new KnowledgeGraph();
        foreach (var id in new[] { "a1", "a2", "a3", "b1", "b2", "b3" })
            g.Nodes.Add(new GraphNode { Id = id, Type = NodeType.Type, Label = id });
        void E(string s, string t) => g.Edges.Add(new GraphEdge { Source = s, Target = t, Relationship = EdgeRelationship.Calls });
        E("a1", "a2"); E("a2", "a3"); E("a3", "a1");   // triangle A
        E("b1", "b2"); E("b2", "b3"); E("b3", "b1");   // triangle B
        E("a1", "b1");                                  // one weak bridge

        var comm = CommunityDetector.Detect(g);

        Assert.AreEqual(comm["a1"], comm["a2"]);
        Assert.AreEqual(comm["a2"], comm["a3"]);
        Assert.AreEqual(comm["b1"], comm["b2"]);
        Assert.AreEqual(comm["b2"], comm["b3"]);
        Assert.AreNotEqual(comm["a1"], comm["b1"], "the two triangles are distinct communities");

        var again = CommunityDetector.Detect(g);
        CollectionAssert.AreEqual(
            comm.OrderBy(k => k.Key).Select(k => k.Value).ToArray(),
            again.OrderBy(k => k.Key).Select(k => k.Value).ToArray(),
            "detection is deterministic");
    }
}

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

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// The structural graph build: product containment + snaplink links into code + file→type→member containment +
/// inheritance, all extracted (confidence 1.0). Runs against a throwaway repo root with one real C# file so the
/// tree-sitter outline is exercised end-to-end. Determinism is part of the contract (byte-identical output for a
/// fixed timestamp), so the CLI/incremental cache diff cleanly.
/// </summary>
[TestClass]
[CoversNode("graph-build")]
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
    public void Build_LocatesGeneratedFilesWithoutExpandingThem()
    {
        // The alternative — leaving .c unregistered because one dependency ships 31 MB of generated parser
        // tables — throws out a language to avoid some files. The file is what differs, so the file is what
        // is tested: the generated one is located, sized and labelled; the hand-written one beside it is
        // parsed in full.
        var root = Path.Combine(Path.GetTempPath(), "nexgraph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "parser.c"),
            "/* Automatically @generated by tree-sitter */\n\nstruct Tables { int x; };\n");
        File.WriteAllText(Path.Combine(root, "scanner.c"),
            "#include <stdio.h>\n\nstruct Scanner { int depth; };\n");

        var state = new ProductState
        {
            Product = new ProductDocument { Product = "Demo" },
            Nodes = new Dictionary<string, ProductNode> { ["root"] = new ProductNode { Title = "Root" } },
        };

        try
        {
            var g = GraphBuilder.Build(state, root, new GraphBuildOptions { GeneratedAt = "T" });
            GraphNode? Node(string id) => g.Nodes.FirstOrDefault(n => n.Id == id);

            var generated = Node("file:parser.c");
            Assert.IsNotNull(generated, "a generated file is still located — it is part of the repo");
            Assert.AreEqual("c", generated!.Language, "and still labelled with its language");
            Assert.AreEqual("true", generated.Metadata?.GetValueOrDefault("generated"));
            Assert.IsFalse(g.Nodes.Any(n => n.Id.StartsWith("code:parser.c", StringComparison.Ordinal)),
                           "but nothing inside it becomes a node");

            Assert.IsNotNull(Node("file:scanner.c"));
            Assert.IsNull(Node("file:scanner.c")!.Metadata?.GetValueOrDefault("generated"),
                          "the hand-written file in the same folder is not generated");
            Assert.IsTrue(g.Nodes.Any(n => n.Id == "code:scanner.c#T:Scanner"), "and is parsed in full");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void Build_KeepsCodeLayerFactsOnAFileASnaplinkAlreadyCreated()
    {
        // Layer order is not knowledge order: a snaplink creates file:Sample.cs before anything reads it.
        // Adding the parsed node with TryAdd discarded the version that knew the language and the size, so
        // whether a file was a snaplink target decided whether it carried its own facts.
        var (root, state) = Setup();
        try
        {
            var g = Build(root, state);
            var file = g.Nodes.Single(n => n.Id == "file:Sample.cs");

            Assert.AreEqual("c-sharp", file.Language);
            Assert.IsTrue(int.TryParse(file.Metadata?.GetValueOrDefault("lines"), out var lines) && lines > 0,
                          "the snaplinked file still records its size");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void Build_RecordsDeclaredVisibility_AndProjectOutputType()
    {
        // Both facts sat in data the build already had — the outline computes visibility and ExtractCsproj
        // already had the .csproj XML open — and both were dropped, so "what is the public surface" and
        // "which projects are executables" had no answer that didn't mean re-reading the source.
        var root = Path.Combine(Path.GetTempPath(), "nexgraph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Visible.cs"),
            "namespace Demo;\ninternal class Hidden\n{\n    public void Open() { }\n    private void Shut() { }\n    protected int Guarded() => 1;\n}\npublic class Shown { }\n");
        File.WriteAllText(Path.Combine(root, "App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>WinExe</OutputType>"
            + "<TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root, "Lib.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(root, "Tests.csproj"),
            "<Project Sdk=\"MSTest.Sdk/3.6.4\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var state = new ProductState
        {
            Product = new ProductDocument { Product = "Demo" },
            Nodes = new Dictionary<string, ProductNode> { ["root"] = new ProductNode { Title = "Root" } },
        };

        try
        {
            var g = GraphBuilder.Build(state, root, new GraphBuildOptions { GeneratedAt = "T" });
            string? Meta(string id, string key) =>
                g.Nodes.FirstOrDefault(n => n.Id == id)?.Metadata?.GetValueOrDefault(key);

            Assert.AreEqual("internal", Meta("code:Visible.cs#T:Hidden", "visibility"), "no modifier at file scope is internal");
            Assert.AreEqual("public", Meta("code:Visible.cs#T:Shown", "visibility"));
            Assert.AreEqual("public", Meta("code:Visible.cs#T:Hidden/M:Open", "visibility"));
            Assert.AreEqual("private", Meta("code:Visible.cs#T:Hidden/M:Shut", "visibility"));
            Assert.AreEqual("protected", Meta("code:Visible.cs#T:Hidden/M:Guarded", "visibility"));

            Assert.AreEqual("winexe", Meta("file:App.csproj", "output_type"));
            Assert.AreEqual("net10.0", Meta("file:App.csproj", "target_framework"));
            Assert.AreEqual("Microsoft.NET.Sdk", Meta("file:App.csproj", "sdk"));
            // Some projects declare what they are ONLY through the SDK — a test project can carry
            // Sdk="MSTest.Sdk" and no package reference at all, so inferring test-ness from a dependency
            // works in one repository and silently fails in the next.
            Assert.AreEqual("MSTest.Sdk/3.6.4", Meta("file:Tests.csproj", "sdk"));
            // Absent OutputType means a library — the SDK's own default, recorded rather than left missing.
            Assert.AreEqual("library", Meta("file:Lib.csproj", "output_type"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void Build_TurnsTypeMentionsIntoReferences_ResolvingOrDropping()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexgraph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Uses.cs"),
            "namespace Demo;\npublic enum Severity { Low, High }\npublic static class Ops { public static int Mount; }\n"
            + "public class Service\n{\n    private readonly Widget _w;\n"
            + "    public void Go() { var s = Severity.High; var m = Ops.Mount; var b = new StringBuilder(); }\n}\n"
            + "public class Widget { }\n");

        var state = new ProductState
        {
            Product = new ProductDocument { Product = "Demo" },
            Nodes = new Dictionary<string, ProductNode> { ["root"] = new ProductNode { Title = "Root" } },
        };

        try
        {
            var g = GraphBuilder.Build(state, root, new GraphBuildOptions { GeneratedAt = "T" });
            const string go = "code:Uses.cs#T:Service/M:Go";

            bool Refs(string source, string target) => g.Edges.Any(e =>
                e.Source == source && e.Target == target && e.Relationship == EdgeRelationship.References);

            Assert.IsTrue(Refs(go, "code:Uses.cs#T:Severity"), "an enum value reaches its enum");
            Assert.IsTrue(Refs(go, "code:Uses.cs#T:Ops"), "a static access reaches the class");
            Assert.IsTrue(Refs("code:Uses.cs#T:Service/F:_w", "code:Uses.cs#T:Widget"),
                          "a field's declared type is a reference from the field");

            // A construction says more than a mention of the same pair, so only the stronger edge survives.
            Assert.IsTrue(g.Edges.Any(e => e.Source == go && e.Target == "external:StringBuilder"
                                        && e.Relationship == EdgeRelationship.Instantiates));
            Assert.IsFalse(Refs(go, "external:StringBuilder"), "instantiates is not restated as references");

            // An unresolved *mention* is dropped rather than stubbed — a `new` earns an external node, but
            // merely naming a framework type must not, or every member would drag one in.
            Assert.IsFalse(g.Nodes.Any(n => n.Id == "external:Severity" || n.Id == "external:Ops"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
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
    public void Build_LinksXamlToCodeBehind_AndCsprojDependencies()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexgraph-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // A WPF view + its code-behind, and two projects with a reference + a package.
        File.WriteAllText(Path.Combine(root, "Widget.xaml"),
            "<UserControl x:Class=\"Demo.Widget\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"\n" +
            "             xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">\n" +
            "  <UserControl.Resources>\n" +
            "    <SolidColorBrush x:Key=\"GoBrush\" Color=\"Red\" />\n" +
            "  </UserControl.Resources>\n" +
            "  <Button x:Name=\"Go\" AutomationProperties.AutomationId=\"Go_Button\" Click=\"OnGo\"\n" +
            "          Background=\"{StaticResource GoBrush}\" Content=\"{Binding Caption}\"\n" +
            "          Command=\"{Binding RefreshCommand}\" />\n" +
            "</UserControl>\n");
        // The MVVM Toolkit generates the names the view binds to, so the graph has to map them back.
        File.WriteAllText(Path.Combine(root, "WidgetViewModel.cs"),
            "namespace Demo;\n" +
            "public partial class WidgetViewModel\n" +
            "{\n" +
            "    [ObservableProperty] private string _caption = \"\";\n" +
            "    [RelayCommand] private void Refresh() { }\n" +
            "}\n");
        File.WriteAllText(Path.Combine(root, "Widget.xaml.cs"),
            "namespace Demo;\n" +
            "public partial class Widget\n" +
            "{\n" +
            "    private void OnGo(object sender, System.EventArgs e)\n" +
            "    {\n" +
            "        Go.IsEnabled = false;\n" +
            "    }\n" +
            "}\n");
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

            // XAML view → its code-behind class (x:Class), high-certainty. Resolved in its own pass now that
            // both halves come from the code layer, where nothing orders Widget.xaml before Widget.xaml.cs.
            Assert.IsTrue(g.Nodes.Any(n => n.Id == "file:Widget.xaml"), "the .xaml is a node");
            Assert.IsTrue(g.Edges.Any(e => e is { Source: "file:Widget.xaml", Target: "code:Widget.xaml.cs#T:Widget", Relationship: EdgeRelationship.ViewOf }),
                "view_of links the .xaml to its code-behind type");

            // The view's own structure is in the graph: the xml grammar makes .xaml real code, so its
            // addressable elements are nodes rather than an opaque file.
            foreach (var id in new[] { "code:Widget.xaml#T:Widget", "code:Widget.xaml#N:Go", "code:Widget.xaml#A:Go_Button" })
                Assert.IsTrue(g.Nodes.Any(n => n.Id == id), $"{id} is a node");
            Assert.IsTrue(g.Nodes.Any(n => n.Id == "code:Widget.xaml#N:Go/M:OnGo"), "the Click handler is a member");
            Assert.IsTrue(g.Edges.Any(e => e is { Source: "file:Widget.xaml", Target: "code:Widget.xaml#N:Go", Relationship: EdgeRelationship.Contains }),
                "the file contains its anchors");

            // The two halves of the view, joined: which method handles the button, and which code touches it.
            Assert.IsTrue(g.Edges.Any(e => e is { Source: "code:Widget.xaml#N:Go", Target: "code:Widget.xaml.cs#T:Widget/M:OnGo", Relationship: EdgeRelationship.Handles }),
                "handles links the element to the code-behind method wired to its Click");
            Assert.IsTrue(g.Edges.Any(e => e is { Source: "code:Widget.xaml.cs#T:Widget/M:OnGo", Target: "code:Widget.xaml#N:Go", Relationship: EdgeRelationship.References }),
                "references runs code-behind → element: the x:Name field is generated into obj/, so the usage is the only evidence");

            // {StaticResource} → the x:Key it resolves to, near-certain when the key is in the same file.
            var res = g.Edges.SingleOrDefault(e => e.Relationship == EdgeRelationship.UsesResource);
            Assert.IsNotNull(res, "uses_resource links the element to the brush it resolves");
            Assert.AreEqual("code:Widget.xaml#K:GoBrush", res!.Target);
            Assert.AreEqual(GraphConfidence.NearCertain, res.Confidence);

            // A binding names the MVVM Toolkit's generated surface — WordWrap, RefreshCommand — which exists
            // only in generated source. Both must land on the declaration that produces them.
            var bindings = g.Edges.Where(e => e.Relationship == EdgeRelationship.BindsTo)
                                  .Select(e => e.Target).ToList();
            CollectionAssert.Contains(bindings, "code:WidgetViewModel.cs#T:WidgetViewModel/F:_caption",
                "{Binding Caption} resolves to the [ObservableProperty] field behind it");
            CollectionAssert.Contains(bindings, "code:WidgetViewModel.cs#T:WidgetViewModel/M:Refresh",
                "{Binding RefreshCommand} resolves to the [RelayCommand] method behind it");

            // csproj → project + package dependencies.
            Assert.IsTrue(g.Edges.Any(e => e is { Source: "file:A.csproj", Target: "file:B.csproj", Relationship: EdgeRelationship.DependsOn }),
                "depends_on links A.csproj → B.csproj");
            Assert.IsTrue(g.Edges.Any(e => e is { Source: "file:A.csproj", Target: "external:SomePackage", Relationship: EdgeRelationship.DependsOn }),
                "depends_on links A.csproj → the NuGet package");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
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

    [TestMethod]
    public void CodeRoot_ReadsSourceFromThatDirectory_ForWorktreeAwareGraphs()
    {
        var productRoot = Path.Combine(Path.GetTempPath(), "nexgraph-prod-" + Guid.NewGuid().ToString("N"));
        var codeRoot = Path.Combine(Path.GetTempPath(), "nexgraph-code-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(productRoot);
        Directory.CreateDirectory(codeRoot);
        try
        {
            // Same repo-relative file, different content in each root — as a worktree branch differs from main.
            File.WriteAllText(Path.Combine(productRoot, "Sample.cs"), "namespace Demo;\npublic class MainOnly { }\n");
            File.WriteAllText(Path.Combine(codeRoot, "Sample.cs"), "namespace Demo;\npublic class BranchOnly { }\n");

            var state = new ProductState
            {
                Product = new ProductDocument { Product = "Demo" },
                Nodes = new Dictionary<string, ProductNode> { ["root"] = new ProductNode { Title = "Root" } },
            };

            bool HasType(KnowledgeGraph g, string label) =>
                g.Nodes.Any(n => n.Type == NodeType.Type && n.Label == label);

            // Default (CodeRoot null) reads the product root — unchanged for a normal checkout / CI.
            var fromMain = GraphBuilder.Build(state, productRoot, new GraphBuildOptions { GeneratedAt = "T" });
            Assert.IsTrue(HasType(fromMain, "MainOnly"));
            Assert.IsFalse(HasType(fromMain, "BranchOnly"));

            // CodeRoot set: the code layer reads the branch's copy instead, at the SAME repo-relative id.
            var fromBranch = GraphBuilder.Build(state, productRoot,
                new GraphBuildOptions { GeneratedAt = "T", CodeRoot = codeRoot });
            Assert.IsTrue(HasType(fromBranch, "BranchOnly"), "code layer must read from CodeRoot");
            Assert.IsFalse(HasType(fromBranch, "MainOnly"));
            Assert.IsTrue(fromBranch.Nodes.Any(n => n.Id == "file:Sample.cs"),
                "node ids stay repo-relative regardless of which root supplied the source");
        }
        finally
        {
            try { Directory.Delete(productRoot, recursive: true); } catch { }
            try { Directory.Delete(codeRoot, recursive: true); } catch { }
        }
    }
}

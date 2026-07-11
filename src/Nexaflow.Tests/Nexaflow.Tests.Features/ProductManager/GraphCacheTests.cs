using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// The incremental build cache: an unchanged file is served from its cached contribution (never re-parsed), yet the
/// <b>global</b> resolution still re-runs — so a change in one file re-points inferred edges asserted by a cached
/// one. A warm-cache build is byte-identical to a fresh one (the cache is an optimisation, never a behaviour change).
/// </summary>
[TestClass]
public class GraphCacheTests
{
    private static ProductState MinimalState() => new()
    {
        Product = new ProductDocument { Product = "Demo" },
        Nodes = new Dictionary<string, ProductNode> { ["root"] = new ProductNode { Title = "Root" } },
    };

    private static string NewRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), "nexcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static (KnowledgeGraph Graph, GraphCache Cache) Build(string root, GraphCache? cache) =>
        GraphBuilder.BuildWithCache(MinimalState(), root, new GraphBuildOptions { GeneratedAt = "T" }, cache);

    [TestMethod]
    [CoversNode("graphviewer")]
    public void WarmCacheBuild_IsByteIdentical_ToFreshBuild()
    {
        var root = NewRepo();
        try
        {
            File.WriteAllText(Path.Combine(root, "A.cs"), "namespace Demo;\npublic class A { public void Go() { new B(); } }\n");
            File.WriteAllText(Path.Combine(root, "B.cs"), "namespace Demo;\npublic class B { }\n");

            var fresh = Build(root, null);
            var warm = Build(root, fresh.Cache);   // every file is a cache hit

            var a = JsonSerializer.Serialize(fresh.Graph, ProductJson.Options);
            var b = JsonSerializer.Serialize(warm.Graph, ProductJson.Options);
            Assert.AreEqual(a, b, "a warm-cache build produces the byte-identical graph");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [CoversNode("graphviewer")]
    public void Cache_SurvivesJsonRoundTrip_AndStillServesHits()
    {
        var root = NewRepo();
        try
        {
            // Exercises every cached relation kind: inheritance, signature (typed return + param), attribute, instantiation.
            File.WriteAllText(Path.Combine(root, "A.cs"),
                "namespace Demo;\npublic class A : IThing\n{\n    [Track]\n    public Widget Go(Widget w) { new B(); return w; }\n}\n" +
                "public class Widget { }\npublic class B { }\npublic interface IThing { }\npublic class TrackAttribute : System.Attribute { }\n");

            var fresh = Build(root, null);

            // Round-trip the cache through the exact on-disk JSON format the CLI/task persist.
            var json = JsonSerializer.Serialize(fresh.Cache, ProductJson.Options);
            var reloaded = JsonSerializer.Deserialize<GraphCache>(json, ProductJson.Options)!;
            Assert.AreEqual(fresh.Cache.Files.Count, reloaded.Files.Count, "every file entry survives the round-trip");

            var warm = Build(root, reloaded);   // a full cache hit off the DESERIALIZED cache
            Assert.AreSame(reloaded.Files["A.cs"], warm.Cache.Files["A.cs"], "the reloaded contribution served the hit");
            Assert.AreEqual(
                JsonSerializer.Serialize(fresh.Graph, ProductJson.Options),
                JsonSerializer.Serialize(warm.Graph, ProductJson.Options),
                "a build off the JSON-reloaded cache is byte-identical (relation records deserialize losslessly)");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [CoversNode("graphviewer")]
    public void ChangeInOneFile_ReResolvesInferredEdge_AssertedByACachedFile()
    {
        var root = NewRepo();
        try
        {
            // A instantiates Widget; Widget lives in B.
            File.WriteAllText(Path.Combine(root, "A.cs"), "namespace Demo;\npublic class Caller { public void Go() { new Widget(); } }\n");
            File.WriteAllText(Path.Combine(root, "B.cs"), "namespace Demo;\npublic class Widget { }\n");

            var first = Build(root, null);
            const string go = "code:A.cs#T:Caller/M:Go";
            Assert.IsTrue(first.Graph.Edges.Any(e => e.Source == go && e.Relationship == EdgeRelationship.Instantiates
                && e.Target == "code:B.cs#T:Widget"), "initially resolves to B's Widget");
            var cachedA = first.Cache.Files["A.cs"];   // capture the contribution object A was extracted into

            // Move Widget to a new file C; B loses it. A.cs is untouched.
            File.WriteAllText(Path.Combine(root, "B.cs"), "namespace Demo;\npublic class Helper { }\n");
            File.WriteAllText(Path.Combine(root, "C.cs"), "namespace Demo;\npublic class Widget { }\n");

            var second = Build(root, first.Cache);

            // A was reused verbatim from the cache (same contribution object — not re-parsed)…
            Assert.AreSame(cachedA, second.Cache.Files["A.cs"], "the unchanged file is served from the cache");
            // …yet its inferred edge re-points to Widget's NEW location, because resolution re-ran globally.
            Assert.IsTrue(second.Graph.Edges.Any(e => e.Source == go && e.Relationship == EdgeRelationship.Instantiates
                && e.Target == "code:C.cs#T:Widget"), "the cached file's edge re-points to Widget in C");
            Assert.IsFalse(second.Graph.Edges.Any(e => e.Source == go && e.Target == "code:B.cs#T:Widget"),
                "the stale edge to B is gone");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [CoversNode("graphviewer")]
    public void DeletedFile_IsPrunedFromGraphAndCache()
    {
        var root = NewRepo();
        try
        {
            File.WriteAllText(Path.Combine(root, "A.cs"), "namespace Demo;\npublic class A { }\n");
            File.WriteAllText(Path.Combine(root, "B.cs"), "namespace Demo;\npublic class B { }\n");
            var first = Build(root, null);
            Assert.IsTrue(first.Graph.Nodes.Any(n => n.Id == "code:B.cs#T:B"));

            File.Delete(Path.Combine(root, "B.cs"));
            var second = Build(root, first.Cache);

            Assert.IsFalse(second.Graph.Nodes.Any(n => n.Id == "code:B.cs#T:B"), "B's nodes are gone from the graph");
            Assert.IsFalse(second.Cache.Files.ContainsKey("B.cs"), "B's cache entry is pruned");
            Assert.IsTrue(second.Graph.Nodes.Any(n => n.Id == "code:A.cs#T:A"), "A survives");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    [CoversNode("graphviewer")]
    public void SchemaVersionMismatch_DiscardsCache_ForACleanReExtract()
    {
        var root = NewRepo();
        try
        {
            File.WriteAllText(Path.Combine(root, "A.cs"), "namespace Demo;\npublic class A { }\n");

            // A cache from an older extractor, poisoned with a ghost file that no longer exists on disk.
            var stale = new GraphCache { SchemaVersion = GraphSchema.Version + 100 };
            stale.Files["ghost.cs"] = new FileContribution
            {
                Hash = "deadbeef",
                Nodes = { new GraphNode { Id = "file:ghost.cs", Type = NodeType.File, Label = "ghost.cs" } },
            };

            var built = Build(root, stale);

            Assert.IsFalse(built.Graph.Nodes.Any(n => n.Id == "file:ghost.cs"), "a schema-mismatched cache is not trusted");
            Assert.AreEqual(GraphSchema.Version, built.Cache.SchemaVersion, "the returned cache is re-stamped to the current schema");
            Assert.IsFalse(built.Cache.Files.ContainsKey("ghost.cs"), "the ghost entry did not survive");
            Assert.IsTrue(built.Graph.Nodes.Any(n => n.Id == "code:A.cs#T:A"), "the real file was extracted fresh");
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

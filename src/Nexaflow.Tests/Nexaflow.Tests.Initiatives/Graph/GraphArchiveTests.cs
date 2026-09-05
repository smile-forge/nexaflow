using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Graph.Store;
using Nexaflow.Syntax;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// The binary archive that replaced graph.json and graph-cache.json.
/// <para>
/// A hand-written format has one failure mode worth testing above all others: a field written in one order
/// and read in another, which does not throw — it silently shifts every later field by four bytes and hands
/// back a graph that is subtly wrong. So the round trip asserts on values rather than on counts, and it uses
/// a fixture where every field is distinguishable from its neighbours: no two strings equal, nullables both
/// null and set, and an empty collection beside a populated one.
/// </para>
/// </summary>
[TestClass]
[CoversNode("graph-archive")]
public class GraphArchiveTests
{
    private string _dir = "";
    private string Path_ => System.IO.Path.Combine(_dir, "graph.bin");

    [TestInitialize]
    public void Setup() => _dir = Directory.CreateTempSubdirectory("nexa-archive-").FullName;

    [TestCleanup]
    public void Cleanup() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    /// <summary>Every field set to something it could not be confused with, so a misaligned read shows up as
    /// a wrong value rather than as a plausible one.</summary>
    private static GraphSnapshot Fixture() => new()
    {
        Graph = new KnowledgeGraph
        {
            Metadata = new GraphMetadata
            {
                NodeCount = 2, EdgeCount = 1, HyperEdgeCount = 1, CommunityCount = 3,
                GeneratedAt = "2026-09-01T02:03:04", Scope = "whole_repo", ProductName = "Fixture",
            },
            Nodes =
            [
                new GraphNode
                {
                    Id = "code:src/A.cs#T:A", Type = "type", Label = "A", FilePath = "src/A.cs",
                    Language = "c-sharp", Community = 7, Confidence = 0.5, Source = "src/A.cs",
                    Metadata = new Dictionary<string, string> { ["kind"] = "class", ["line"] = "12" },
                },
                // Every nullable at its other value: no file, no language, no community, no metadata.
                new GraphNode { Id = "external:System", Type = "external", Label = "System", Confidence = 1.0 },
            ],
            Edges =
            [
                new GraphEdge
                {
                    Source = "code:src/A.cs#T:A", Target = "external:System", Relationship = "references",
                    Weight = 2.5, Confidence = 0.25, ProvenanceFile = "src/A.cs",
                    Metadata = new Dictionary<string, string> { ["line"] = "13" },
                },
            ],
            HyperEdges =
            [
                new GraphHyperEdge
                {
                    Relationship = "signature", Weight = 1.5, Confidence = 0.75, ProvenanceFile = "src/A.cs",
                    Endpoints =
                    [
                        new HyperEndpoint { Node = "code:src/A.cs#T:A", Role = "member", Ordinal = 0, Confidence = 0.9 },
                        new HyperEndpoint { Node = "external:System", Role = "return" },
                    ],
                },
            ],
        },
        Cache = new GraphCache
        {
            Files =
            {
                ["src/A.cs"] = new FileContribution
                {
                    Hash  = "hash-a",
                    Nodes = [new GraphNode { Id = "code:src/A.cs#T:A", Type = "type", Label = "A" }],
                    Edges = [new GraphEdge { Source = "file:src/A.cs", Target = "code:src/A.cs#T:A", Relationship = "contains" }],
                    Bases = [new CachedBase { TypeId = "code:src/A.cs#T:A", Name = "IThing", IsInterface = true }],
                    Refs  = [new RawRef("T:A/M:Go", RawRefKind.New, "Widget", 21)],
                    Signatures = [new RawSignature("T:A/M:Go", "int", ["string", "bool"], 20)],
                    Attributes = [new RawAttribute("T:A", "TestClass", 0, 3)],
                    Calls      = [new RawCall("T:A/M:Go", "Handle", ["Widget"], 22)],
                    FileRefs   = [new RawFileRef("T:A/M:Go", "docs/features.md", 23)],
                },
                // A file that contributed nothing is still a file the builder has seen and must not re-read.
                ["src/Empty.cs"] = new FileContribution { Hash = "hash-empty" },
            },
        },
        Files =
        {
            ["src/A.cs"]     = new FileStamp("hash-a", 4096, new DateTime(2026, 9, 1, 1, 2, 3, DateTimeKind.Utc)),
            ["src/Empty.cs"] = new FileStamp("hash-empty", 0, new DateTime(2026, 8, 31, 23, 59, 58, DateTimeKind.Utc)),
        },
    };

    [TestMethod]
    public void TheWholeSnapshot_ComesBackFieldForField()
    {
        GraphArchive.Write(Path_, Fixture());
        var back = GraphArchive.Read(Path_);

        Assert.IsNotNull(back);
        var expected = Fixture();

        Assert.AreEqual(expected.Graph.Metadata.CommunityCount, back!.Graph.Metadata.CommunityCount);
        Assert.AreEqual(expected.Graph.Metadata.GeneratedAt,    back.Graph.Metadata.GeneratedAt);
        Assert.AreEqual(expected.Graph.Metadata.ProductName,    back.Graph.Metadata.ProductName);

        var node = back.Graph.Nodes[0];
        Assert.AreEqual("code:src/A.cs#T:A", node.Id);
        Assert.AreEqual("src/A.cs", node.FilePath);
        Assert.AreEqual("c-sharp",  node.Language);
        Assert.AreEqual(7,          node.Community);
        Assert.AreEqual(0.5,        node.Confidence);
        Assert.AreEqual("class",    node.Metadata!["kind"]);
        Assert.AreEqual("12",       node.Metadata["line"]);

        var external = back.Graph.Nodes[1];
        Assert.IsNull(external.FilePath,  "a null must not come back as an empty string");
        Assert.IsNull(external.Language);
        Assert.IsNull(external.Community, "a null community must not come back as zero");

        var edge = back.Graph.Edges[0];
        Assert.AreEqual("references", edge.Relationship);
        Assert.AreEqual(2.5,          edge.Weight);
        Assert.AreEqual(0.25,         edge.Confidence);
        Assert.AreEqual("src/A.cs",   edge.ProvenanceFile);

        var hyper = back.Graph.HyperEdges[0];
        Assert.AreEqual(2,   hyper.Endpoints.Count);
        Assert.AreEqual(0,   hyper.Endpoints[0].Ordinal);
        Assert.AreEqual(0.9, hyper.Endpoints[0].Confidence);
        Assert.IsNull(hyper.Endpoints[1].Ordinal, "an absent ordinal is not ordinal zero");
        Assert.IsNull(hyper.Endpoints[1].Confidence);
    }

    [TestMethod]
    public void ThePerFileMaterial_ComesBackFieldForField()
    {
        GraphArchive.Write(Path_, Fixture());
        var back = GraphArchive.Read(Path_);

        Assert.IsNotNull(back);
        var c = back!.Cache.Files["src/A.cs"];

        Assert.AreEqual("hash-a", c.Hash);
        Assert.AreEqual("IThing", c.Bases[0].Name);
        Assert.IsTrue(c.Bases[0].IsInterface);
        Assert.AreEqual(new RawRef("T:A/M:Go", RawRefKind.New, "Widget", 21), c.Refs[0]);
        Assert.AreEqual("int", c.Signatures[0].ReturnType);
        CollectionAssert.AreEqual(new[] { "string", "bool" }, c.Signatures[0].ParamTypes.ToArray());
        Assert.AreEqual(new RawAttribute("T:A", "TestClass", 0, 3), c.Attributes[0]);
        Assert.AreEqual("Handle", c.Calls[0].Callee);
        CollectionAssert.AreEqual(new[] { "Widget" }, c.Calls[0].NewArgTypes.ToArray());
        Assert.AreEqual(new RawFileRef("T:A/M:Go", "docs/features.md", 23), c.FileRefs[0]);

        Assert.AreEqual("hash-empty", back.Cache.Files["src/Empty.cs"].Hash);
        Assert.AreEqual(0, back.Cache.Files["src/Empty.cs"].Refs.Count);
    }

    [TestMethod]
    public void TheFileStamps_ComeBackExactly_SoAScanCanTrustThem()
    {
        GraphArchive.Write(Path_, Fixture());
        var stamps = GraphArchive.ReadFileIndex(Path_);

        Assert.IsNotNull(stamps);
        var a = stamps!["src/A.cs"];
        Assert.AreEqual(4096, a.Length);
        Assert.AreEqual(new DateTime(2026, 9, 1, 1, 2, 3, DateTimeKind.Utc), a.ModifiedUtc);
        Assert.IsTrue(a.Matches(4096, new DateTime(2026, 9, 1, 1, 2, 3, DateTimeKind.Utc)));
        Assert.IsFalse(a.Matches(4097, new DateTime(2026, 9, 1, 1, 2, 3, DateTimeKind.Utc)));
    }

    /// <summary>The reason the sections exist: a query pays for the graph and not for the two thirds of the
    /// file it cannot use.</summary>
    [TestMethod]
    public void ReadingTheGraphAlone_SkipsThePerFileMaterial()
    {
        GraphArchive.Write(Path_, Fixture());

        var graph = GraphArchive.ReadGraph(Path_);

        Assert.IsNotNull(graph);
        Assert.AreEqual(2, graph!.Nodes.Count);
        Assert.AreEqual(1, graph.Edges.Count);
        Assert.AreEqual("Fixture", graph.Metadata.ProductName);
    }

    [TestMethod]
    public void ReadingTheCacheAlone_SkipsTheAssembledGraph()
    {
        GraphArchive.Write(Path_, Fixture());

        var cache = GraphArchive.ReadCache(Path_);

        Assert.IsNotNull(cache);
        Assert.AreEqual(2, cache!.Files.Count);
        Assert.AreEqual("hash-a", cache.Files["src/A.cs"].Hash);
    }

    /// <summary>Null is the answer to every kind of "not readable", because a caller that has to tell them
    /// apart in order to decide what to do next does not exist: all of them mean build it.</summary>
    [TestMethod]
    public void AFileThatIsNotOne_ReadsAsNothingRatherThanThrowing()
    {
        Assert.IsNull(GraphArchive.Read(System.IO.Path.Combine(_dir, "absent.bin")), "no file");

        File.WriteAllText(Path_, "this is not an archive");
        Assert.IsNull(GraphArchive.Read(Path_), "wrong magic");

        File.WriteAllBytes(Path_, [0x4E, 0x46, 0x49]);
        Assert.IsNull(GraphArchive.Read(Path_), "truncated before the magic is complete");
    }

    /// <summary>A layout change makes every offset in an existing file a lie. It is discarded on sight
    /// rather than read into something that looks like a graph.</summary>
    [TestMethod]
    public void AnArchiveFromAnotherLayoutVersion_IsDiscarded()
    {
        GraphArchive.Write(Path_, Fixture());

        var bytes = File.ReadAllBytes(Path_);
        BitConverter.GetBytes(GraphArchive.FormatVersion + 1).CopyTo(bytes, 7);   // straight after the magic
        File.WriteAllBytes(Path_, bytes);

        Assert.IsNull(GraphArchive.Read(Path_));
    }

    /// <summary>Interning is the whole reason the file is small: a node id repeated across every edge that
    /// touches it is stored once. Asserting on it directly keeps a future change from quietly undoing it.</summary>
    [TestMethod]
    public void ARepeatedNodeId_IsStoredOnce()
    {
        var snapshot = Fixture();
        var id       = new string('x', 400);

        snapshot.Graph.Nodes.Add(new GraphNode { Id = id, Type = "type", Label = "x" });
        for (var i = 0; i < 50; i++)
            snapshot.Graph.Edges.Add(new GraphEdge { Source = id, Target = id, Relationship = "references" });

        GraphArchive.Write(Path_, snapshot);

        var occurrences = Occurrences(File.ReadAllBytes(Path_), System.Text.Encoding.UTF8.GetBytes(id));
        Assert.AreEqual(1, occurrences,
            $"the id is written once and referred to by index; found it {occurrences} times");
    }

    private static int Occurrences(byte[] haystack, byte[] needle)
    {
        var found = 0;
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length && match; j++) match = haystack[i + j] == needle[j];
            if (match) found++;
        }
        return found;
    }
}

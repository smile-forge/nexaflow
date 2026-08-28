using System;
using System.IO;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// Keeping the graph honest one file at a time. A whole-repo build costs about ninety seconds, almost all of
/// it walking thousands of files to find they have not changed — which made "the graph might be stale"
/// something every caller had to reason about, and made editing a file the graph had not yet seen feel
/// impossible. Re-reading the one file that was touched costs a parse, and the cache is already per-file and
/// content-hashed, so this is the cheap half of a build done on demand.
/// </summary>
[TestClass]
[CoversNode("graph-edit")]
public class GraphRefreshTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexaflow-graph-refresh", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "src"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private const string Rel = "src/Sample.cs";

    private void Write(string text) =>
        File.WriteAllText(Path.Combine(_root, "src", "Sample.cs"), text);

    private static (KnowledgeGraph Graph, GraphCache Cache) Empty() => (new KnowledgeGraph(), new GraphCache());

    private static string[] Ids(KnowledgeGraph g) => [.. g.Nodes.Select(n => n.Id).Order(StringComparer.Ordinal)];

    [TestMethod]
    public void AFileTheGraphHasNeverSeen_IsAddedByOneRefresh()
    {
        Write("public class C\n{\n    public void M() { }\n}\n");
        var (graph, cache) = Empty();

        Assert.IsTrue(GraphBuilder.RefreshFile(graph, cache, _root, Rel), "the file changed, so it was merged");

        CollectionAssert.Contains(Ids(graph), $"code:{Rel}#T:C");
        CollectionAssert.Contains(Ids(graph), $"code:{Rel}#T:C/M:M");
        Assert.AreEqual(graph.Nodes.Count, graph.Metadata.NodeCount, "the summary counts must follow the graph");
    }

    [TestMethod]
    public void ASecondRefreshOfUnchangedContent_DoesNothing()
    {
        Write("public class C\n{\n    public void M() { }\n}\n");
        var (graph, cache) = Empty();
        GraphBuilder.RefreshFile(graph, cache, _root, Rel);

        Assert.IsFalse(GraphBuilder.RefreshFile(graph, cache, _root, Rel),
            "nothing changed, so there is nothing to save — this is what keeps a no-op edit cheap");
    }

    [TestMethod]
    public void ADeclarationAddedSinceTheLastBuild_AppearsWithoutARebuild()
    {
        Write("public class C\n{\n    public void M() { }\n}\n");
        var (graph, cache) = Empty();
        GraphBuilder.RefreshFile(graph, cache, _root, Rel);

        Write("public class C\n{\n    public void M() { }\n\n    public void N() { }\n}\n");
        Assert.IsTrue(GraphBuilder.RefreshFile(graph, cache, _root, Rel));

        CollectionAssert.Contains(Ids(graph), $"code:{Rel}#T:C/M:N");
    }

    /// <summary>A node the file no longer declares has to go, or it is left claiming something untrue and
    /// every path from it is a dead end.</summary>
    [TestMethod]
    public void ADeclarationRemovedSinceTheLastBuild_StopsBeingClaimed()
    {
        Write("public class C\n{\n    public void M() { }\n\n    public void N() { }\n}\n");
        var (graph, cache) = Empty();
        GraphBuilder.RefreshFile(graph, cache, _root, Rel);
        CollectionAssert.Contains(Ids(graph), $"code:{Rel}#T:C/M:N");

        Write("public class C\n{\n    public void M() { }\n}\n");
        Assert.IsTrue(GraphBuilder.RefreshFile(graph, cache, _root, Rel));

        CollectionAssert.DoesNotContain(Ids(graph), $"code:{Rel}#T:C/M:N");
        CollectionAssert.Contains(Ids(graph), $"code:{Rel}#T:C/M:M", "and the ones still there stay");
    }

    [TestMethod]
    public void ADeletedFile_IsPrunedRatherThanLeftBehind()
    {
        Write("public class C\n{\n    public void M() { }\n}\n");
        var (graph, cache) = Empty();
        GraphBuilder.RefreshFile(graph, cache, _root, Rel);

        File.Delete(Path.Combine(_root, "src", "Sample.cs"));
        Assert.IsTrue(GraphBuilder.RefreshFile(graph, cache, _root, Rel), "the deletion is a change worth saving");

        Assert.AreEqual(0, graph.Nodes.Count(n => n.Source == Rel), "nothing should still be attributed to it");
        Assert.IsFalse(cache.Files.ContainsKey(Rel), "and its cached contribution goes too");
    }

    [TestMethod]
    public void RefreshingOneFile_LeavesEveryOtherFileAlone()
    {
        File.WriteAllText(Path.Combine(_root, "src", "Other.cs"), "public class Other { }\n");
        Write("public class C { }\n");

        var (graph, cache) = Empty();
        GraphBuilder.RefreshFile(graph, cache, _root, "src/Other.cs");
        GraphBuilder.RefreshFile(graph, cache, _root, Rel);

        Write("public class C\n{\n    public void M() { }\n}\n");
        GraphBuilder.RefreshFile(graph, cache, _root, Rel);

        CollectionAssert.Contains(Ids(graph), "code:src/Other.cs#T:Other",
            "re-reading one file must not disturb what another contributed");
    }

    [TestMethod]
    public void AMissingFileTheGraphNeverHad_IsNotAChange()
    {
        var (graph, cache) = Empty();
        Assert.IsFalse(GraphBuilder.RefreshFile(graph, cache, _root, "src/NeverExisted.cs"));
    }
}

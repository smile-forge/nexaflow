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
    public void FilesRefreshedTogether_EachContribute_AndOnlyTheChangedOnesAreNamed()
    {
        Write("public class C\n{\n    public void M() { }\n}\n");
        File.WriteAllText(Path.Combine(_root, "src", "Other.cs"), "public class D { }\n");
        var (graph, cache) = Empty();

        CollectionAssert.AreEquivalent(new[] { Rel, "src/Other.cs" },
            GraphBuilder.RefreshFiles(graph, cache, _root, [Rel, "src/Other.cs"]).ToList());
        CollectionAssert.IsSubsetOf(new[] { $"code:{Rel}#T:C/M:M", "code:src/Other.cs#T:D" }, Ids(graph).ToList());

        File.WriteAllText(Path.Combine(_root, "src", "Other.cs"), "public class E { }\n");
        CollectionAssert.AreEqual(new[] { "src/Other.cs" }, GraphBuilder.RefreshFiles(graph, cache, _root, [Rel, "src/Other.cs"]).ToList());
        CollectionAssert.DoesNotContain(Ids(graph).ToList(), "code:src/Other.cs#T:D");
        CollectionAssert.Contains(Ids(graph).ToList(), $"code:{Rel}#T:C", "the unchanged file keeps what it contributed");
    }

    private void WriteFile(string rel, string text) =>
        File.WriteAllText(Path.Combine(_root, rel.Replace('/', Path.DirectorySeparatorChar)), text);

    private static bool Links(KnowledgeGraph g, string source, string target, string relationship) =>
        g.Edges.Any(e => e.Source == source && e.Target == target && e.Relationship == relationship);

    private const string Target = "public class Target\n{\n    public void Run() { }\n}\n";
    private const string Caller = "public class Caller : Target\n{\n    public void Go() { Run(); new Target(); }\n}\n";

    [TestMethod]
    public void TheLinksAFileMakes_AreWorkedOutByItsRefresh_AndSurviveTheNextEditToIt()
    {
        WriteFile("src/Target.cs", Target);
        WriteFile("src/Caller.cs", Caller);
        var (graph, cache) = Empty();
        GraphBuilder.RefreshFiles(graph, cache, _root, ["src/Target.cs", "src/Caller.cs"]);

        Assert.IsTrue(Links(graph, "code:src/Caller.cs#T:Caller/M:Go", "code:src/Target.cs#T:Target/M:Run", EdgeRelationship.Calls));
        Assert.IsTrue(Links(graph, "code:src/Caller.cs#T:Caller", "code:src/Target.cs#T:Target", EdgeRelationship.Extends));

        WriteFile("src/Caller.cs", Caller.Replace("new Target(); }", "new Target(); }\n\n    public void Other() { }"));
        Assert.IsTrue(GraphBuilder.RefreshFile(graph, cache, _root, "src/Caller.cs"));

        Assert.IsTrue(Links(graph, "code:src/Caller.cs#T:Caller/M:Go", "code:src/Target.cs#T:Target/M:Run", EdgeRelationship.Calls),
                      "an edit to the calling file keeps what it calls");
        Assert.IsTrue(Links(graph, "code:src/Caller.cs#T:Caller/M:Go", "code:src/Target.cs#T:Target", EdgeRelationship.Instantiates));
    }

    [TestMethod]
    public void AMemberRenamedAway_TakesTheEdgesIntoIt_FromAFileThatWasNotEdited()
    {
        WriteFile("src/Target.cs", Target);
        WriteFile("src/Caller.cs", Caller);
        var (graph, cache) = Empty();
        GraphBuilder.RefreshFiles(graph, cache, _root, ["src/Target.cs", "src/Caller.cs"]);

        WriteFile("src/Target.cs", Target.Replace("Run", "Start"));
        GraphBuilder.RefreshFile(graph, cache, _root, "src/Target.cs");

        Assert.IsFalse(graph.Edges.Any(e => e.Target == "code:src/Target.cs#T:Target/M:Run"), "nothing points at what is gone");
        Assert.IsTrue(Links(graph, "code:src/Caller.cs#T:Caller", "code:src/Target.cs#T:Target", EdgeRelationship.Extends),
                      "and what still stands is still linked");
    }

    [TestMethod]
    public void FilesRefreshedOneByOne_InEitherOrder_AreLinkedAsABuildOfThemLinksThem()
    {
        WriteFile("src/Target.cs", Target);
        WriteFile("src/Caller.cs", Caller);
        var built = GraphBuilder.BuildWithCache(new Nexaflow.Services.Initiatives.Product.Model.ProductState(), _root,
                                                new GraphBuildOptions { CodeRoot = _root, Incremental = false }, null).Graph;

        var (graph, cache) = Empty();
        // The caller first, before anything it names exists: what it names has to be found once that arrives.
        GraphBuilder.RefreshFile(graph, cache, _root, "src/Caller.cs");
        GraphBuilder.RefreshFile(graph, cache, _root, "src/Target.cs");

        static string[] Linked(KnowledgeGraph g) =>
            [.. g.Edges.Where(e => e.Relationship is EdgeRelationship.Calls or EdgeRelationship.Extends or EdgeRelationship.Instantiates)
                       .Select(e => $"{e.Source} {e.Relationship} {e.Target}").Order(StringComparer.Ordinal)];

        Assert.IsTrue(Linked(built).Length > 0, "the build found the links to compare against");
        CollectionAssert.AreEqual(Linked(built), Linked(graph),
                                  $"built:\n{string.Join("\n", Linked(built))}\nrefreshed:\n{string.Join("\n", Linked(graph))}");
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

    /// <summary>
    /// A file that is not here is not evidence that it should leave the graph. graph.json is shared with
    /// every worktree, and a branch that runs a build publishes its own files into it — so an absent file is
    /// as likely to be a parallel session's work in progress as a deletion. Pruning on that guess would
    /// destroy their contribution, which is far worse than carrying a node one build out of date. A full
    /// build reconciles deletions, because it sees the whole tree rather than one path.
    /// </summary>
    [TestMethod]
    public void AFileThatIsNotHere_IsLeftAloneRatherThanPruned()
    {
        Write("public class C\n{\n    public void M() { }\n}\n");
        var (graph, cache) = Empty();
        GraphBuilder.RefreshFile(graph, cache, _root, Rel);

        File.Delete(Path.Combine(_root, "src", "Sample.cs"));

        Assert.IsFalse(GraphBuilder.RefreshFile(graph, cache, _root, Rel), "absence is not a change to record");
        CollectionAssert.Contains(Ids(graph), $"code:{Rel}#T:C",
            "another branch's file must survive a refresh run from a tree that does not have it");
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

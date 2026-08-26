using System.Collections.Generic;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// Finding declarations nothing reaches. The bar here is the opposite of the snaplink validator's: this one
/// is allowed to be wrong, and says so — but it must not be wrong for reasons it could have known about, so
/// the innocent explanations are what these mostly pin down.
/// </summary>
[TestClass]
[CoversNode("graph-orphans")]
public class GraphOrphanTests
{
    private static GraphNode Type(string file, string name) => new()
    {
        Id = $"code:{file}#T:{name}", Type = NodeType.Type, Label = name, FilePath = file,
        Metadata = new Dictionary<string, string> { ["ast"] = $"T:{name}", ["line"] = "1", ["kind"] = "class" },
    };

    private static GraphNode Member(string file, string type, string name) => new()
    {
        Id = $"code:{file}#T:{type}/M:{name}", Type = NodeType.Member, Label = name, FilePath = file,
        Metadata = new Dictionary<string, string> { ["ast"] = $"T:{type}/M:{name}", ["line"] = "2", ["kind"] = "method" },
    };

    private static GraphEdge Edge(string from, string to, string rel) =>
        new() { Source = from, Target = to, Relationship = rel };

    [TestMethod]
    public void AReachedTypeIsNotAnOrphan_AndAnUnreachedOneIs()
    {
        var g = new KnowledgeGraph
        {
            Nodes = [Type("src/A.cs", "Used"), Type("src/B.cs", "Unused")],
            Edges = [Edge("code:src/C.cs#T:Caller", "code:src/A.cs#T:Used", EdgeRelationship.Instantiates)],
        };

        var orphans = GraphQuery.Orphans(g).Select(o => o.Node.Label).ToList();
        CollectionAssert.AreEqual(new[] { "Unused" }, orphans);
    }

    [TestMethod]
    public void ContainmentIsNotAUse()
    {
        // Every member is contained by its type and every type by its file. Counting that would mean nothing
        // is ever an orphan, which is the trap this whole query has to avoid.
        var g = new KnowledgeGraph
        {
            Nodes = [Type("src/A.cs", "Lonely")],
            Edges = [Edge("file:src/A.cs", "code:src/A.cs#T:Lonely", EdgeRelationship.Contains)],
        };

        Assert.AreEqual(1, GraphQuery.Orphans(g).Count, "being contained by its own file is not being used");
    }

    [TestMethod]
    public void ATypeWhoseMemberIsCalled_IsReached()
    {
        // The static-utility case: `RepoFiles.EnumerateSource(...)` is an edge to the method, and nothing ever
        // names the class. Without rolling that up, every static class in the repo reads as dead.
        var g = new KnowledgeGraph
        {
            Nodes = [Type("src/A.cs", "Helpers"), Member("src/A.cs", "Helpers", "Do")],
            Edges =
            [
                Edge("code:src/A.cs#T:Helpers", "code:src/A.cs#T:Helpers/M:Do", EdgeRelationship.Contains),
                Edge("code:src/B.cs#T:Caller/M:Go", "code:src/A.cs#T:Helpers/M:Do", EdgeRelationship.Calls),
            ],
        };

        Assert.AreEqual(0, GraphQuery.Orphans(g).Count);
    }

    [TestMethod]
    public void SomethingOutsideTheSearchedPath_IsNotReported()
    {
        // A submodule is a whole third-party library; the parts of it this repo does not call are not findings.
        var g = new KnowledgeGraph { Nodes = [Type("external/lib/X.cs", "TheirType")] };

        Assert.AreEqual(0, GraphQuery.Orphans(g).Count, "src/ by default");
        Assert.AreEqual(1, GraphQuery.Orphans(g, under: "").Count, "and everything when asked");
    }

    [TestMethod]
    public void AnEnumIsReportedNow_BecauseAValueReferenceIsAnEdge()
    {
        // This used to be excused on the grounds that `Severity.Warning` left no edge. Type mentions changed
        // that, so an unreached enum is a real finding again — and the excuse had to go with the gap it named.
        var anEnum = Type("src/A.cs", "Severity");
        anEnum.Metadata!["kind"] = "enum";
        var g = new KnowledgeGraph { Nodes = [anEnum] };

        Assert.AreEqual(1, GraphQuery.Orphans(g).Count);
        Assert.IsNull(GraphQuery.Orphans(g).Single().Excuse);
    }

    [TestMethod]
    public void TwoDeclarationsSharingANameAreExcused()
    {
        // Core and Visuals.Common both declare InverseBoolToVisibilityConverter. Edges are name-resolved, so a
        // reference that could mean either is dropped — neither collects it, and neither is evidence of death.
        var g = new KnowledgeGraph { Nodes = [Type("src/A.cs", "Twin"), Type("src/B.cs", "Twin")] };

        Assert.AreEqual(0, GraphQuery.Orphans(g).Count);
        StringAssert.Contains(GraphQuery.Orphans(g, includeExcused: true).First().Excuse, "shares this name");
    }

    [TestMethod]
    public void AnImplementationOfARepoContractIsExcused_ButNotOfAForeignOne()
    {
        // Nothing names an IPageRegistration or an IThemeContribution: the shell scans assemblies for them. The
        // base has to be a declaration in this repo though, or implementing IDisposable would excuse everything.
        var g = new KnowledgeGraph
        {
            Nodes = [Type("src/I.cs", "IThing"), Type("src/A.cs", "Thing"), Type("src/B.cs", "Loner")],
            Edges =
            [
                Edge("code:src/A.cs#T:Thing", "code:src/I.cs#T:IThing", EdgeRelationship.Implements),
                Edge("code:src/B.cs#T:Loner", "external:IDisposable", EdgeRelationship.Implements),
            ],
        };

        var orphans = GraphQuery.Orphans(g).Select(o => o.Node.Label).ToList();
        CollectionAssert.DoesNotContain(orphans, "Thing");
        CollectionAssert.Contains(orphans, "Loner", "a framework interface excuses nothing");
    }

    [TestMethod]
    public void ALanguageTheExtractorDoesNotReadIsExcused()
    {
        // The syntax-highlighting corpus is .js/.py/.rb/.ts. No relation extractor runs on those, so no edge to
        // one can exist and its absence says nothing.
        var g = new KnowledgeGraph { Nodes = [Type("src/corpus/example.ts", "SquareConfig")] };

        Assert.AreEqual(0, GraphQuery.Orphans(g).Count);
        StringAssert.Contains(GraphQuery.Orphans(g, includeExcused: true).Single().Excuse, "does not read");
    }

    [TestMethod]
    public void ATestIsExcused_BecauseTheRunnerInvokesItByReflection()
    {
        var g = new KnowledgeGraph
        {
            Nodes = [Type("src/T.cs", "SomeTests")],
            HyperEdges =
            [
                new GraphHyperEdge
                {
                    Relationship = HyperRelationship.Annotated,
                    Endpoints =
                    [
                        new HyperEndpoint { Node = "code:src/T.cs#T:SomeTests", Role = EndpointRole.Target },
                        new HyperEndpoint { Node = "external:TestClass", Role = EndpointRole.Attr },
                    ],
                },
            ],
        };

        Assert.AreEqual(0, GraphQuery.Orphans(g).Count);
        StringAssert.Contains(GraphQuery.Orphans(g, includeExcused: true).Single().Excuse, "test");
    }

    [TestMethod]
    public void AMemberDeclaredOnAnInterface_IsExcused()
    {
        // The call goes through the interface, so the implementation has no direct caller.
        var g = new KnowledgeGraph
        {
            Nodes =
            [
                Type("src/I.cs", "IThing"), Member("src/I.cs", "IThing", "Run"),
                Type("src/A.cs", "Thing"), Member("src/A.cs", "Thing", "Run"),
            ],
            Edges =
            [
                Edge("code:src/I.cs#T:IThing", "code:src/I.cs#T:IThing/M:Run", EdgeRelationship.Contains),
                Edge("code:src/A.cs#T:Thing", "code:src/A.cs#T:Thing/M:Run", EdgeRelationship.Contains),
                Edge("code:src/A.cs#T:Thing", "code:src/I.cs#T:IThing", EdgeRelationship.Implements),
            ],
        };

        var members = GraphQuery.Orphans(g, NodeType.Member).Select(o => o.Node.Id).ToList();
        CollectionAssert.DoesNotContain(members, "code:src/A.cs#T:Thing/M:Run");
    }

    [TestMethod]
    public void AResourceKeyAnotherDictionaryAlsoDefines_IsExcused()
    {
        // Light and dark themes both declare AccentBrush; a reference resolves to one of them, and merge order
        // decides at runtime which actually wins. The other is not dead.
        GraphNode Key(string file) => new()
        {
            Id = $"code:{file}#K:AccentBrush", Type = NodeType.Type, Label = "AccentBrush", FilePath = file,
            Metadata = new Dictionary<string, string> { ["ast"] = "K:AccentBrush", ["line"] = "3" },
        };

        var g = new KnowledgeGraph
        {
            Nodes = [Key("src/Dark.xaml"), Key("src/Light.xaml")],
            Edges = [Edge("code:src/V.xaml#N:B", "code:src/Dark.xaml#K:AccentBrush", EdgeRelationship.UsesResource)],
        };

        Assert.AreEqual(0, GraphQuery.Orphans(g).Count, "the unreferenced twin is not a finding");
    }
}

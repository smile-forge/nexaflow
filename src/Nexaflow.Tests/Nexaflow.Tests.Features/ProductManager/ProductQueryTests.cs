using System.Linq;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// The read-only tree lookups behind the CLI's <c>find</c> / <c>describe</c> — the "where is feature X, and
/// where's its code/tests/docs" index.
/// </summary>
[TestClass]
[CoversNode("data-model")]
public class ProductQueryTests
{
    private static ProductState SampleTree() => new()
    {
        Nodes = new Dictionary<string, ProductNode>
        {
            ["features"] = new() { Title = "Features", Status = Status.Should, Children = ["tabular"] },
            ["tabular"]  = new() { Title = "Tabular", Status = Status.Done, Parent = "features", Children = ["row-count"] },
            ["row-count"] = new()
            {
                Title = "Row count", Status = Status.Done, Parent = "tabular",
                Description = "Footer shows the total number of rows.",
                Concerns = [new ConcernLink { Tag = "tests", Status = Status.Done,
                    Snaplinks = [new Snaplink { Type = "code", Doc = "src/Nexaflow.Tests/RowCountTests.cs", Class = "RowCountTests" }] }],
                Snaplinks =
                [
                    new Snaplink { Type = "code", Doc = "src/Tabular/RowCounter.cs", Class = "RowCounter", Method = "Count" },
                    new Snaplink { Type = "markdown", Doc = "docs/features.md", TitlePath = ["Tabular"] }
                ]
            }
        }
    };

    // ── find ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Find_MatchesTitle_CaseInsensitively()
    {
        var hits = ProductQuery.Find(SampleTree(), "row");
        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("row-count", hits[0].Id);
    }

    [TestMethod]
    public void Find_MatchesDescription_AndReturnsThePath()
    {
        var hits = ProductQuery.Find(SampleTree(), "footer");
        Assert.AreEqual(1, hits.Count);
        CollectionAssert.AreEqual(
            new[] { "Features", "Tabular", "Row count" },
            hits[0].Path.Select(c => c.Title).ToArray());
    }

    [TestMethod]
    public void Find_NoMatch_IsEmpty()
        => Assert.AreEqual(0, ProductQuery.Find(SampleTree(), "nonexistent-xyzzy").Count);

    // ── describe ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void Describe_UnknownId_ReturnsNull()
        => Assert.IsNull(ProductQuery.Describe(SampleTree(), "no-such-node"));

    [TestMethod]
    public void Describe_ReportsPath_Concerns_AndChildren()
    {
        var d = ProductQuery.Describe(SampleTree(), "tabular")!;
        Assert.AreEqual("Tabular", d.Title);
        CollectionAssert.AreEqual(new[] { "Features", "Tabular" }, d.Path.Select(c => c.Title).ToArray());
        Assert.IsTrue(d.Children.Any(c => c.Id == "row-count"));
    }

    [TestMethod]
    public void Describe_BucketsSnaplinks_IntoCode_Test_AndDoc()
    {
        var d = ProductQuery.Describe(SampleTree(), "row-count")!;
        var kinds = d.Snaplinks.Select(l => l.Kind).OrderBy(k => k).ToList();

        // node code + node doc + the concern's test snaplink
        CollectionAssert.AreEquivalent(new[] { "code", "doc", "test" }, kinds);
        Assert.IsTrue(d.Snaplinks.Single(l => l.Kind == "test").Display.Contains("(tests)"),
            "a concern's snaplink is annotated with the concern it satisfies");
    }

    // ── tree (outline) ─────────────────────────────────────────────────────────

    [TestMethod]
    public void Outline_UnknownId_ReturnsNull()
        => Assert.IsNull(ProductQuery.Outline(SampleTree(), "no-such-node"));

    [TestMethod]
    public void Outline_WalksTheSubtreeInTreeOrder_WithDepths()
    {
        var rows = ProductQuery.Outline(SampleTree(), "features")!;
        CollectionAssert.AreEqual(new[] { "features", "tabular", "row-count" }, rows.Select(r => r.Node.Id).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, rows.Select(r => r.Depth).ToArray());
    }

    [TestMethod]
    public void Outline_MaxDepth_CapsTheWalk_AndRootIsZero()
    {
        CollectionAssert.AreEqual(new[] { "features" },
            ProductQuery.Outline(SampleTree(), "features", maxDepth: 0)!.Select(r => r.Node.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "features", "tabular" },
            ProductQuery.Outline(SampleTree(), "features", maxDepth: 1)!.Select(r => r.Node.Id).ToArray());
    }

    [TestMethod]
    public void Outline_RowsCarryTheSameDetailAsDescribe()
    {
        var s = SampleTree();
        var row = ProductQuery.Outline(s, "features")!.Single(r => r.Node.Id == "row-count").Node;
        var described = ProductQuery.Describe(s, "row-count")!;
        Assert.AreEqual(described.Title, row.Title);
        Assert.AreEqual(described.Status, row.Status);
        CollectionAssert.AreEquivalent(
            described.Snaplinks.Select(l => l.Display).ToArray(),
            row.Snaplinks.Select(l => l.Display).ToArray());
    }

    [TestMethod]
    public void Outline_IsCycleSafe()
    {
        var s = SampleTree();
        s.Nodes["row-count"].Children = ["tabular"];   // malformed back-edge
        var rows = ProductQuery.Outline(s, "features")!;
        Assert.AreEqual(3, rows.Count, "each node is emitted once even when a child points back up");
    }

    // ── query ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Query_ByConcern_KeepsOnlyNodesCarryingIt_AndReportsItsStatusAndSnaplinks()
    {
        var hits = ProductQuery.Query(SampleTree(), concern: "tests");
        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("row-count", hits[0].Id);
        Assert.AreEqual(Status.Done, hits[0].ConcernStatus);
        Assert.AreEqual(1, hits[0].ConcernSnaplinks);
    }

    [TestMethod]
    public void Query_ConcernStatusFilter_SeparatesBackedFromUnbacked()
    {
        Assert.AreEqual(1, ProductQuery.Query(SampleTree(), concern: "tests", status: Status.Done).Count);
        Assert.AreEqual(0, ProductQuery.Query(SampleTree(), concern: "tests", status: Status.Should).Count,
            "the one node with a tests concern is done, so none are still 'should'");
    }

    [TestMethod]
    public void Query_Under_LimitsToTheSubtree()
    {
        var ids = ProductQuery.Query(SampleTree(), under: "tabular").Select(h => h.Id).ToArray();
        CollectionAssert.AreEquivalent(new[] { "tabular", "row-count" }, ids);   // itself + descendants, not "features"
    }

    [TestMethod]
    public void Query_LeafFilter_SplitsLeavesFromParents()
    {
        CollectionAssert.AreEqual(new[] { "row-count" },
            ProductQuery.Query(SampleTree(), leafOnly: true).Select(h => h.Id).ToArray());
        CollectionAssert.AreEquivalent(new[] { "features", "tabular" },
            ProductQuery.Query(SampleTree(), leafOnly: false).Select(h => h.Id).ToArray());
    }

    // ── derived (rolled-up) status — the CLI reports what the UI shows, not a parent's stored field ──

    [TestMethod]
    public void DisplayStatus_RollsUpAParent_NotItsStoredField()
    {
        var s = SampleTree();
        // 'features' is STORED 'should' but every descendant leaf is done, so the UI (and now find/describe/query)
        // shows it as done — a parent's stored field is meaningless and never displayed.
        Assert.AreEqual(Status.Should, s.Nodes["features"].Status, "precondition: the stored field is should");
        Assert.AreEqual(Status.Done, ProductQuery.DisplayStatus(s, "features"), "derived rolls up to done");
        Assert.AreEqual(Status.Done, ProductQuery.Describe(s, "features")!.Status);
        Assert.AreEqual(Status.Done, ProductQuery.Find(s, "features").Single(h => h.Id == "features").Status);
    }

    [TestMethod]
    public void Query_StatusFilter_UsesTheDerivedStatus_ForParents()
    {
        var s = SampleTree();
        // stored-should 'features' rolls up to done: a should-filter must NOT return it, a done-filter must.
        CollectionAssert.DoesNotContain(
            ProductQuery.Query(s, status: Status.Should).Select(h => h.Id).ToArray(), "features");
        CollectionAssert.Contains(
            ProductQuery.Query(s, status: Status.Done).Select(h => h.Id).ToArray(), "features");
    }
}

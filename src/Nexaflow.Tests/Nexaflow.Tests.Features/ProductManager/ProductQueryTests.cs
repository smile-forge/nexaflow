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
}

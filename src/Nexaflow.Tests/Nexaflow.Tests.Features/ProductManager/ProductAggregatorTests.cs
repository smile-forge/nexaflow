using Nexaflow.Features.ProductManager.Model;
using Nexaflow.Features.ProductManager.Services;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>
/// The derived survey logic: a parent rolls up the statuses of its descendant <em>leaves</em>; concern
/// coverage and the faulted "needs attention" list are computed the same way.
/// </summary>
[TestClass]
public class ProductAggregatorTests
{
    // a (should) → b (should) → c (done, tests:done) , d (faulted, note)
    // e (root, done)
    private static ProductState Fixture()
    {
        var s = new ProductState();
        s.Nodes["a"] = new ProductNode { Title = "A", Status = Status.Should, Children = ["b"] };
        s.Nodes["b"] = new ProductNode { Title = "B", Status = Status.Should, Parent = "a", Children = ["c", "d"] };
        s.Nodes["c"] = new ProductNode { Title = "C", Status = Status.Done, Parent = "b",
            Concerns = [new ConcernLink { Tag = "tests", Status = Status.Done }] };
        s.Nodes["d"] = new ProductNode { Title = "D", Status = Status.Faulted, Parent = "b", Note = "broke" };
        s.Nodes["e"] = new ProductNode { Title = "E", Status = Status.Done };
        return s;
    }

    [TestMethod]
    public void Leaves_AreDescendantsWithNoChildren()
    {
        CollectionAssert.AreEquivalent(new[] { "c", "d" }, ProductAggregator.Leaves(Fixture(), "a").ToArray());
        CollectionAssert.AreEqual(new[] { "c" }, ProductAggregator.Leaves(Fixture(), "c").ToArray()); // a leaf is its own leaf
    }

    [TestMethod]
    public void Roots_AreParentlessNodes()
    {
        CollectionAssert.AreEquivalent(new[] { "a", "e" }, ProductAggregator.Roots(Fixture()).ToArray());
    }

    [TestMethod]
    public void StatusBreakdown_CountsLeafStatuses()
    {
        var b = ProductAggregator.StatusBreakdown(Fixture(), "a");
        Assert.AreEqual(1, b.Done);       // c
        Assert.AreEqual(1, b.Faulted);    // d
        Assert.AreEqual(0, b.Should);
        Assert.AreEqual(0, b.Shouldnt);
        Assert.AreEqual(2, b.Total);
        Assert.AreEqual(50, b.Percent);
    }

    [TestMethod]
    public void ConcernBreakdown_CountsDoneLinksOverLeaves()
    {
        var (done, total) = ProductAggregator.ConcernBreakdown(Fixture(), "a", "tests");
        Assert.AreEqual(1, done);   // only c
        Assert.AreEqual(2, total);  // c, d
    }

    [TestMethod]
    public void NeedsAttention_IsExactlyFaultedDescendants()
    {
        var att = ProductAggregator.NeedsAttention(Fixture(), "a");
        Assert.AreEqual(1, att.Count);
        Assert.AreEqual("d", att[0].Id);
        Assert.AreEqual(Status.Faulted, att[0].Status);
        Assert.AreEqual("broke", att[0].Note);
    }

    [TestMethod]
    public void BreadcrumbPath_IsRootToNode()
    {
        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, ProductAggregator.BreadcrumbPath(Fixture(), "c").ToArray());
    }

    [TestMethod]
    public void RollupTolerates_Cycle()
    {
        var s = new ProductState();
        s.Nodes["x"] = new ProductNode { Title = "X", Children = ["y"] };
        s.Nodes["y"] = new ProductNode { Title = "Y", Parent = "x", Children = ["x"] };
        // must not stack-overflow
        Assert.IsNotNull(ProductAggregator.Leaves(s, "x"));
        Assert.IsNotNull(ProductAggregator.NeedsAttention(s, "x"));
        Assert.IsNotNull(ProductAggregator.BreadcrumbPath(s, "y"));
    }
}

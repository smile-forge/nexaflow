using System.Linq;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>Pure tree restructuring (promote / demote / re-parent) and its guards.</summary>
[TestClass]
public class ProductTreeOpsTests
{
    // root → a → a1, a2 ; root → b
    private static ProductState Sample() => new()
    {
        Product = new ProductDocument { Product = "P" },
        Nodes = new()
        {
            ["root"] = new ProductNode { Title = "root", Children = ["a", "b"] },
            ["a"]    = new ProductNode { Title = "a", Parent = "root", Children = ["a1", "a2"] },
            ["a1"]   = new ProductNode { Title = "a1", Parent = "a" },
            ["a2"]   = new ProductNode { Title = "a2", Parent = "a" },
            ["b"]    = new ProductNode { Title = "b", Parent = "root" },
        }
    };

    [TestMethod]
    [CoversNode("product-node-menu")]
    public void Promote_MovesNodeBesideItsParent()
    {
        var s = Sample();
        Assert.IsTrue(ProductTreeOps.Promote(s, "a1"));

        Assert.AreEqual("root", s.Nodes["a1"].Parent);
        CollectionAssert.DoesNotContain(s.Nodes["a"].Children, "a1");
        // placed right after its former parent "a"
        CollectionAssert.AreEqual(new[] { "a", "a1", "b" }, s.Nodes["root"].Children);
    }

    [TestMethod]
    [CoversNode("product-node-menu")]
    public void Promote_OnRootChild_MakesItTopLevel()
    {
        var s = Sample();
        Assert.IsTrue(ProductTreeOps.Promote(s, "a"));   // parent "root" has no parent → a becomes a root
        Assert.IsNull(s.Nodes["a"].Parent);
        CollectionAssert.DoesNotContain(s.Nodes["root"].Children, "a");
    }

    [TestMethod]
    [CoversNode("product-node-menu")]
    public void Promote_RejectedForRoot() => Assert.IsFalse(ProductTreeOps.Promote(Sample(), "root"));

    [TestMethod]
    [CoversNode("product-node-menu")]
    public void Demote_MovesUnderPreviousSibling()
    {
        var s = Sample();
        Assert.IsTrue(ProductTreeOps.Demote(s, "a2"));   // previous sibling is a1

        Assert.AreEqual("a1", s.Nodes["a2"].Parent);
        CollectionAssert.Contains(s.Nodes["a1"].Children, "a2");
        CollectionAssert.DoesNotContain(s.Nodes["a"].Children, "a2");
    }

    [TestMethod]
    [CoversNode("product-node-menu")]
    public void Demote_RejectedForFirstSibling() => Assert.IsFalse(ProductTreeOps.Demote(Sample(), "a1"));

    [TestMethod]
    [CoversNode("product-restructure")]
    public void Reparent_MovesAcrossBranches()
    {
        var s = Sample();
        Assert.IsTrue(ProductTreeOps.Reparent(s, "a1", "b"));

        Assert.AreEqual("b", s.Nodes["a1"].Parent);
        CollectionAssert.Contains(s.Nodes["b"].Children, "a1");
        CollectionAssert.DoesNotContain(s.Nodes["a"].Children, "a1");
    }

    [TestMethod]
    [CoversNode("product-restructure")]
    public void Reparent_ToTopLevel_WhenNull()
    {
        var s = Sample();
        Assert.IsTrue(ProductTreeOps.Reparent(s, "a1", null));
        Assert.IsNull(s.Nodes["a1"].Parent);
        CollectionAssert.DoesNotContain(s.Nodes["a"].Children, "a1");
    }

    [TestMethod]
    [CoversNode("product-restructure")]
    public void Reparent_RejectsCycle_OntoOwnDescendant()
    {
        var s = Sample();
        Assert.IsFalse(ProductTreeOps.Reparent(s, "a", "a1"));   // a1 is inside a's subtree
        Assert.AreEqual("root", s.Nodes["a"].Parent);           // unchanged
    }

    [TestMethod]
    [CoversNode("product-restructure")]
    public void Reparent_RejectsSelfAndNoOp()
    {
        var s = Sample();
        Assert.IsFalse(ProductTreeOps.Reparent(s, "a", "a"));     // onto self
        Assert.IsFalse(ProductTreeOps.Reparent(s, "a1", "a"));    // already a child of a
    }

    // ── Remove ──────────────────────────────────────────────────────────────
    [TestMethod]
    [CoversNode("product-restructure")]
    public void Remove_Leaf_UnlistsFromParentAndDeletes()
    {
        var s = Sample();
        var removed = ProductTreeOps.Remove(s, "a1", recursive: false);

        CollectionAssert.AreEqual(new[] { "a1" }, removed);
        Assert.IsFalse(s.Nodes.ContainsKey("a1"));
        CollectionAssert.DoesNotContain(s.Nodes["a"].Children, "a1");
        Assert.IsTrue(s.Nodes.ContainsKey("a2"), "a sibling is untouched");
    }

    [TestMethod]
    [CoversNode("product-restructure")]
    public void Remove_RejectsNodeWithChildren_WithoutRecursive()
    {
        var s = Sample();
        Assert.IsNull(ProductTreeOps.Remove(s, "a", recursive: false));
        Assert.IsTrue(s.Nodes.ContainsKey("a"), "nothing deleted when the guard trips");
    }

    [TestMethod]
    [CoversNode("product-restructure")]
    public void Remove_Recursive_DeletesWholeSubtree()
    {
        var s = Sample();
        var removed = ProductTreeOps.Remove(s, "a", recursive: true);

        CollectionAssert.AreEquivalent(new[] { "a", "a1", "a2" }, removed);
        Assert.IsFalse(s.Nodes.ContainsKey("a"));
        Assert.IsFalse(s.Nodes.ContainsKey("a1"));
        Assert.IsFalse(s.Nodes.ContainsKey("a2"));
        CollectionAssert.DoesNotContain(s.Nodes["root"].Children, "a");
    }

    [TestMethod]
    [CoversNode("product-restructure")]
    public void Remove_MissingNode_ReturnsNull()
        => Assert.IsNull(ProductTreeOps.Remove(Sample(), "nope", recursive: true));

    // ── Cascade status (sunburst "Status: …" menu) ──────────────────────────
    [TestMethod]
    [CoversNode("product-node-menu")]
    public void CascadeStatus_AdvancesOnlyShouldItems()
    {
        var s = new ProductState
        {
            Nodes = new()
            {
                ["p"] = new ProductNode { Title = "p", Children = ["l1", "l2", "l3"],
                    Concerns = [new ConcernLink { Tag = "x", Status = Status.Should }, new ConcernLink { Tag = "y", Status = Status.Shouldnt }] },
                ["l1"] = new ProductNode { Title = "l1", Parent = "p", Status = Status.Should },
                ["l2"] = new ProductNode { Title = "l2", Parent = "p", Status = Status.Shouldnt },
                ["l3"] = new ProductNode { Title = "l3", Parent = "p", Status = Status.Faulted,
                    Concerns = [new ConcernLink { Tag = "z", Status = Status.Should }] },
            }
        };

        ProductTreeOps.CascadeStatus(s, "p", Status.Done);

        Assert.AreEqual(Status.Done,     s.Nodes["l1"].Status);   // should → done
        Assert.AreEqual(Status.Shouldnt, s.Nodes["l2"].Status);   // shouldn't protected
        Assert.AreEqual(Status.Faulted,  s.Nodes["l3"].Status);   // faulted protected
        Assert.AreEqual(Status.Done,     s.Nodes["l3"].Concerns!.Single().Status);                // should concern → done
        Assert.AreEqual(Status.Done,     s.Nodes["p"].Concerns!.Single(c => c.Tag == "x").Status); // should concern → done
        Assert.AreEqual(Status.Shouldnt, s.Nodes["p"].Concerns!.Single(c => c.Tag == "y").Status); // shouldn't concern protected
    }

    [TestMethod]
    [CoversNode("product-node-menu")]
    public void CascadeStatus_ClickedLeafChangesRegardless()
    {
        var s = new ProductState { Nodes = new() { ["leaf"] = new ProductNode { Title = "leaf", Status = Status.Faulted } } };
        ProductTreeOps.CascadeStatus(s, "leaf", Status.Done);
        Assert.AreEqual(Status.Done, s.Nodes["leaf"].Status);   // a direct click overrides even a faulted node
    }

    [TestMethod]
    [CoversNode("product-node-menu")]
    public void CascadeStatus_ParentStoredStatusLeftAlone()
    {
        var s = new ProductState
        {
            Nodes = new()
            {
                ["p"] = new ProductNode { Title = "p", Status = Status.Should, Children = ["l"] },
                ["l"] = new ProductNode { Title = "l", Parent = "p", Status = Status.Should },
            }
        };
        ProductTreeOps.CascadeStatus(s, "p", Status.Done);
        Assert.AreEqual(Status.Should, s.Nodes["p"].Status);   // a parent's stored status is derived → untouched
        Assert.AreEqual(Status.Done,   s.Nodes["l"].Status);
    }

    // ── RepairChildren (doctor): reconcile children[] against the child→Parent back-references ──

    [TestMethod]
    [CoversNode("data-model")]
    public void RepairChildren_SplitsConcatenatedChildId_InOrder()
    {
        // The exact corruption a raw-JSON script produced: two child ids concatenated into one string.
        var s = new ProductState
        {
            Product = new ProductDocument { Product = "P" },
            Nodes = new()
            {
                ["wfs"]    = new ProductNode { Title = "wfs", Children = ["tabwfs-ai"] },
                ["tab"]    = new ProductNode { Title = "tab", Parent = "wfs" },
                ["wfs-ai"] = new ProductNode { Title = "ai",  Parent = "wfs" },
            }
        };

        var repairs = ProductTreeOps.RepairChildren(s, apply: true);

        Assert.AreEqual(1, repairs.Count);
        CollectionAssert.AreEqual(new[] { "tab", "wfs-ai" }, s.Nodes["wfs"].Children);   // split back, in order
        Assert.AreEqual(0, repairs[0].Dropped.Count);
    }

    [TestMethod]
    [CoversNode("data-model")]
    public void RepairChildren_ReattachesOrphan()
    {
        var s = new ProductState
        {
            Nodes = new()
            {
                ["p"] = new ProductNode { Title = "p", Children = [] },
                ["c"] = new ProductNode { Title = "c", Parent = "p" },   // names p as parent, but p doesn't list it
            }
        };

        ProductTreeOps.RepairChildren(s, apply: true);

        CollectionAssert.Contains(s.Nodes["p"].Children, "c");
    }

    [TestMethod]
    [CoversNode("data-model")]
    public void RepairChildren_NoOp_OnCleanTree() =>
        Assert.AreEqual(0, ProductTreeOps.RepairChildren(Sample(), apply: true).Count);

    [TestMethod]
    [CoversNode("data-model")]
    public void RepairChildren_DropsUnrecoverableDangling()
    {
        var s = new ProductState { Nodes = new() { ["p"] = new ProductNode { Title = "p", Children = ["ghost"] } } };

        var repairs = ProductTreeOps.RepairChildren(s, apply: true);

        Assert.AreEqual(1, repairs.Count);
        CollectionAssert.Contains(repairs[0].Dropped, "ghost");
        Assert.AreEqual(0, s.Nodes["p"].Children.Count);
    }

    // ── Concern / snaplink / field mutations (the CLI verbs' typed core) ──

    [TestMethod]
    [CoversNode("data-model")]
    public void SetConcern_AddsThenUpdates_WithoutDuplicating()
    {
        var s = new ProductState { Nodes = new() { ["n"] = new ProductNode { Title = "n" } } };

        Assert.IsTrue(ProductTreeOps.SetConcern(s, "n", "tests", Status.Should));
        Assert.AreEqual(Status.Should, s.Nodes["n"].Concerns!.Single(c => c.Tag == "tests").Status);

        ProductTreeOps.SetConcern(s, "n", "tests", Status.Done);
        Assert.AreEqual(Status.Done, s.Nodes["n"].Concerns!.Single(c => c.Tag == "tests").Status);
        Assert.AreEqual(1, s.Nodes["n"].Concerns!.Count(c => c.Tag == "tests"));   // updated in place

        Assert.IsFalse(ProductTreeOps.SetConcern(s, "missing", "tests", Status.Done));
    }

    [TestMethod]
    [CoversNode("data-model")]
    public void AddSnaplink_ToNode_AndToConcern()
    {
        var s = new ProductState { Nodes = new() { ["n"] = new ProductNode { Title = "n" } } };

        Assert.IsTrue(ProductTreeOps.AddSnaplink(s, "n", new Snaplink { Type = "code", Doc = "X.cs" }));
        Assert.AreEqual("X.cs", s.Nodes["n"].Snaplinks!.Single().Doc);

        ProductTreeOps.SetConcern(s, "n", "tests", Status.Done);
        Assert.IsTrue(ProductTreeOps.AddSnaplink(s, "n", new Snaplink { Type = "code", Doc = "T.cs" }, "tests"));
        Assert.AreEqual("T.cs", s.Nodes["n"].Concerns!.Single(c => c.Tag == "tests").Snaplinks!.Single().Doc);

        Assert.IsFalse(ProductTreeOps.AddSnaplink(s, "n", new Snaplink { Type = "url", Target = "u" }, "absent-concern"));
    }

    [TestMethod]
    [CoversNode("data-model")]
    public void EditNode_SetsAndClearsOptionalFields()
    {
        var s = new ProductState { Nodes = new() { ["n"] = new ProductNode { Title = "n" } } };

        ProductTreeOps.EditNode(s, "n", description: "d", note: "why");
        Assert.AreEqual("d", s.Nodes["n"].Description);
        Assert.AreEqual("why", s.Nodes["n"].Note);

        ProductTreeOps.EditNode(s, "n", description: "");   // empty string clears an optional field
        Assert.IsNull(s.Nodes["n"].Description);
        Assert.AreEqual("why", s.Nodes["n"].Note);          // null arg leaves it untouched
    }
}

using System.Collections.Generic;
using System.Linq;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Product;

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

    // ── Rename: change a node's id, retargeting everything keyed on it ───────

    [TestMethod]
    [CoversNode("product-restructure")]
    public void Rename_RetargetsParentEntry_ChildBackRefs_AndKeepsSiblingOrder()
    {
        var s = Sample();
        Assert.AreEqual(ProductTreeOps.RenameError.None, ProductTreeOps.Rename(s, "a", "alpha"));

        Assert.IsFalse(s.Nodes.ContainsKey("a"));
        Assert.IsTrue(s.Nodes.ContainsKey("alpha"));
        CollectionAssert.AreEqual(new[] { "alpha", "b" }, s.Nodes["root"].Children,
            "the parent's entry is replaced in position, not appended");
        Assert.AreEqual("alpha", s.Nodes["a1"].Parent);
        Assert.AreEqual("alpha", s.Nodes["a2"].Parent);
        CollectionAssert.AreEqual(new[] { "a1", "a2" }, s.Nodes["alpha"].Children, "its own children are unchanged");
    }

    [TestMethod]
    [CoversNode("product-restructure")]
    public void Rename_RetargetsNodeSnaplinks_OnNodesAndConcerns()
    {
        var s = Sample();
        s.Nodes["b"].Snaplinks = [new Snaplink { Type = "node", Target = "a" }];
        s.Nodes["a1"].Concerns = [new ConcernLink { Tag = "tests", Status = Status.Should,
            Snaplinks = [new Snaplink { Type = "node", Target = "a" }, new Snaplink { Type = "code", Doc = "a" }] }];

        ProductTreeOps.Rename(s, "a", "alpha");

        Assert.AreEqual("alpha", s.Nodes["b"].Snaplinks![0].Target);
        Assert.AreEqual("alpha", s.Nodes["a1"].Concerns![0].Snaplinks![0].Target);
        Assert.AreEqual("a", s.Nodes["a1"].Concerns![0].Snaplinks![1].Doc,
            "a code snaplink's doc path is not a node id — left alone");
    }

    [TestMethod]
    [CoversNode("product-restructure")]
    public void Rename_RejectsAnIdThatIsAlreadyTaken()
    {
        var s = Sample();
        Assert.AreEqual(ProductTreeOps.RenameError.IdTaken, ProductTreeOps.Rename(s, "a", "b"));
        Assert.IsTrue(s.Nodes.ContainsKey("a"), "nothing changed when the guard trips");
        CollectionAssert.AreEqual(new[] { "a", "b" }, s.Nodes["root"].Children);
    }

    [TestMethod]
    [CoversNode("product-restructure")]
    public void Rename_RejectsMissingNode_AndUnusableIds()
    {
        Assert.AreEqual(ProductTreeOps.RenameError.NoSuchNode, ProductTreeOps.Rename(Sample(), "nope", "x"));
        Assert.AreEqual(ProductTreeOps.RenameError.IdInvalid, ProductTreeOps.Rename(Sample(), "a", "a"));
        Assert.AreEqual(ProductTreeOps.RenameError.IdInvalid, ProductTreeOps.Rename(Sample(), "a", "  "));
        Assert.AreEqual(ProductTreeOps.RenameError.IdInvalid, ProductTreeOps.Rename(Sample(), "a", "two words"));
    }

    [TestMethod]
    [CoversNode("product-restructure")]
    public void Rename_ARootNode_HasNoParentToRetarget()
    {
        var s = Sample();
        Assert.AreEqual(ProductTreeOps.RenameError.None, ProductTreeOps.Rename(s, "root", "top"));
        Assert.AreEqual("top", s.Nodes["a"].Parent);
        Assert.AreEqual("top", s.Nodes["b"].Parent);
        Assert.IsNull(s.Nodes["top"].Parent);
    }

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
    public void RemoveConcern_DropsLink_AndNullsEmptyList()
    {
        var s = new ProductState { Nodes = new() { ["n"] = new ProductNode { Title = "n" } } };
        ProductTreeOps.SetConcern(s, "n", "tests", Status.Should);
        ProductTreeOps.SetConcern(s, "n", "AI Ready", Status.Should);

        Assert.IsTrue(ProductTreeOps.RemoveConcern(s, "n", "AI Ready"));
        Assert.IsFalse(s.Nodes["n"].Concerns!.Any(c => c.Tag == "AI Ready"));
        Assert.IsTrue(s.Nodes["n"].Concerns!.Any(c => c.Tag == "tests"), "the other concern is untouched");

        Assert.IsTrue(ProductTreeOps.RemoveConcern(s, "n", "tests"));
        Assert.IsNull(s.Nodes["n"].Concerns, "the list is nulled once its last concern goes");

        Assert.IsFalse(ProductTreeOps.RemoveConcern(s, "n", "tests"), "no concern left to remove");
    }

    [TestMethod]
    [CoversNode("data-model")]
    public void RemoveSnaplink_ByIndex_ThenClearAll()
    {
        var s = new ProductState
        {
            Nodes = new()
            {
                ["n"] = new ProductNode
                {
                    Title = "n",
                    Concerns = [new ConcernLink { Tag = "tests", Snaplinks =
                    [
                        new Snaplink { Type = "code", Doc = "A.cs", Class = "A" },
                        new Snaplink { Type = "code", Doc = "B.cs", Class = "B" },
                    ] }]
                }
            }
        };

        Assert.AreEqual(1, ProductTreeOps.RemoveSnaplink(s, "n", "tests", index: 0));
        Assert.AreEqual("B.cs", s.Nodes["n"].Concerns!.Single().Snaplinks!.Single().Doc);

        Assert.AreEqual(0, ProductTreeOps.RemoveSnaplink(s, "n", "tests", index: 9), "out-of-range removes nothing");

        Assert.AreEqual(1, ProductTreeOps.ClearSnaplinks(s, "n", "tests"), "clearing is its own verb");
        Assert.AreEqual(0, s.Nodes["n"].Concerns!.Single().Snaplinks!.Count);
    }

    /// <summary>Naming nothing removes nothing. It used to mean "all of them", so a caller whose matcher was
    /// mis-spelled — or built from options that were all absent — wiped the node's whole set with a call that
    /// looked exactly like the one that would have removed a single link.</summary>
    [TestMethod]
    [CoversNode("data-model")]
    public void RemoveSnaplink_NamingNoLink_RemovesNothing()
    {
        var s = WithLinks(new Snaplink { Type = "code", Doc = "A.cs", Class = "A" },
                          new Snaplink { Type = "code", Doc = "B.cs", Class = "B" });

        Assert.AreEqual(0, ProductTreeOps.RemoveSnaplink(s, "n", "tests"), "no index and no matcher");
        Assert.AreEqual(0, ProductTreeOps.RemoveSnaplink(s, "n", "tests", match: new SnaplinkFilter()),
            "an empty filter is 'no filter given', not 'match everything'");
        Assert.AreEqual(2, Links(s).Count, "the set is intact");

        Assert.AreEqual(2, ProductTreeOps.ClearSnaplinks(s, "n", "tests"), "the caller that means all says so");
        Assert.AreEqual(0, Links(s).Count);
    }

    [TestMethod]
    [CoversNode("data-model")]
    public void SetSnaplink_ClearsAFieldWithoutDisturbingTheRest()
    {
        // The case this exists for: a .xaml that turns out to be a ResourceDictionary declares no class, so
        // the class must go while the doc (and the link's position) stay.
        var s = WithLinks(new Snaplink { Type = "code", Doc = "Theme.xaml", Class = "Theme", Status = Status.Done });

        Assert.IsTrue(ProductTreeOps.SetSnaplink(s, "n", 0, "tests", clear: ["class"]));

        var link = Links(s).Single();
        Assert.IsNull(link.Class);
        Assert.AreEqual("Theme.xaml", link.Doc, "clearing one field must not disturb the others");
        Assert.AreEqual(Status.Done, link.Status);
    }

    [TestMethod]
    [CoversNode("data-model")]
    public void SetSnaplink_AssignsFieldsInPlace()
    {
        var s = WithLinks(new Snaplink { Type = "code", Doc = "View.xaml", Class = "Old" },
                          new Snaplink { Type = "code", Doc = "Other.cs", Class = "Other" });

        Assert.IsTrue(ProductTreeOps.SetSnaplink(s, "n", 0, "tests",
            set: l => { l.Class = "New"; l.Ast = "N:Root"; }));

        var links = Links(s);
        Assert.AreEqual("New", links[0].Class);
        Assert.AreEqual("N:Root", links[0].Ast);
        Assert.AreEqual("Other", links[1].Class, "the neighbouring link is untouched");
    }

    [TestMethod]
    [CoversNode("data-model")]
    public void SetSnaplink_RefusesAnIndexOrFieldItCannotHonour()
    {
        var s = WithLinks(new Snaplink { Type = "code", Doc = "A.cs", Class = "A" });

        Assert.IsFalse(ProductTreeOps.SetSnaplink(s, "n", 9, "tests", clear: ["class"]), "index out of range");
        Assert.IsFalse(ProductTreeOps.SetSnaplink(s, "n", 0, "nope", clear: ["class"]), "no such concern");
        Assert.IsFalse(ProductTreeOps.SetSnaplink(s, "missing", 0, "tests", clear: ["class"]), "no such node");
        Assert.IsFalse(ProductTreeOps.SetSnaplink(s, "n", 0, "tests", clear: ["nonsense"]), "unknown field name");
        Assert.AreEqual("A", Links(s).Single().Class, "a refused edit leaves the link alone");
    }

    /// <summary>
    /// The clear list is resolved before anything is assigned. It used to be walked as it was applied, so an
    /// unknown field name refused the call with the assignments already made — the caller was told nothing
    /// happened while half of it had, and the half that stuck was persisted by whatever wrote next.
    /// </summary>
    [TestMethod]
    [CoversNode("data-model")]
    public void SetSnaplink_WithAnUnknownClearField_AssignsNothingEither()
    {
        var s = WithLinks(new Snaplink { Type = "code", Doc = "A.cs", Class = "A", Method = "One" });

        Assert.IsFalse(ProductTreeOps.SetSnaplink(s, "n", 0, "tests",
            set: l => { l.Class = "B"; l.Doc = "B.cs"; }, clear: ["method", "nonsense"]));

        var link = Links(s).Single();
        Assert.AreEqual("A", link.Class, "the refused call assigned nothing");
        Assert.AreEqual("A.cs", link.Doc);
        Assert.AreEqual("One", link.Method, "and cleared nothing");
    }

    /// <summary>Three links in one file, one per method. An index would name whichever happens to sit at
    /// that position; the filter names the link itself, which is the only handle that survives a reorder.</summary>
    [TestMethod]
    [CoversNode("data-model")]
    public void RemoveSnaplink_ByFilter_TakesOnlyTheLinksThatAgreeWithEveryFieldGiven()
    {
        var s = WithLinks(
            new Snaplink { Type = "code", Doc = "tests/A.cs", Class = "A", Method = "One" },
            new Snaplink { Type = "code", Doc = "tests/A.cs", Class = "A", Method = "Two" },
            new Snaplink { Type = "code", Doc = "tests/A.cs", Class = "A" },
            new Snaplink { Type = "code", Doc = "tests/B.cs", Class = "B" });

        Assert.AreEqual(1, ProductTreeOps.RemoveSnaplink(s, "n", "tests",
            match: new SnaplinkFilter(Doc: "tests/A.cs", Method: "Two")));
        CollectionAssert.AreEqual(
            new[] { "One", null, null },
            Links(s).Select(l => l.Method).ToArray(),
            "only the link whose method matched went");

        // Doc alone is the broader handle: every remaining link in that file, class-only one included.
        Assert.AreEqual(2, ProductTreeOps.RemoveSnaplink(s, "n", "tests",
            match: new SnaplinkFilter(Doc: "tests/A.cs")));
        Assert.AreEqual("tests/B.cs", Links(s).Single().Doc);
    }

    /// <summary>A path pasted from Explorer or a Windows stack trace is backslashed and arbitrarily cased;
    /// matching nothing there would read as "already removed" and send the caller off to clear the list.</summary>
    [TestMethod]
    [CoversNode("data-model")]
    public void RemoveSnaplink_ByFilter_ComparesPathsSlashAndCaseInsensitively()
    {
        var s = WithLinks(new Snaplink { Type = "code", Doc = "src/Tests/A.cs", Class = "A" });

        Assert.AreEqual(1, ProductTreeOps.RemoveSnaplink(s, "n", "tests",
            match: new SnaplinkFilter(Doc: @"src\TESTS\A.cs")));
        Assert.AreEqual(0, Links(s).Count);
    }

    /// <summary>An index and an empty filter together still mean the index — the filter is absent, not a
    /// wildcard, so a caller that built one from options nobody passed does not lose its addressing.</summary>
    [TestMethod]
    [CoversNode("data-model")]
    public void RemoveSnaplink_WithAnEmptyFilter_StillHonoursTheIndex()
    {
        var s = WithLinks(
            new Snaplink { Type = "code", Doc = "A.cs", Class = "A" },
            new Snaplink { Type = "code", Doc = "B.cs", Class = "B" });

        Assert.AreEqual(1, ProductTreeOps.RemoveSnaplink(s, "n", "tests", index: 0, match: new SnaplinkFilter()));
        Assert.AreEqual("B.cs", Links(s).Single().Doc);
    }

    [TestMethod]
    [CoversNode("data-model")]
    public void RemoveSnaplink_ByFilter_ThatMatchesNothing_LeavesTheListAlone()
    {
        var s = WithLinks(new Snaplink { Type = "code", Doc = "A.cs", Class = "A" });

        Assert.AreEqual(0, ProductTreeOps.RemoveSnaplink(s, "n", "tests",
            match: new SnaplinkFilter(Class: "Absent")));
        Assert.AreEqual(1, Links(s).Count);
    }

    private static ProductState WithLinks(params Snaplink[] links) => new()
    {
        Nodes = new()
        {
            ["n"] = new ProductNode
            {
                Title = "n",
                Concerns = [new ConcernLink { Tag = "tests", Snaplinks = [.. links] }]
            }
        }
    };

    private static List<Snaplink> Links(ProductState s) => s.Nodes["n"].Concerns!.Single().Snaplinks!;

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

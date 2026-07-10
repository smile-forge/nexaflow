using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;

namespace Nexaflow.Tests.Features.ProductManager;

/// <summary>Rolled-up "effective" status shown on a sunburst arc: a leaf folds in its concerns; a parent
/// derives from its children (and their concerns), ignoring its own stored status. Precedence:
/// faulted → should → done → shouldnt.</summary>
[TestClass]
public class ProductRollupTests
{
    private static ProductState One(ProductNode node)
    {
        var s = new ProductState();
        s.Nodes["n"] = node;
        return s;
    }

    // ── Combine precedence ───────────────────────────────────────────────────
    [TestMethod] public void Combine_FaultedWins()  => Assert.AreEqual(Status.Faulted, ProductAggregator.Combine([Status.Done, Status.Should, Status.Faulted, Status.Shouldnt]));
    [TestMethod] public void Combine_ShouldOverDone() => Assert.AreEqual(Status.Should, ProductAggregator.Combine([Status.Done, Status.Should, Status.Shouldnt]));
    [TestMethod] public void Combine_DoneOverShouldnt() => Assert.AreEqual(Status.Done, ProductAggregator.Combine([Status.Shouldnt, Status.Done]));
    [TestMethod] public void Combine_AllShouldnt() => Assert.AreEqual(Status.Shouldnt, ProductAggregator.Combine([Status.Shouldnt, Status.Shouldnt]));
    [TestMethod] public void Combine_EmptyIsShouldnt() => Assert.AreEqual(Status.Shouldnt, ProductAggregator.Combine([]));

    // ── Leaf: own status + concerns ──────────────────────────────────────────
    [TestMethod]
    public void Leaf_CombinesOwnStatusWithConcerns()
    {
        var s = One(new ProductNode { Status = Status.Done, Concerns = [new ConcernLink { Tag = "t", Status = Status.Faulted }] });
        Assert.AreEqual(Status.Faulted, ProductAggregator.EffectiveStatus(s, "n"));
    }

    [TestMethod]
    public void Leaf_NoConcerns_IsOwnStatus()
        => Assert.AreEqual(Status.Done, ProductAggregator.EffectiveStatus(One(new ProductNode { Status = Status.Done }), "n"));

    // ── Parent: derived from children ────────────────────────────────────────
    private static ProductState Parent(params Status[] childStatuses)
    {
        var s = new ProductState();
        var kids = new List<string>();
        for (int i = 0; i < childStatuses.Length; i++)
        {
            var id = $"c{i}";
            kids.Add(id);
            s.Nodes[id] = new ProductNode { Status = childStatuses[i], Parent = "p" };
        }
        s.Nodes["p"] = new ProductNode { Status = Status.Shouldnt, Children = kids };   // stored status is a decoy
        return s;
    }

    [TestMethod] public void Parent_AnyFaultedChild_IsFaulted() => Assert.AreEqual(Status.Faulted, ProductAggregator.EffectiveStatus(Parent(Status.Done, Status.Faulted, Status.Should), "p"));
    [TestMethod] public void Parent_AnyShouldChild_IsShould()   => Assert.AreEqual(Status.Should,  ProductAggregator.EffectiveStatus(Parent(Status.Done, Status.Should, Status.Shouldnt), "p"));
    [TestMethod] public void Parent_AllShouldnt_IsShouldnt()    => Assert.AreEqual(Status.Shouldnt, ProductAggregator.EffectiveStatus(Parent(Status.Shouldnt, Status.Shouldnt), "p"));
    [TestMethod] public void Parent_ShouldntAndDone_IsDone()    => Assert.AreEqual(Status.Done,     ProductAggregator.EffectiveStatus(Parent(Status.Shouldnt, Status.Done), "p"));

    [TestMethod]
    public void Parent_IgnoresOwnStoredStatus()
    {
        var s = Parent(Status.Done, Status.Done);
        s.Nodes["p"].Status = Status.Faulted;   // stored faulted, but children are all done
        Assert.AreEqual(Status.Done, ProductAggregator.EffectiveStatus(s, "p"));
    }

    [TestMethod]
    public void Parent_ChildConcernCountsInRollup()
    {
        var s = Parent(Status.Done, Status.Done);
        s.Nodes["c0"].Concerns = [new ConcernLink { Tag = "t", Status = Status.Faulted }];
        Assert.AreEqual(Status.Faulted, ProductAggregator.EffectiveStatus(s, "p"));   // a child's concern is faulted
    }

    [TestMethod]
    public void Parent_OwnConcern_AffectsOwnStatusAndRollsUp()
    {
        // grand → p (concern faulted) → c0,c1 (both done)
        var s = Parent(Status.Done, Status.Done);
        s.Nodes["p"].Parent = "grand";
        s.Nodes["p"].Concerns = [new ConcernLink { Tag = "t", Status = Status.Faulted }];
        s.Nodes["grand"] = new ProductNode { Title = "grand", Children = ["p"] };

        Assert.AreEqual(Status.Faulted, ProductAggregator.EffectiveStatus(s, "p"));      // p's own concern counts for p
        Assert.AreEqual(Status.Faulted, ProductAggregator.EffectiveStatus(s, "grand")); // …and rolls up to grand
    }

    [TestMethod]
    public void DoneNode_PlusShouldConcern_BecomesShould()
    {
        // the reported case: a node marked done with a new "should" concern shows "should" on the sunburst arc
        var leaf = One(new ProductNode { Status = Status.Done, Concerns = [new ConcernLink { Tag = "new", Status = Status.Should }] });
        Assert.AreEqual(Status.Should, ProductAggregator.EffectiveStatus(leaf, "n"));

        var parent = Parent(Status.Done, Status.Done);   // both children done → parent done…
        parent.Nodes["p"].Concerns = [new ConcernLink { Tag = "new", Status = Status.Should }];
        Assert.AreEqual(Status.Should, ProductAggregator.EffectiveStatus(parent, "p"));  // …+ a should concern → should
    }

    // ── Detail pill: own concerns are shown separately, so the pill excludes them ─────
    [TestMethod]
    public void Pill_Leaf_IsRawStatus_IgnoringConcerns()
    {
        var leaf = One(new ProductNode { Status = Status.Done, Concerns = [new ConcernLink { Tag = "t", Status = Status.Faulted }] });
        Assert.AreEqual(Status.Faulted, ProductAggregator.EffectiveStatus(leaf, "n"));                 // arc bundles the concern
        Assert.AreEqual(Status.Done,    ProductAggregator.DerivedStatusExcludingConcerns(leaf, "n"));  // pill does not
    }

    [TestMethod]
    public void Pill_Parent_IsChildrenRollup_IgnoringOwnConcerns()
    {
        var s = Parent(Status.Done, Status.Done);
        s.Nodes["p"].Concerns = [new ConcernLink { Tag = "t", Status = Status.Should }];
        Assert.AreEqual(Status.Should, ProductAggregator.EffectiveStatus(s, "p"));                 // arc bundles own concern
        Assert.AreEqual(Status.Done,   ProductAggregator.DerivedStatusExcludingConcerns(s, "p"));  // pill = children only
    }

    // ── Breakdown + needs-attention use derived status ───────────────────────
    [TestMethod]
    public void StatusBreakdown_CountsLeafConcernAsFaulted()
    {
        var s = Parent(Status.Done, Status.Done);
        s.Nodes["c0"].Concerns = [new ConcernLink { Tag = "t", Status = Status.Faulted }];
        var b = ProductAggregator.StatusBreakdown(s, "p");
        Assert.AreEqual(1, b.Faulted);   // c0 (done + faulted concern) → faulted
        Assert.AreEqual(1, b.Done);
    }

    [TestMethod]
    public void IsLocallyFaulted_LeafStatusOrConcern_NotRollup()
    {
        // grand → p (no concern) → c0 (faulted), c1 (done)
        var s = Parent(Status.Faulted, Status.Done);
        s.Nodes["p"].Parent = "grand";
        s.Nodes["grand"] = new ProductNode { Title = "grand", Children = ["p"] };

        Assert.IsTrue(ProductAggregator.IsLocallyFaulted(s, "c0"));    // leaf, faulted
        Assert.IsFalse(ProductAggregator.IsLocallyFaulted(s, "c1"));   // leaf, done
        Assert.IsFalse(ProductAggregator.IsLocallyFaulted(s, "p"));    // faulted only by roll-up → not a source
        Assert.IsFalse(ProductAggregator.IsLocallyFaulted(s, "grand"));
    }

    [TestMethod]
    public void NeedsAttention_ListsFaultedSourcesNotAncestors()
    {
        var s = Parent(Status.Faulted, Status.Done);
        s.Nodes["p"].Parent = "grand";
        s.Nodes["grand"] = new ProductNode { Title = "grand", Children = ["p"] };

        var ids = ProductAggregator.NeedsAttention(s, "grand").Select(a => a.Id).ToArray();
        CollectionAssert.AreEquivalent(new[] { "c0" }, ids);   // just the faulted leaf, not p/grand
    }
}

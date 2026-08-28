using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Services.Initiatives.Product.Services;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Product;

/// <summary>
/// Snaplinks a branch has changed but not merged.
/// <para>
/// A node is a plan, and the shared tree is deliberately forward-looking about those. A snaplink is a claim
/// that a file exists and contains something — and from an unmerged branch that claim is true nowhere else,
/// which is why writing it to the shared tree made the main checkout report broken links for work nobody had
/// finished. These changes are recorded per branch instead, in a file committed alongside the code they
/// describe, so they arrive with the merge that makes them true.
/// </para>
/// </summary>
[TestClass]
[CoversNode("integrity-validate")]
public class PendingSnaplinksTests
{
    private string _root = "";

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "nexaflow-pending", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private static Snaplink Code(string cls) => new() { Type = "code", Doc = $"src/{cls}.cs", Class = cls };

    private static ProductState TreeWith(params Snaplink[] links) => new()
    {
        Product = new ProductDocument { Product = "P" },
        Nodes = new Dictionary<string, ProductNode>(StringComparer.Ordinal)
        {
            ["feature"] = new ProductNode
            {
                Title     = "Feature",
                Snaplinks = [.. links],
                Concerns  = [new ConcernLink { Tag = "tests", Snaplinks = [] }],
            },
        },
    };

    // ── The change set ──────────────────────────────────────────────────────

    [TestMethod]
    public void APromotedSetReplacesWhatTheSharedTreeHeld()
    {
        var pending = new PendingSnaplinks { Branch = "b" };
        pending.Capture("feature", null, [Code("A"), Code("B")]);

        var shared = TreeWith(Code("Old"));
        Assert.AreEqual(1, pending.ApplyTo(shared));

        CollectionAssert.AreEqual(new[] { "A", "B" },
            shared.Nodes["feature"].Snaplinks!.Select(l => l.Class).ToArray());
    }

    /// <summary>
    /// Whole sets, not per-link deltas: an empty set is how "I removed the last link" is said, and inferring
    /// removals from absence could not tell that from "I never touched this node".
    /// </summary>
    [TestMethod]
    public void AnEmptySetIsHowARemovalTravels()
    {
        var pending = new PendingSnaplinks { Branch = "b" };
        pending.Capture("feature", null, []);

        var shared = TreeWith(Code("Old"));
        pending.ApplyTo(shared);

        Assert.AreEqual(0, shared.Nodes["feature"].Snaplinks!.Count);
    }

    [TestMethod]
    public void ANodeTheBranchNeverTouched_IsLeftAlone()
    {
        var pending = new PendingSnaplinks { Branch = "b" };
        pending.Capture("feature", "tests", [Code("T")]);

        var shared = TreeWith(Code("Kept"));
        pending.ApplyTo(shared);

        CollectionAssert.AreEqual(new[] { "Kept" },
            shared.Nodes["feature"].Snaplinks!.Select(l => l.Class).ToArray(),
            "capturing a concern's links must not disturb the node's own");
        CollectionAssert.AreEqual(new[] { "T" },
            shared.Nodes["feature"].Concerns![0].Snaplinks!.Select(l => l.Class).ToArray());
    }

    /// <summary>Promoting links onto a node the shared tree does not have would be inventing the node.</summary>
    [TestMethod]
    public void LinksForANodeThatNeverMerged_AreSkipped()
    {
        var pending = new PendingSnaplinks { Branch = "b" };
        pending.Capture("not-in-main", null, [Code("A")]);

        Assert.AreEqual(0, pending.ApplyTo(TreeWith()));
    }

    [TestMethod]
    public void CapturingTwice_KeepsOnlyTheLatest()
    {
        var pending = new PendingSnaplinks { Branch = "b" };
        pending.Capture("feature", null, [Code("First")]);
        pending.Capture("feature", null, [Code("Second")]);

        var shared = TreeWith();
        pending.ApplyTo(shared);

        CollectionAssert.AreEqual(new[] { "Second" },
            shared.Nodes["feature"].Snaplinks!.Select(l => l.Class).ToArray());
    }

    // ── The file it travels in ──────────────────────────────────────────────

    /// <summary>
    /// Under the committed export dir, not the gitignored working state — that is what lets it ride along
    /// with the pull request and arrive in the main checkout at merge.
    /// </summary>
    [TestMethod]
    public void TheSetIsWrittenWhereGitWillCarryIt()
    {
        var store   = new PendingStore(_root);
        var pending = new PendingSnaplinks { Branch = "claude/my-feature" };
        pending.Capture("feature", null, [Code("A")]);

        store.Save(pending);

        var path = store.PathFor("claude/my-feature");
        Assert.IsTrue(File.Exists(path), path);
        StringAssert.Contains(path.Replace('\\', '/'), "docs/product/pending/");
        Assert.IsFalse(Path.GetFileName(path).Contains('/'), "a branch name must flatten into one file name");
    }

    [TestMethod]
    public void RoundTripsThroughTheFile()
    {
        var store   = new PendingStore(_root);
        var pending = new PendingSnaplinks { Branch = "b" };
        pending.Capture("feature", null, [Code("A")]);
        pending.Capture("feature", "tests", [Code("T")]);
        store.Save(pending);

        var loaded = store.Load("b");
        var shared = TreeWith();
        Assert.AreEqual(2, loaded.ApplyTo(shared));
        Assert.AreEqual("A", shared.Nodes["feature"].Snaplinks![0].Class);
        Assert.AreEqual("T", shared.Nodes["feature"].Concerns![0].Snaplinks![0].Class);
    }

    [TestMethod]
    public void SavingAnEmptySetRemovesTheFile()
    {
        var store   = new PendingStore(_root);
        var pending = new PendingSnaplinks { Branch = "b" };
        pending.Capture("feature", null, [Code("A")]);
        store.Save(pending);
        Assert.IsTrue(File.Exists(store.PathFor("b")));

        store.Save(new PendingSnaplinks { Branch = "b" });

        Assert.IsFalse(File.Exists(store.PathFor("b")),
            "a branch that no longer changes anything should not leave a file for someone to promote");
    }

    /// <summary>Presence in the main checkout is the merged signal — a branch's file can only arrive there
    /// by being merged, so consolidation needs no reverse lookup from PR to worktree to machine.</summary>
    [TestMethod]
    public void AllFindsEverySetThatHasArrived()
    {
        var store = new PendingStore(_root);
        foreach (var branch in new[] { "one", "two" })
        {
            var p = new PendingSnaplinks { Branch = branch };
            p.Capture("feature", null, [Code(branch)]);
            store.Save(p);
        }

        CollectionAssert.AreEquivalent(new[] { "one", "two" }, store.All().Select(p => p.Branch).ToArray());
    }

    [TestMethod]
    public void NothingPendingIsNotAnError()
    {
        var store = new PendingStore(_root);
        Assert.AreEqual(0, store.All().Count);
        Assert.IsTrue(store.Load("never-written").IsEmpty);
    }
}

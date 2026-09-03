using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Services.Initiatives.Graph;
using Nexaflow.Services.Initiatives.Graph.Model;
using Nexaflow.Services.Initiatives.Product.Model;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Initiatives.Graph;

/// <summary>
/// Finding what changed in the authored tree without looking at all of it.
/// <para>
/// Re-deriving the product layer wholesale costs seconds on a real tree, and nearly every edit touches one
/// node — so each node carries the hash of itself and everything under it, and the walk stops wherever a
/// subtree still agrees. Two properties have to hold for that to be safe, and both are asserted here: a
/// change must always reach the hashes of its ancestors (or the walk skips over it), and it must never
/// reach a branch it did not touch (or the saving evaporates).
/// </para>
/// </summary>
[TestClass]
[CoversNode("graph-build")]
public class ProductTreeDeltaTests
{
    /// <summary>root → (alpha → alpha-one, alpha-two), (beta → beta-one). Two branches, so "did the change
    /// stay in its own branch" is answerable.</summary>
    private static ProductState Tree() => new()
    {
        Product = new ProductDocument { Product = "Fixture" },
        Nodes = new Dictionary<string, ProductNode>(StringComparer.Ordinal)
        {
            ["root"]      = new() { Title = "Root",  Children = ["alpha", "beta"] },
            ["alpha"]     = new() { Title = "Alpha", Parent = "root", Children = ["alpha-one", "alpha-two"] },
            ["alpha-one"] = new() { Title = "Alpha One", Parent = "alpha", Children = [] },
            ["alpha-two"] = new() { Title = "Alpha Two", Parent = "alpha", Children = [] },
            ["beta"]      = new() { Title = "Beta",  Parent = "root", Children = ["beta-one"] },
            ["beta-one"]  = new() { Title = "Beta One", Parent = "beta", Children = [] },
        },
    };

    private static KnowledgeGraph Built(ProductState state)
    {
        var graph = GraphBuilder.Build(state, ".", new GraphBuildOptions());

        // A code node, to prove the delta never touches the expensive half.
        graph.Nodes.Add(new GraphNode
        {
            Id = "code:src/A.cs#T:A", Type = NodeType.Type, Label = "A", FilePath = "src/A.cs", Source = "src/A.cs",
        });
        return graph;
    }

    private static string? Label(KnowledgeGraph g, string id) =>
        g.Nodes.FirstOrDefault(n => n.Id == id)?.Label;

    // ── The hash ────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheSameTree_HashesTheSameWay()
    {
        CollectionAssert.AreEquivalent(ProductTreeHash.Compute(Tree()).ToList(),
                                       ProductTreeHash.Compute(Tree()).ToList());
    }

    /// <summary>The property the walk depends on: a change reaches every ancestor, so descending only where
    /// hashes differ can never step over it.</summary>
    [TestMethod]
    public void AChangedLeaf_ChangesItsAncestors()
    {
        var before = ProductTreeHash.Compute(Tree());

        var after = Tree();
        after.Nodes["alpha-one"].Title = "Alpha One, renamed";
        var hashes = ProductTreeHash.Compute(after);

        Assert.AreNotEqual(before["alpha-one"], hashes["alpha-one"]);
        Assert.AreNotEqual(before["alpha"],     hashes["alpha"],     "the parent folds its children in");
        Assert.AreNotEqual(before["root"],      hashes["root"],      "and so does the root");
    }

    /// <summary>The property the saving depends on: an untouched branch keeps its hash, so the walk stops
    /// there instead of descending into it.</summary>
    [TestMethod]
    public void AChangedLeaf_LeavesTheOtherBranchAlone()
    {
        var before = ProductTreeHash.Compute(Tree());

        var after = Tree();
        after.Nodes["alpha-one"].Title = "Alpha One, renamed";
        var hashes = ProductTreeHash.Compute(after);

        Assert.AreEqual(before["beta"],     hashes["beta"]);
        Assert.AreEqual(before["beta-one"], hashes["beta-one"]);
        Assert.AreEqual(before["alpha-two"], hashes["alpha-two"], "a sibling is not an ancestor");
    }

    /// <summary>The case a line diff of the file gets wrong: the node's own text is untouched and only its
    /// place changed. Both parents must notice.</summary>
    [TestMethod]
    public void AMovedNode_ChangesBothParents_EvenThoughItsOwnTextDidNot()
    {
        var before = ProductTreeHash.Compute(Tree());

        var after = Tree();
        after.Nodes["alpha"].Children.Remove("alpha-two");
        after.Nodes["beta"].Children.Add("alpha-two");
        after.Nodes["alpha-two"].Parent = "beta";
        var hashes = ProductTreeHash.Compute(after);

        Assert.AreNotEqual(before["alpha"], hashes["alpha"], "it lost a child");
        Assert.AreNotEqual(before["beta"],  hashes["beta"],  "it gained one");
    }

    [TestMethod]
    public void ASnaplinkAdded_ChangesThatNode()
    {
        var before = ProductTreeHash.Compute(Tree());

        var after = Tree();
        after.Nodes["beta-one"].Snaplinks = [new Snaplink { Type = "code", Doc = "src/B.cs", Class = "B" }];

        Assert.AreNotEqual(before["beta-one"], ProductTreeHash.Compute(after)["beta-one"]);
    }

    /// <summary>A concern's status is what the tree is mostly edited for, so it has to be in the hash or
    /// every set-concern goes unnoticed.</summary>
    [TestMethod]
    public void AConcernStatusChanged_ChangesThatNode()
    {
        var withConcern = Tree();
        withConcern.Nodes["beta-one"].Concerns = [new ConcernLink { Tag = "tests", Status = Status.Should }];
        var before = ProductTreeHash.Compute(withConcern);

        withConcern.Nodes["beta-one"].Concerns![0].Status = Status.Done;

        Assert.AreNotEqual(before["beta-one"], ProductTreeHash.Compute(withConcern)["beta-one"]);
    }

    // ── The delta ───────────────────────────────────────────────────────────

    [TestMethod]
    public void AnUnchangedTree_ChangesNothing_AndSaysSo()
    {
        var graph  = Built(Tree());
        var nodes  = graph.Nodes.Count;

        Assert.IsFalse(GraphBuilder.ApplyTreeDelta(graph, Tree(), "."),
                       "nothing moved, so there is nothing to write");
        Assert.AreEqual(nodes, graph.Nodes.Count);
    }

    [TestMethod]
    public void ARetitledNode_IsUpdated()
    {
        var graph = Built(Tree());

        var after = Tree();
        after.Nodes["alpha-one"].Title = "Alpha One, renamed";

        Assert.IsTrue(GraphBuilder.ApplyTreeDelta(graph, after, "."));
        Assert.AreEqual("Alpha One, renamed", Label(graph, "product:alpha-one"));
        Assert.AreEqual("Beta One",           Label(graph, "product:beta-one"), "the other branch is untouched");
    }

    [TestMethod]
    public void ANodeAddedToTheTree_AppearsWithItsEdge()
    {
        var graph = Built(Tree());

        var after = Tree();
        after.Nodes["beta-two"] = new ProductNode { Title = "Beta Two", Parent = "beta", Children = [] };
        after.Nodes["beta"].Children.Add("beta-two");

        Assert.IsTrue(GraphBuilder.ApplyTreeDelta(graph, after, "."));
        Assert.AreEqual("Beta Two", Label(graph, "product:beta-two"));
        Assert.IsTrue(graph.Edges.Any(e => e.Source == "product:beta" && e.Target == "product:beta-two"),
                      "its parent should contain it");
    }

    /// <summary>Left behind, a removed node is an orphan that search and walks still return.</summary>
    [TestMethod]
    public void ANodeRemovedFromTheTree_LeavesTheGraph()
    {
        var graph = Built(Tree());

        var after = Tree();
        after.Nodes.Remove("beta-one");
        after.Nodes["beta"].Children.Remove("beta-one");

        Assert.IsTrue(GraphBuilder.ApplyTreeDelta(graph, after, "."));
        Assert.IsNull(Label(graph, "product:beta-one"));
        Assert.IsFalse(graph.Edges.Any(e => e.Target == "product:beta-one"), "and its edges with it");
    }

    /// <summary>The point of the whole exercise: the code half is the expensive half and a tree edit must
    /// not disturb it.</summary>
    [TestMethod]
    public void CodeNodes_AreNeverTouched()
    {
        var graph = Built(Tree());

        var after = Tree();
        after.Nodes["alpha-one"].Title = "Alpha One, renamed";
        GraphBuilder.ApplyTreeDelta(graph, after, ".");

        Assert.AreEqual("A", Label(graph, "code:src/A.cs#T:A"));
    }

    /// <summary>A second delta over the same tree must be a no-op — otherwise the stamps written by the
    /// first one are wrong and every subsequent change re-does the whole thing.</summary>
    [TestMethod]
    public void ApplyingTwice_DoesNothingTheSecondTime()
    {
        var graph = Built(Tree());

        var after = Tree();
        after.Nodes["alpha-one"].Title = "Alpha One, renamed";

        Assert.IsTrue(GraphBuilder.ApplyTreeDelta(graph, after, "."));
        Assert.IsFalse(GraphBuilder.ApplyTreeDelta(graph, after, "."),
                       "the delta must record what it did, or it will do it again forever");
    }
}

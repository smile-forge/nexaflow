using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Graphs;
using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using Nexaflow.Visuals.Text.Markdown.Graphs.Layout;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Tests.Core.Unit.Markdown;

/// <summary>
/// Deriving the visible graph from a parsed one plus an expansion state — the model half of
/// expandable nodes. WPF-free: what a chip does is decided here, drawing it is the renderer's job.
/// </summary>
[TestClass]
[CoversNode("graph-expandable-nodes")]
public class GraphExpansionTests
{
    /// <summary>A root with <paramref name="width"/> children, each with one grandchild.</summary>
    private static Graph Tree(int width)
    {
        var g = new Graph();
        g.GetOrAdd("root");
        for (int i = 0; i < width; i++)
        {
            g.AddEdge("root", $"c{i}");
            g.AddEdge($"c{i}", $"g{i}");
        }
        return g;
    }

    private static NodeExpansion StateOf(Graph g, string id) =>
        g.FindNode(id)?.Expansion ?? NodeExpansion.Leaf;

    // ── Doing nothing ─────────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void An_ordinary_graph_keeps_every_node_and_grows_no_chips()
    {
        var source = new MermaidParser().Parse("graph TD\n  a --> b\n  b --> c\n");
        var view   = GraphExpansion.Apply(source, new NexaflowGraphConfig());

        Assert.AreEqual(3, view.Nodes.Count);
        Assert.AreEqual(2, view.Edges.Count);
        Assert.IsTrue(view.Nodes.All(n => n.Expansion == NodeExpansion.Leaf),
            "A diagram that never mentions expansion must not sprout affordances.");
    }

    [TestMethod, TestCategory("Unit")]
    public void The_parsed_graph_is_never_touched_so_it_can_be_laid_out_again()
    {
        // Layout flips IsReversed on the edges it is handed; if that were the parsed graph, a second
        // render at a new width would come out with its arrows turned round.
        var source = new Graph();
        source.AddEdge("a", "b");
        source.AddEdge("b", "a");

        for (int i = 0; i < 3; i++)
            SugiyamaLayout.Compute(GraphExpansion.Apply(source, new NexaflowGraphConfig()));

        Assert.IsTrue(source.Edges.All(e => !e.IsReversed), "The source graph must survive a render.");
    }

    // ── expandDepth ───────────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void ExpandDepth_hides_what_is_past_the_frontier_and_marks_who_holds_it()
    {
        var view = GraphExpansion.Apply(Tree(2), new NexaflowGraphConfig { ExpandDepth = 1 });

        Assert.AreEqual(3, view.Nodes.Count, "root + its two children; the grandchildren stay behind.");
        Assert.AreEqual(NodeExpansion.Expanded,  StateOf(view, "root"));
        Assert.AreEqual(NodeExpansion.Collapsed, StateOf(view, "c0"));
        Assert.AreEqual(1, view.FindNode("c0")!.HiddenCount, "The chip can say how much is behind it.");
        Assert.IsNull(view.FindNode("g0"));
    }

    [TestMethod, TestCategory("Unit")]
    public void ExpandDepth_zero_shows_only_the_roots()
    {
        var view = GraphExpansion.Apply(Tree(3), new NexaflowGraphConfig { ExpandDepth = 0 });

        Assert.AreEqual(1, view.Nodes.Count);
        Assert.AreEqual(NodeExpansion.Collapsed, StateOf(view, "root"));
        Assert.AreEqual(6, view.FindNode("root")!.HiddenCount);
        Assert.AreEqual(0, view.Edges.Count, "An edge to a hidden node is not drawn to nowhere.");
    }

    [TestMethod, TestCategory("Unit")]
    public void Opening_one_node_reveals_only_that_subtree()
    {
        var cfg  = new NexaflowGraphConfig { ExpandDepth = 1 };
        var view = GraphExpansion.Apply(Tree(2), cfg, new Dictionary<string, bool> { ["c0"] = true });

        Assert.IsNotNull(view.FindNode("g0"), "The opened node's child is now shown.");
        Assert.IsNull(view.FindNode("g1"), "Its sibling stays closed.");
        Assert.AreEqual(NodeExpansion.Expanded,  StateOf(view, "c0"));
        Assert.AreEqual(NodeExpansion.Collapsed, StateOf(view, "c1"));
    }

    [TestMethod, TestCategory("Unit")]
    public void The_reader_can_close_what_the_source_declared_open()
    {
        var cfg = new NexaflowGraphConfig { ExpandDepth = 9 };
        var view = GraphExpansion.Apply(Tree(1), cfg, new Dictionary<string, bool> { ["c0"] = false });

        Assert.IsNull(view.FindNode("g0"));
        Assert.AreEqual(NodeExpansion.Collapsed, StateOf(view, "c0"));
    }

    // ── Declared marks (a generated diagram) ──────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void A_declared_collapsed_node_gets_a_chip_even_with_nothing_behind_it_in_the_source()
    {
        // How a generated diagram works: the producer knows there is more to walk, but has not
        // walked it, so the subtree is not in the source at all.
        var g = new Graph();
        g.AddEdge("root", "lib");

        var cfg = new NexaflowGraphConfig();
        cfg.Expanded["root"] = "app.exe";
        cfg.Collapsed["lib"] = "KERNEL32.dll";

        var view = GraphExpansion.Apply(g, cfg);

        Assert.AreEqual(NodeExpansion.Collapsed, StateOf(view, "lib"));
        Assert.AreEqual(NodeExpansion.Expanded,  StateOf(view, "root"));
        Assert.AreEqual("KERNEL32.dll", view.FindNode("lib")!.ExpandKey,
            "The producer's own name comes back on the request, not the mermaid id.");
    }

    [TestMethod, TestCategory("Unit")]
    public void A_node_nobody_declared_stays_a_leaf_even_beside_ones_that_did()
    {
        var g = new Graph();
        g.AddEdge("root", "lib");
        g.AddEdge("root", "plain");
        g.AddEdge("plain", "under");

        var cfg = new NexaflowGraphConfig();
        cfg.Collapsed["lib"] = "lib";

        var view = GraphExpansion.Apply(g, cfg);

        Assert.AreEqual(NodeExpansion.Collapsed, StateOf(view, "lib"));
        Assert.AreEqual(NodeExpansion.Leaf, StateOf(view, "plain"),
            "An undeclared parent is not offered a collapse it never asked for.");
        Assert.IsNotNull(view.FindNode("under"));
    }

    // ── maxFanOut ─────────────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void MaxFanOut_folds_the_surplus_siblings_behind_one_countable_chip()
    {
        var g = new Graph();
        for (int i = 0; i < 40; i++) g.AddEdge("root", $"c{i}");

        var view = GraphExpansion.Apply(g, new NexaflowGraphConfig { MaxFanOut = 10 });

        var overflow = view.Nodes.SingleOrDefault(n => n.Id == GraphExpansion.OverflowPrefix + "root");
        Assert.IsNotNull(overflow, "The surplus siblings fold behind a stand-in.");
        Assert.AreEqual(NodeExpansion.Collapsed, overflow!.Expansion);
        Assert.AreEqual(31, overflow.HiddenCount);
        Assert.AreEqual("+31 more", overflow.Label);

        // The stand-in counts against the cap: "no more than ten things hanging off this node" is
        // the promise, and eleven would break it.
        Assert.AreEqual(9, view.Nodes.Count(n => n.Id.StartsWith("c")));
        Assert.AreEqual(10, view.Edges.Count(e => e.SourceId == "root"));
        Assert.AreEqual(view.Nodes.Count - 1, view.Edges.Count,
            "Every drawn node is still attached, the stand-in included.");
    }

    [TestMethod, TestCategory("Unit")]
    public void Opening_the_overflow_chip_shows_every_sibling()
    {
        var g = new Graph();
        for (int i = 0; i < 40; i++) g.AddEdge("root", $"c{i}");

        var view = GraphExpansion.Apply(g, new NexaflowGraphConfig { MaxFanOut = 10 },
            new Dictionary<string, bool> { [GraphExpansion.OverflowPrefix + "root"] = true });

        Assert.AreEqual(41, view.Nodes.Count);
        Assert.IsFalse(view.Nodes.Any(n => n.Id.StartsWith(GraphExpansion.OverflowPrefix)));
    }

    [TestMethod, TestCategory("Unit")]
    public void A_shared_child_is_never_folded_away_from_its_other_parent()
    {
        var g = new Graph();
        for (int i = 0; i < 30; i++) g.AddEdge("root", $"c{i}");
        g.AddEdge("other", "c29");   // c29 belongs to two parents

        var view = GraphExpansion.Apply(g, new NexaflowGraphConfig { MaxFanOut = 5 });

        Assert.IsNotNull(view.FindNode("c29"), "Folding it would silently drop the other parent's edge.");
        Assert.IsTrue(view.Edges.Any(e => e.SourceId == "other" && e.TargetId == "c29"));
    }

    [TestMethod, TestCategory("Unit")]
    public void A_fan_out_within_the_cap_is_left_alone()
    {
        var g = new Graph();
        for (int i = 0; i < 6; i++) g.AddEdge("root", $"c{i}");

        var view = GraphExpansion.Apply(g, new NexaflowGraphConfig { MaxFanOut = 10 });

        Assert.AreEqual(7, view.Nodes.Count);
        Assert.IsFalse(view.Nodes.Any(n => n.Id.StartsWith(GraphExpansion.OverflowPrefix)));
    }

    // ── Awkward shapes ────────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void A_cycle_with_no_root_still_renders_rather_than_vanishing()
    {
        var g = new Graph();
        g.AddEdge("a", "b");
        g.AddEdge("b", "a");

        var view = GraphExpansion.Apply(g, new NexaflowGraphConfig { ExpandDepth = 1 });

        Assert.AreEqual(2, view.Nodes.Count, "With nothing to start from, every node is a root.");
    }

    [TestMethod, TestCategory("Unit")]
    public void A_subgraph_keeps_only_the_members_that_are_still_shown()
    {
        var g = Tree(2);
        var sub = new Subgraph { Id = "box", Label = "Box" };
        sub.NodeIds.AddRange(["c0", "g0"]);
        g.Subgraphs.Add(sub);

        var view = GraphExpansion.Apply(g, new NexaflowGraphConfig { ExpandDepth = 1 });

        var kept = view.Subgraphs.Single();
        CollectionAssert.AreEqual(new[] { "c0" }, kept.NodeIds.ToArray());
    }
}

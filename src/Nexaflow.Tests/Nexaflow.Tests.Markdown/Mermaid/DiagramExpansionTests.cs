using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Tests.Markdown.Mermaid;

/// <summary>
/// How much of a graph-shaped diagram is drawn: the front matter read, the frontier walked, and the chips worked out.
/// WPF-free — what a chip means is decided here, and drawing it is the builder's job.
/// </summary>
[TestClass]
[CoversNode("graph-expandable-nodes")]
public class DiagramExpansionTests
{
    /// <summary>A root with <paramref name="width"/> children, each with one grandchild.</summary>
    private static DiagramChart Tree(int width)
    {
        var ids = new List<string> { "root" };
        var edges = new List<(string From, string To)>();

        for (var at = 0; at < width; at++)
        {
            ids.Add($"c{at}");
            ids.Add($"g{at}");
            edges.Add(("root", $"c{at}"));
            edges.Add(($"c{at}", $"g{at}"));
        }

        return new DiagramChart(ids, edges);
    }

    /// <summary>The front matter a block would carry, as the reader is handed it.</summary>
    private static NexaflowConfig Asked(string nexaflow) =>
        NexaflowConfig.Read("config:\n  nexaflow:\n" + nexaflow);

    // ── Doing nothing ───────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void A_diagram_that_never_mentions_folding_draws_every_node_and_grows_no_chips()
    {
        var folding = DiagramExpansion.Of(NexaflowConfig.None, Tree(2));

        Assert.IsTrue(folding.IsEmpty, "nothing asked for anything, so nothing is worked out");
        Assert.IsTrue(Tree(2).Ids.All(folding.Draws));
        Assert.IsTrue(Tree(2).Ids.All(id => folding.FoldOf(id) is null),
                      "a diagram that never mentions folding must not sprout affordances");
    }

    [TestMethod, TestCategory("Unit")]
    public void Nothing_in_the_front_matter_means_nothing_to_read()
    {
        Assert.IsTrue(NexaflowConfig.Read(null).IsEmpty);
        Assert.IsTrue(NexaflowConfig.Read("config:\n  flowchart:\n    curve: linear\n").IsEmpty,
                      "a key outside the namespace is not mistaken for one inside it");
    }

    // ── The frontier ────────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void DefaultExpansion_folds_away_what_is_past_the_frontier_and_says_who_holds_it()
    {
        var folding = DiagramExpansion.Of(Asked("    defaultExpansion: 1\n"), Tree(2));

        Assert.IsTrue(folding.Draws("root"));
        Assert.IsTrue(folding.Draws("c0"), "a child is one level down, which is inside the frontier");
        Assert.IsFalse(folding.Draws("g0"), "a grandchild is past it");

        Assert.AreEqual(new DiagramFold(Open: true, Hidden: 0), folding.FoldOf("root"));
        Assert.AreEqual(new DiagramFold(Open: false, Hidden: 1), folding.FoldOf("c0"),
                        "the child is shut, and says how much is behind it");
    }

    [TestMethod, TestCategory("Unit")]
    public void DefaultExpansion_of_nought_draws_the_roots_alone()
    {
        var folding = DiagramExpansion.Of(Asked("    defaultExpansion: 0\n"), Tree(2));

        Assert.IsTrue(folding.Draws("root"));
        Assert.IsFalse(folding.Draws("c0"));
        Assert.IsFalse(folding.Draws("g0"));
        Assert.AreEqual(new DiagramFold(Open: false, Hidden: 4), folding.FoldOf("root"));
    }

    [TestMethod, TestCategory("Unit")]
    public void ExpandDepth_says_the_same_thing_as_defaultExpansion()
    {
        var folding = DiagramExpansion.Of(Asked("    expandDepth: 1\n"), Tree(1));

        Assert.IsTrue(folding.Draws("c0"));
        Assert.IsFalse(folding.Draws("g0"), "every diagram the Executable feature writes says expandDepth");
    }

    // ── What the reader has done since ──────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void Opening_one_node_reveals_only_what_is_behind_that_one()
    {
        var folding = DiagramExpansion.Of(Asked("    defaultExpansion: 1\n"), Tree(2),
                                          new Dictionary<string, bool> { ["c0"] = true });

        Assert.IsTrue(folding.Draws("g0"), "the one that was opened");
        Assert.IsFalse(folding.Draws("g1"), "and no other");
    }

    [TestMethod, TestCategory("Unit")]
    public void The_reader_can_fold_away_what_the_source_declared_open()
    {
        var folding = DiagramExpansion.Of(Asked("    expanded:\n      root: app.exe\n"), Tree(1),
                                          new Dictionary<string, bool> { ["app.exe"] = false });

        Assert.IsFalse(folding.Draws("c0"), "what the reader folded wins over what the source declared");
        Assert.AreEqual(new DiagramFold(Open: false, Hidden: 2), folding.FoldOf("root"));
    }

    [TestMethod, TestCategory("Unit")]
    public void What_the_reader_opens_is_remembered_by_the_producers_own_name_not_the_id()
    {
        var chart = Tree(1);
        var config = Asked("    collapsed:\n      c0: KERNEL32.dll\n");

        Assert.AreEqual("KERNEL32.dll", config.KeyFor("c0"));
        Assert.AreEqual("root", config.KeyFor("root"), "a node nobody named is known by its id");

        var folding = DiagramExpansion.Of(config, chart, new Dictionary<string, bool> { ["KERNEL32.dll"] = true });
        Assert.IsTrue(folding.Draws("g0"), "opened under the name the host thinks in");
    }

    // ── Chips only where something asked for them ───────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void A_node_declared_folded_gets_a_chip_even_with_nothing_behind_it_in_the_source()
    {
        var chart = new DiagramChart(["a", "b"], [("a", "b")]);
        var folding = DiagramExpansion.Of(Asked("    collapsed:\n      - b\n"), chart);

        Assert.IsTrue(folding.Draws("b"));
        Assert.AreEqual(new DiagramFold(Open: false, Hidden: 0), folding.FoldOf("b"),
                        "the subtree is the host's, not the source's — the chip is how it is asked for");
    }

    [TestMethod, TestCategory("Unit")]
    public void A_node_nobody_declared_stays_a_leaf_beside_ones_that_did()
    {
        var chart = new DiagramChart(["a", "b", "c"], [("a", "b"), ("a", "c")]);
        var folding = DiagramExpansion.Of(Asked("    collapsed:\n      - b\n"), chart);

        Assert.IsNotNull(folding.FoldOf("b"));
        Assert.IsNull(folding.FoldOf("c"), "nothing spoke about it, so it earned no chip");
        Assert.IsNull(folding.FoldOf("a"), "and neither did its parent");
    }

    // ── Awkward shapes ──────────────────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void A_cycle_with_no_root_is_still_drawn_rather_than_vanishing()
    {
        var chart = new DiagramChart(["a", "b", "c"], [("a", "b"), ("b", "c"), ("c", "a")]);
        var folding = DiagramExpansion.Of(Asked("    defaultExpansion: 1\n"), chart);

        Assert.IsTrue(chart.Ids.All(folding.Draws), "nothing points at nothing, so every node is a root");
    }

    [TestMethod, TestCategory("Unit")]
    public void A_node_that_points_at_itself_is_a_leaf_and_folds_nothing()
    {
        var chart = new DiagramChart(["a"], [("a", "a")]);
        var folding = DiagramExpansion.Of(Asked("    defaultExpansion: 0\n"), chart);

        Assert.IsTrue(folding.Draws("a"));
        Assert.IsNull(folding.FoldOf("a"), "a self-loop is not a subtree, so there is nothing to fold away");
    }

    [TestMethod, TestCategory("Unit")]
    public void A_node_reached_several_ways_is_as_deep_as_its_shortest_way_in()
    {
        var chart = new DiagramChart(["a", "b", "c"], [("a", "b"), ("b", "c"), ("a", "c")]);
        var folding = DiagramExpansion.Of(Asked("    defaultExpansion: 1\n"), chart);

        Assert.IsTrue(folding.Draws("c"), "one step from the root by the short way, so inside a frontier of one");
    }

    // ── Only what is a tree folds ───────────────────────────────────────────

    [TestMethod, TestCategory("Unit")]
    public void A_flat_diagram_has_nothing_to_fold()
    {
        // Folding is a fact about a tree: without one, every node is a leaf and no chip has anything to say.
        var chart = new DiagramChart(["a", "b", "c"], []);
        var folding = DiagramExpansion.Of(Asked("    defaultExpansion: 0\n"), chart);

        Assert.IsTrue(chart.Ids.All(folding.Draws), "nothing points at anything, so every node is a root");
        Assert.IsTrue(chart.Ids.All(id => folding.FoldOf(id) is null));
    }

    [TestMethod, TestCategory("Unit")]
    public void Only_a_node_with_something_under_it_carries_a_chip()
    {
        var folding = DiagramExpansion.Of(Asked("    defaultExpansion: 1\n"), Tree(2));

        Assert.IsNotNull(folding.FoldOf("root"), "it holds the children");
        Assert.IsNotNull(folding.FoldOf("c0"), "and the child holds the grandchild");
        Assert.IsNull(folding.FoldOf("g0"), "the grandchild holds nothing — and is not drawn either way");
    }

    /// <summary>One root with <paramref name="width"/> children and nothing under them.</summary>
    private static DiagramChart Fan(int width)
    {
        var ids = new List<string> { "root" };
        var edges = new List<(string From, string To)>();

        for (var at = 0; at < width; at++)
        {
            ids.Add($"c{at}");
            edges.Add(("root", $"c{at}"));
        }

        return new DiagramChart(ids, edges);
    }

    [TestMethod, TestCategory("Unit")]
    public void MaxFanOut_draws_the_first_of_a_wide_set_and_leaves_the_rest_over()
    {
        var folding = DiagramExpansion.Of(Asked("    maxFanOut: 3\n"), Fan(10));

        Assert.AreEqual(3, Fan(10).Ids.Skip(1).Count(folding.Draws), "three of them stay on the page");
        Assert.IsTrue(folding.Draws("c0") && folding.Draws("c2"), "and they are the ones written first");
        Assert.AreEqual(7, folding.MoreBehind("root"));
        CollectionAssert.AreEqual(new[] { "root" }, folding.Offering.ToList());
    }

    [TestMethod, TestCategory("Unit")]
    public void A_set_of_siblings_within_the_cap_is_left_alone()
    {
        var folding = DiagramExpansion.Of(Asked("    maxFanOut: 8\n"), Fan(5));

        Assert.IsTrue(Fan(5).Ids.All(folding.Draws));
        Assert.AreEqual(0, folding.MoreBehind("root"));
        Assert.IsFalse(folding.Offering.Any(), "nothing is offering anything");
    }

    [TestMethod, TestCategory("Unit")]
    public void Opening_what_is_left_over_shows_every_sibling()
    {
        var folding = DiagramExpansion.Of(Asked("    maxFanOut: 3\n"), Fan(10),
                                          new Dictionary<string, bool> { [NexaflowConfig.More + "root"] = true });

        Assert.IsTrue(Fan(10).Ids.All(folding.Draws));
        Assert.AreEqual(0, folding.MoreBehind("root"));
    }

    [TestMethod, TestCategory("Unit")]
    public void What_is_left_over_is_opened_under_the_producers_own_name_for_the_node_it_hangs_from()
    {
        var config = Asked("    maxFanOut: 3\n    expanded:\n      root: app.exe\n");

        Assert.AreEqual(NexaflowConfig.More + "app.exe", config.KeyFor(NexaflowConfig.More + "root"));
        Assert.AreEqual("root", NexaflowConfig.MoreOf(NexaflowConfig.More + "root"));
        Assert.IsNull(NexaflowConfig.MoreOf("root"), "an ordinary id offers nothing but itself");
    }

    [TestMethod, TestCategory("Unit")]
    public void A_sibling_holding_something_shown_is_never_held_back()
    {
        var chart = new DiagramChart(["root", "a", "b", "c", "under"],
                                     [("root", "a"), ("root", "b"), ("root", "c"), ("c", "under")]);

        var folding = DiagramExpansion.Of(Asked("    maxFanOut: 1\n"), chart);

        Assert.IsTrue(folding.Draws("c") && folding.Draws("under"), "holding it back would orphan what is under it");
        Assert.IsTrue(folding.Draws("a"), "the first of them stays whatever else happens");
        Assert.IsFalse(folding.Draws("b"));
        Assert.AreEqual(1, folding.MoreBehind("root"));
    }

    [TestMethod, TestCategory("Unit")]
    public void A_sibling_something_else_points_at_is_never_held_back_from_it()
    {
        var chart = new DiagramChart(["root", "other", "a", "b", "shared"],
                                     [("root", "a"), ("root", "b"), ("root", "shared"), ("other", "shared")]);

        var folding = DiagramExpansion.Of(Asked("    maxFanOut: 1\n"), chart);

        Assert.IsTrue(folding.Draws("shared"), "it is still reached the other way, so holding it back hides nothing");
        Assert.IsFalse(folding.Draws("b"));
    }

    [TestMethod, TestCategory("Unit")]
    public void Depth_and_breadth_do_not_count_the_same_node_twice()
    {
        // Depth folds the grandchild away; breadth then holds back the surplus child. What went for depth stands behind
        // a chip, and what went for breadth behind the node offering it — never both.
        var chart = new DiagramChart(["root", "a", "b", "c", "deep"],
                                     [("root", "a"), ("root", "b"), ("root", "c"), ("a", "deep")]);

        var folding = DiagramExpansion.Of(Asked("    defaultExpansion: 1\n    maxFanOut: 2\n"), chart);

        Assert.IsFalse(folding.Draws("deep"), "past the frontier");
        Assert.AreEqual(1, folding.MoreBehind("root"), "one of the three children was held back");
        Assert.AreEqual(new DiagramFold(Open: false, Hidden: 1), folding.FoldOf("a"), "and the one holding it says so");
    }
}

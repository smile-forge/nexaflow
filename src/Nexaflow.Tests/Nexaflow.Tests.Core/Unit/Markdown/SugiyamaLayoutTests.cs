using Nexaflow.Visuals.Text.Markdown.Graphs;
using Nexaflow.Visuals.Text.Markdown.Graphs.Layout;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;
using System.Windows;

namespace Nexaflow.Tests.Core.Unit.Markdown;

/// <summary>
/// WPF-free tests for the Sugiyama layout's clustering + edge-routing behaviour
/// (geometry only — no rendering). Exercises nested composite boxes and the
/// antiparallel-edge separation pass.
/// </summary>
[TestClass]
public class SugiyamaLayoutTests
{
    private static Rect Box(LayoutedGraph lg, string label) =>
        lg.SubgraphBoxes.First(b => b.Label == label).Bounds;

    /// <summary>A child box must sit fully inside its parent with a real gap on the left, right and
    /// bottom (the top abuts the parent's header band).</summary>
    private static bool ContainedWithMargin(Rect outer, Rect inner, double margin = 4.0) =>
        inner.Left   >= outer.Left   + margin &&
        inner.Right  <= outer.Right  - margin &&
        inner.Bottom <= outer.Bottom - margin &&
        inner.Top    >= outer.Top;

    [TestMethod]
    public void NestedComposites_AreLaidOutAsBoxesInsideBoxes()
    {
        var g = new MermaidStateParser().Parse(
            """
            stateDiagram-v2
                [*] --> First
                state First {
                    [*] --> Second
                    state Second {
                        [*] --> second
                        state Third {
                            [*] --> third
                            third --> [*]
                        }
                        second --> Third
                    }
                }
            """);

        var lg = SugiyamaLayout.Compute(g);

        var first  = Box(lg, "First");
        var second = Box(lg, "Second");
        var third  = Box(lg, "Third");

        Assert.IsTrue(ContainedWithMargin(first, second), "Second must sit inside First with a margin");
        Assert.IsTrue(ContainedWithMargin(second, third), "Third must sit inside Second with a margin");
    }

    [TestMethod]
    public void AntiparallelEdges_AreSeparatedIntoParallelLanes()
    {
        // A two-state cycle: both directions must run on their own lane (endpoints shifted to
        // opposite sides of centre) so neither overlaps the other.
        var g = new Graph();
        g.AddEdge("A", "B");
        g.AddEdge("B", "A");

        var lg = SugiyamaLayout.Compute(g);

        var ab = lg.Edges.Single(e => e.Source is { SourceId: "A", TargetId: "B" });
        var ba = lg.Edges.Single(e => e.Source is { SourceId: "B", TargetId: "A" });

        // The whole edge (start port included) is shifted onto its lane, to opposite sides.
        double startGap = Math.Abs(ab.Waypoints[0].X - ba.Waypoints[0].X);
        double endGap   = Math.Abs(ab.Waypoints[^1].X - ba.Waypoints[^1].X);
        Assert.IsTrue(startGap > 10, $"start ports must be separated (was {startGap:0.#})");
        Assert.IsTrue(endGap   > 10, $"end ports must be separated (was {endGap:0.#})");
    }

    [TestMethod]
    public void ExtraEdge_GetsItsOwnPort_NotOverlappingACouple()
    {
        // A node carrying a bidirectional couple AND a third edge: all three must leave the node at
        // distinct ports, and the lone edge must be offset from the node centre (not down the middle).
        var g = new Graph();
        g.AddEdge("Still", "Moving");
        g.AddEdge("Moving", "Still");
        g.AddEdge("Still", "Done");

        var lg = SugiyamaLayout.Compute(g);
        double stillX = lg.AllNodes.Single(n => n.Source?.Id == "Still").X;

        // Every edge leaving Still (the couple's two + Still→Done) starts at Still's bottom face.
        var startXs = lg.Edges
            .Where(e => e.From.Source?.Id == "Still")
            .Select(e => Math.Round(e.Waypoints[0].X, 1))
            .ToList();

        Assert.AreEqual(3, startXs.Count);
        Assert.AreEqual(3, startXs.Distinct().Count(), "all three ports must be distinct");

        var toDone = lg.Edges.Single(e => e.Source is { SourceId: "Still", TargetId: "Done" });
        Assert.IsTrue(Math.Abs(toDone.Waypoints[0].X - stillX) > 5,
            "the lone Still→Done edge must start offset from the box centre, clear of the couple");
    }
}

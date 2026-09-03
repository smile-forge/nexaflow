using Nexaflow.Visuals.Text.Markdown.Graphs;
using Nexaflow.Visuals.Text.Markdown.Graphs.Layout;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// WPF-free tests for the Sugiyama layout's clustering + edge-routing behaviour
/// (geometry only — no rendering). Exercises nested composite boxes and the
/// antiparallel-edge separation pass.
/// </summary>
[TestClass]
[CoversNode("mermaid")]
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
    public void ClassDiagram_DirectionLr_StacksUnconnectedClassesVertically()
    {
        // The "As Code" panel emits `direction LR` so a file's unrelated classes form a vertical column.
        var td = Nodes(SugiyamaLayout.Compute(new MermaidClassParser().Parse("classDiagram\n  class A\n  class B\n  class C\n")));
        var lr = Nodes(SugiyamaLayout.Compute(new MermaidClassParser().Parse("classDiagram\n  direction LR\n  class A\n  class B\n  class C\n")));

        Assert.IsTrue(Spread(td, n => n.X) > Spread(td, n => n.Y), "default top-down lays unconnected classes in a row");
        Assert.IsTrue(Spread(lr, n => n.Y) > Spread(lr, n => n.X), "direction LR stacks them into a column");
    }

    private static List<LayoutNode> Nodes(LayoutedGraph lg) => lg.AllNodes.Where(n => !n.IsDummy).ToList();
    private static double Spread(List<LayoutNode> ns, Func<LayoutNode, double> sel) => ns.Max(sel) - ns.Min(sel);

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

    // ── Wide and dense graphs ─────────────────────────────────────────────────

    /// <summary>Counts crossings between the drawn edges, from their endpoints — the thing a reader
    /// actually sees, rather than the layer orders the algorithm works in.</summary>
    private static int Crossings(LayoutedGraph lg)
    {
        var segments = lg.Edges
            .Where(e => e.Waypoints.Count >= 2)
            .Select(e => (a: e.Waypoints[0], b: e.Waypoints[^1], from: e.From, to: e.To))
            .ToList();

        int count = 0;
        for (int i = 0; i < segments.Count; i++)
            for (int j = i + 1; j < segments.Count; j++)
            {
                var (p1, p2, f1, t1) = segments[i];
                var (p3, p4, f2, t2) = segments[j];
                // Edges meeting at a shared node are not a crossing, they are a fan.
                if (ReferenceEquals(f1, f2) || ReferenceEquals(t1, t2) ||
                    ReferenceEquals(f1, t2) || ReferenceEquals(t1, f2)) continue;
                if (Intersects(p1, p2, p3, p4)) count++;
            }
        return count;
    }

    private static bool Intersects(Point a, Point b, Point c, Point d)
    {
        double Side(Point p, Point q, Point r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        double d1 = Side(a, b, c), d2 = Side(a, b, d), d3 = Side(c, d, a), d4 = Side(c, d, b);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
               ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    [TestMethod]
    public void DeliberatelyCrossedBipartiteGraph_IsUntangled()
    {
        // Six sources wired to six targets in reverse order: laid out in source order this is 15
        // crossings, and every one of them is avoidable by reordering one layer.
        var g = new Graph();
        for (int i = 0; i < 6; i++) g.GetOrAdd($"s{i}");
        for (int i = 0; i < 6; i++) g.AddEdge($"s{i}", $"t{5 - i}");

        Assert.AreEqual(0, Crossings(SugiyamaLayout.Compute(g)),
            "a graph whose crossings are entirely an ordering artefact must come out flat");
    }

    [TestMethod]
    public void DenseGraph_HasFewCrossingsRelativeToItsEdges()
    {
        // A layered mesh where each node feeds two in the next layer, wired so the naive order
        // interleaves them.
        var g = new Graph();
        for (int layer = 0; layer < 4; layer++)
            for (int i = 0; i < 6; i++)
            {
                g.AddEdge($"n{layer}_{i}", $"n{layer + 1}_{(i * 2) % 6}");
                g.AddEdge($"n{layer}_{i}", $"n{layer + 1}_{(i * 2 + 1) % 6}");
            }

        var lg = SugiyamaLayout.Compute(g);
        int crossings = Crossings(lg);

        // Not zero — this graph genuinely cannot be drawn flat — but the untangling has to bite.
        Assert.IsTrue(crossings < lg.Edges.Count,
            $"{crossings} crossings for {lg.Edges.Count} edges is not a layout anyone can follow");
    }

    [TestMethod]
    public void AChildSitsUnderItsParentRatherThanPackedFromTheMargin()
    {
        // Two separate parents, one child each. Packing each layer from the left margin puts both
        // children hard left; the straightening pass is what puts each under its own parent.
        var g = new Graph();
        g.AddEdge("p1", "c1");
        g.AddEdge("p2", "c2");

        var lg = SugiyamaLayout.Compute(g);
        double Dx(string parent, string child) =>
            Math.Abs(lg.AllNodes.Single(n => n.Source?.Id == parent).X -
                     lg.AllNodes.Single(n => n.Source?.Id == child).X);

        Assert.IsTrue(Dx("p1", "c1") < 1, "c1 must sit under p1");
        Assert.IsTrue(Dx("p2", "c2") < 1, "c2 must sit under p2");
    }

    [TestMethod]
    public void HighFanOut_WrapsIntoRowsRatherThanOneEndlessLine()
    {
        // One node with sixty children. In a single row that is thousands of pixels of diagram; the
        // layer wraps instead, so the block stays inside the width it was given.
        var g = new Graph();
        for (int i = 0; i < 60; i++) g.AddEdge("root", $"c{i}");

        var wide = SugiyamaLayout.Compute(g);                          // no limit given
        var kept = SugiyamaLayout.Compute(g, preferredMaxWidth: 1000);

        Assert.IsTrue(kept.Width < wide.Width / 2,
            $"wrapping must actually narrow the diagram (was {wide.Width:0}, now {kept.Width:0})");
        Assert.IsTrue(kept.Width <= 1400, $"the wrapped layout is still {kept.Width:0} wide");

        var children = kept.AllNodes.Where(n => n.Source?.Id.StartsWith("c") == true).ToList();
        Assert.AreEqual(60, children.Count, "wrapping hides nothing — every child is still drawn");
        Assert.IsTrue(children.Select(n => Math.Round(n.Y)).Distinct().Count() > 1,
            "the children must occupy more than one row");
    }

    [TestMethod]
    public void HighFanOut_InAnLrGraph_WrapsAgainstTheHeightItWasGiven()
    {
        // The same problem turned ninety degrees: an LR fan-out stacks vertically, so the limit that
        // matters is the height of the panel, not its width.
        var g = new Graph { Direction = GraphDirection.LeftRight };
        for (int i = 0; i < 40; i++) g.AddEdge("root", $"c{i}");

        var tall = SugiyamaLayout.Compute(g, preferredMaxWidth: 1000);
        var kept = SugiyamaLayout.Compute(g, preferredMaxWidth: 1000, preferredMaxHeight: 600);

        Assert.IsTrue(kept.Height < tall.Height / 2,
            $"the column must break up (was {tall.Height:0}, now {kept.Height:0})");
        Assert.AreEqual(41, kept.AllNodes.Count(n => !n.IsDummy));
    }

    [TestMethod]
    public void LongLabels_SpendHeightRatherThanGrowingSidewaysForever()
    {
        // The shape a native binary's import tree makes: left-to-right, long api-set names, several
        // levels deep. Sized to its labels it comes out thousands of pixels wide and a few hundred
        // tall — every bit of the reading on one axis while the other sits empty.
        var g = new Graph { Direction = GraphDirection.LeftRight };
        g.GetOrAdd("root", "kernel32.dll<br/>1200 imports");
        for (int layer = 0; layer < 6; layer++)
            for (int i = 0; i < 5; i++)
            {
                string from = layer == 0 ? "root" : $"n{layer - 1}_{i}";
                g.AddEdge(from, $"n{layer}_{i}");
                g.FindNode($"n{layer}_{i}")!.Label = $"api-ms-win-core-processthreads-l{layer * 5 + i}-1-0.dll";
            }

        var free = SugiyamaLayout.Compute(g);                                        // no width to respect
        var kept = SugiyamaLayout.Compute(g, preferredMaxWidth: 1500, preferredMaxHeight: 600);

        Assert.IsTrue(kept.Width <= 1500 + 1, $"the diagram must fit the width it was given (was {kept.Width:0})");
        Assert.IsTrue(kept.Width < free.Width, "…by narrowing nodes, not by being the same shape");
        Assert.IsTrue(kept.Height > free.Height,
            "…and the height it saves sideways it spends downward, where the space actually is");

        // Nothing is dropped or truncated away — the labels wrap into narrower boxes.
        Assert.AreEqual(31, kept.AllNodes.Count(n => !n.IsDummy));
    }

    [TestMethod]
    public void AShortLabelIsNeverSqueezedByACapItDidNotCause()
    {
        // The cap binds only on the labels that made the diagram too wide; an ordinary flowchart with
        // ordinary labels must lay out exactly as it did before there was a cap at all.
        var g = new Graph();
        for (int i = 0; i < 6; i++) g.AddEdge("start", $"step {i}");

        var free = Nodes(SugiyamaLayout.Compute(g));
        var kept = Nodes(SugiyamaLayout.Compute(g, preferredMaxWidth: 900, preferredMaxHeight: 600));

        for (int i = 0; i < free.Count; i++)
            Assert.AreEqual(free[i].Width, kept[i].Width, 0.01);
    }

    [TestMethod]
    public void ASmallGraphIsUnaffectedByTheWidthItIsGiven()
    {
        // The width hint tightens a layout that needs it; it must not reshape one that doesn't.
        var g = new Graph();
        g.AddEdge("a", "b");
        g.AddEdge("b", "c");

        var loose = Nodes(SugiyamaLayout.Compute(g));
        var tight = Nodes(SugiyamaLayout.Compute(g, preferredMaxWidth: 900, preferredMaxHeight: 600));

        for (int i = 0; i < loose.Count; i++)
        {
            Assert.AreEqual(loose[i].X, tight[i].X, 0.01);
            Assert.AreEqual(loose[i].Y, tight[i].Y, 0.01);
        }
    }

    // ── The clustered rebuild is total ────────────────────────────────────

    /// <summary>
    /// Laying a graph out by levels rebuilds every edge, and for years that rebuild listed the
    /// fields it carried by hand — so an edge kept its label and arrow but silently lost its
    /// multiplicity the moment it sat inside a namespace or composite. Every property now travels
    /// via <see cref="Edge.Copy"/>, and this is the test that says so.
    /// </summary>
    [TestMethod]
    public void ClusteredEdges_KeepEveryProperty()
    {
        var g = new MermaidClassParser().Parse(
            """
            classDiagram
                namespace Ordering {
                    class Order
                    class LineItem
                }
                Order "1" --> "*" LineItem : contains
            """);

        // Sanity: the parse really did produce a clustered graph with multiplicity to lose.
        Assert.AreEqual(1, g.Subgraphs.Count, "expected a namespace box");
        var source = g.Edges.Single();
        Assert.AreEqual("1", source.StartLabel);
        Assert.AreEqual("*", source.EndLabel);

        var laidOut = SugiyamaLayout.Compute(g, 900).Edges.Single(e => e.Source is not null).Source!;
        Assert.AreEqual("contains", laidOut.Label);
        Assert.AreEqual("1", laidOut.StartLabel, "multiplicity must survive the per-level rebuild");
        Assert.AreEqual("*", laidOut.EndLabel);
        Assert.AreEqual(source.Arrow, laidOut.Arrow);
        Assert.AreEqual(source.Style, laidOut.Style);
    }

    /// <summary>
    /// The same guarantee stated against the type rather than one diagram: a copy differs from its
    /// original only where it was asked to. A property added to <see cref="Edge"/> without being
    /// added to <see cref="Edge.Copy"/> fails here rather than silently vanishing inside a box.
    /// </summary>
    [TestMethod]
    public void EdgeCopy_CarriesEveryProperty()
    {
        var original = new Edge
        {
            SourceId = "a", TargetId = "b", Label = "label", Style = EdgeStyle.Dotted,
            Arrow = EdgeArrow.DiamondFilled, StartArrow = EdgeArrow.TriangleHollow,
            StartLabel = "1", EndLabel = "*", SubLabel = "[HTTPS]",
            LineColor = "#f00", TextColor = "#0f0", IsReversed = true,
            Href = "https://example.com", Tooltip = "tip",
        };

        var copy = original.Copy();
        foreach (var property in typeof(Edge).GetProperties().Where(p => p.CanRead))
            Assert.AreEqual(property.GetValue(original), property.GetValue(copy), property.Name);

        var moved = original.Copy("x", "y");
        Assert.AreEqual("x", moved.SourceId);
        Assert.AreEqual("y", moved.TargetId);
        Assert.AreEqual("1", moved.StartLabel, "re-pointing an edge must not drop the rest of it");
    }

    /// <summary>As above, for the group box — its style and membership are what get forgotten.</summary>
    [TestMethod]
    public void SubgraphCopy_CarriesEveryProperty()
    {
        var original = new Subgraph
        {
            Id = "b", Label = "Boundary", ParentId = "outer", Href = "https://example.com", Tooltip = "tip",
            Style = new SubgraphStyle { SubLabel = "[System]", FillColor = "#111", StrokeColor = "#222", TextColor = "#333", BorderStyle = EdgeStyle.Solid },
        };
        original.NodeIds.AddRange(["one", "two"]);

        var copy = original.Copy();
        Assert.AreEqual(original.Id, copy.Id);
        Assert.AreEqual(original.Label, copy.Label);
        Assert.AreEqual(original.ParentId, copy.ParentId);
        Assert.AreEqual(original.Href, copy.Href);
        Assert.AreEqual(original.Tooltip, copy.Tooltip);
        CollectionAssert.AreEqual(original.NodeIds, copy.NodeIds);

        Assert.AreEqual("[System]", copy.Style!.SubLabel);
        Assert.AreEqual("#111", copy.Style.FillColor);
        Assert.AreEqual("#222", copy.Style.StrokeColor);
        Assert.AreEqual("#333", copy.Style.TextColor);
        Assert.AreEqual(EdgeStyle.Solid, copy.Style.BorderStyle);

        // The style is copied, not shared — a derived view must not edit the parsed graph.
        copy.Style.SubLabel = "changed";
        Assert.AreEqual("[System]", original.Style!.SubLabel);

        CollectionAssert.AreEqual(new[] { "only" }, original.Copy(["only"]).NodeIds);
    }

    /// <summary>The shell a derived view starts from carries the document-level properties.</summary>
    [TestMethod]
    public void GraphCopyShell_CarriesTheDocumentProperties()
    {
        var g = new Graph
        {
            Title = "T",
            Direction = GraphDirection.LeftRight,
            Legend = [new GraphLegendEntry("Person", null, null, null)],
        };
        g.GetOrAdd("a");

        var shell = g.CopyShell();
        Assert.AreEqual("T", shell.Title);
        Assert.AreEqual(GraphDirection.LeftRight, shell.Direction);
        Assert.AreSame(g.Legend, shell.Legend);
        Assert.AreEqual(0, shell.Nodes.Count, "a shell carries no content");
        Assert.AreEqual(0, shell.Edges.Count);
        Assert.AreEqual(0, shell.Subgraphs.Count);
    }
}

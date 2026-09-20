using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Flowchart;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Swimlane;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>swimlane-beta</c> block drawn on the shared layout tree: a flowchart whose outermost subgraphs are lanes — a band each,
/// running the whole length of the chart with the lane's own name in a strip at the near end of it, the steps in one coming one to a
/// rank, and work handed to another lane going across rather than on.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("swimlanes")]
public class SwimlaneBuilderTests : MermaidBuilderContract
{
    private const string Order =
        "swimlane-beta LR\n  subgraph Customer\n    Browse[Browse catalogue]\n    Pay[Pay]\n  end\n"
        + "  subgraph Warehouse\n    Pick[Pick items]\n    Ship[Ship order]\n  end\n"
        + "  subgraph Finance\n    Invoice[Raise invoice]\n  end\n"
        + "  Browse --> Pay\n  Pay --> Pick\n  Pick --> Ship\n  Pay --> Invoice";

    private const string Escalation =
        "swimlane-beta TB\n  subgraph Customer\n    request[Request service]\n    receive[Receive update]\n  end\n"
        + "  subgraph Support\n    triage[Triage request]\n    answer[Send answer]\n  end\n"
        + "  request --> triage\n  triage -->|Known issue| answer\n  answer --> receive";

    public override MermaidDiagram Diagram => MermaidDiagram.Swimlane;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("an order filled across three lanes", Order),
        ("a request escalated and answered", Escalation),
        ("laid out top down", "swimlane-beta TB\n  subgraph A\n    one\n  end\n  subgraph B\n    two\n  end\n  one --> two"),
        ("laid out bottom up", "swimlane-beta BT\n  subgraph A\n    one\n  end\n  subgraph B\n    two\n  end\n  one --> two"),
        ("laid out right to left", "swimlane-beta RL\n  subgraph A\n    one\n  end\n  subgraph B\n    two\n  end\n  one --> two"),
        ("a lane named and labelled", "swimlane-beta LR\n  subgraph ops [Operations all told]\n    plan[Plan the work]\n  end"),
        ("a lane with nothing in it", "swimlane-beta TB\n  subgraph Nobody\n  end\n  subgraph Somebody\n    one\n  end"),
        ("a subgraph inside a lane",
         "swimlane-beta TB\n  subgraph Team\n    subgraph Morning\n      a --> b\n    end\n  end\n  subgraph Other\n    c\n  end"),
        ("the shapes a lane holds",
         "swimlane-beta TB\n  subgraph Work\n    start([Start])\n    ask{Ready?}\n    done((Done))\n  end\n  start --> ask --> done"),
        ("handoffs both ways",
         "swimlane-beta LR\n  subgraph A\n    one\n  end\n  subgraph B\n    two\n  end\n  one --> two\n  two --> one"),
        ("what is written on a handoff",
         "swimlane-beta LR\n  subgraph A\n    one\n  end\n  subgraph B\n    two\n  end\n  one -->|signed off| two"),
        ("lanes and classes",
         "swimlane-beta LR\n  subgraph ops [Operations]\n    intake[Receive request]\n  end\n  subgraph legal [Legal]\n"
         + "    review[Review contract]\n  end\n  intake --> review\n  classDef attention fill:#fff2cc,stroke:#d6a500\n"
         + "  class review attention"),
        ("a lane styled", "swimlane-beta TB\n  subgraph A\n    one\n  end\n  style A fill:#eef,stroke:#66a"),
        ("the lane order worked out",
         "---\nconfig:\n  swimlane:\n    automaticLaneOrdering: true\n---\nswimlane-beta TB\n  subgraph A\n    one\n  end\n"
         + "  subgraph B\n    two\n  end\n  subgraph C\n    three\n  end\n  one --> three"),
        ("a handoff counting like any other link",
         "---\nconfig:\n  swimlane:\n    ignoreCrossLaneEdges: false\n---\nswimlane-beta TB\n  subgraph A\n    one\n  end\n"
         + "  subgraph B\n    two\n  end\n  one --> two"),
        ("no lanes at all, which is a flowchart", "swimlane-beta LR\n  a[Start] --> b[Stop]"),
        ("a node in no lane beside the lanes", "swimlane-beta TB\n  loose\n  subgraph A\n    one\n  end\n  loose --> one"),
        ("still being written", "swimlane-beta TB\n  subgraph \n    a[\"\"]\n  end\n  b --> "),
        ("what nobody means to write", "swimlane-beta LR\n  one\n  end\n  style nowhere fill:#969"),
        ("nothing to draw", "swimlane-beta"),
    ];

    [TestMethod]
    public void EachLaneIsABandRunningTheWholeLengthOfTheChart() => UiThread.Run(() =>
    {
        var laid = Build(Order);
        var bands = Lanes(laid, Order);

        Assert.AreEqual(3, bands.Count);

        // Running left to right, a lane is a band across the chart, so it is the chart's whole width and stacked down the page.
        foreach (var (name, bounds) in bands)
            Assert.IsTrue(bounds.Width >= laid.Size.Width - 41, $"{name} runs the whole width of the chart: {bounds} of {laid.Size}");

        var down = bands.Values.OrderBy(bounds => bounds.Top).ToList();
        for (var at = 1; at < down.Count; at++)
            Assert.IsTrue(down[at].Top >= down[at - 1].Bottom - 1, $"the bands are stacked without overlapping: {down[at - 1]} then {down[at]}");
    });

    [TestMethod]
    public void ALanesOwnNameIsInAStripAtTheNearEndOfItsBand() => UiThread.Run(() =>
    {
        const string source = "swimlane-beta TB\n  subgraph Customer\n    one\n  end";

        var laid = Build(source);
        var band = Pieces(laid, FlowchartPiece.Lane).Single();
        var strip = Pieces(laid, FlowchartPiece.Title).Single();
        var said = Said(strip).Single();

        Assert.AreEqual("Customer", said.Words!.Glyphs.Text);
        Assert.AreEqual(band.Bounds.Top, strip.Bounds.Top, 1, "running down the page, the strip is across the top of the band");
        Assert.IsTrue(strip.Bounds.Height < band.Bounds.Height, "and it is only as deep as the name it holds");
        Assert.IsTrue(Pieces(laid, FlowchartPiece.Node).Single().Bounds.Top >= strip.Bounds.Bottom - 1,
                      "the work in the lane starts below it");
    });

    [TestMethod]
    public void ALanesNameReadsUpTheBandWhereTheChartRunsAcross() => UiThread.Run(() =>
    {
        var down = Said(Pieces(Build("swimlane-beta TB\n  subgraph Customer\n    one\n  end"), FlowchartPiece.Title).Single()).Single();
        var across = Said(Pieces(Build("swimlane-beta LR\n  subgraph Customer\n    one\n  end"), FlowchartPiece.Title).Single()).Single();

        Assert.IsTrue(down.Bounds.Width > down.Bounds.Height, $"down the page it reads across: {down.Bounds}");
        Assert.IsTrue(across.Bounds.Height > across.Bounds.Width, $"and across the page it reads up the band: {across.Bounds}");
    });

    [TestMethod]
    public void EverythingInALaneIsDrawnInsideIt() => UiThread.Run(() =>
    {
        const string source = "swimlane-beta TB\n  subgraph Team\n    subgraph Morning\n      a\n    end\n    b\n  end\n  c";

        var laid = Build(source);
        var lane = Pieces(laid, FlowchartPiece.Lane).Single();
        var inside = Pieces(laid, FlowchartPiece.Node)
            .Where(piece => piece.Ancestors().Any(over => over.Kind == FlowchartPiece.Group))
            .ToList();

        CollectionAssert.AreEqual(new[] { "a", "b" }, inside.Select(piece => Written(source, piece.Part)).ToArray(),
                                  "the lane's own nodes hang off it, and c does not");

        foreach (var node in inside)
            Assert.IsTrue(Holds(lane.Bounds, node.Bounds), $"{node.Bounds} sits inside the band {lane.Bounds}");
    });

    [TestMethod]
    public void ALanesStepsComeOneToARank() => UiThread.Run(() =>
    {
        var nodes = Nodes("swimlane-beta TB\n  subgraph A\n    one\n    two\n  end");

        Assert.IsTrue(nodes["two"].Top >= nodes["one"].Bottom,
                      $"every step of a lane's own work is a rank of its own: {nodes["one"]} then {nodes["two"]}");
    });

    [TestMethod]
    public void WorkHandedToAnotherLaneGoesAcrossRatherThanOn() => UiThread.Run(() =>
    {
        var nodes = Nodes("swimlane-beta TB\n  subgraph A\n    one\n  end\n  subgraph B\n    two\n  end\n  one --> two");

        Assert.AreEqual(nodes["one"].Top, nodes["two"].Top, 1, "the lane it is handed to carries on where its own work has got to");
        Assert.IsTrue(nodes["two"].Left > nodes["one"].Right, "and it is the next lane across");
    });

    [TestMethod]
    public void AHandoffCountsLikeAnyOtherLinkWhereTheFrontMatterAsks() => UiThread.Run(() =>
    {
        var nodes = Nodes("---\nconfig:\n  swimlane:\n    ignoreCrossLaneEdges: false\n---\nswimlane-beta TB\n"
                        + "  subgraph A\n    one\n  end\n  subgraph B\n    two\n  end\n  one --> two");

        Assert.IsTrue(nodes["two"].Top >= nodes["one"].Bottom, $"it moves it a rank on: {nodes["one"]} then {nodes["two"]}");
    });

    [TestMethod]
    public void TheLanesAreSetAcrossInTheOrderTheyAreWritten() => UiThread.Run(() =>
    {
        const string source = "swimlane-beta TB\n  subgraph A\n    one\n  end\n  subgraph B\n    two\n  end\n"
                            + "  subgraph C\n    three\n  end\n  one --> three";

        CollectionAssert.AreEqual(new[] { "A", "B", "C" }, Across(Build(source), source),
                                  "the order lanes are written in usually means something, so it is kept");
    });

    [TestMethod]
    public void TheLaneOrderIsWorkedOutWhereTheFrontMatterAsks() => UiThread.Run(() =>
    {
        const string source = "---\nconfig:\n  swimlane:\n    automaticLaneOrdering: true\n---\nswimlane-beta TB\n"
                            + "  subgraph A\n    one\n  end\n  subgraph B\n    two\n  end\n  subgraph C\n    three\n  end\n"
                            + "  one --> three\n  three --> one";

        var order = Across(Build(source), source).ToList();

        Assert.AreEqual(1, Math.Abs(order.IndexOf("A") - order.IndexOf("C")),
                        $"the lanes handing work to each other are set beside each other: {string.Join(", ", order)}");
    });

    [TestMethod]
    public void OnlyALanesNameStripIsFilled() => UiThread.Run(() =>
    {
        var laid = Build("swimlane-beta TB\n  subgraph A\n    one\n  end");

        Assert.IsNull(Filled(Pieces(laid, FlowchartPiece.Lane).Single()), "the band is not coloured over the work it holds");
        Assert.IsNotNull(Filled(Pieces(laid, FlowchartPiece.Title).Single()), "and the strip its name is in is, so the lanes are told apart");
    });

    [TestMethod]
    public void PressingALaneWhereNothingElseStandsMeansTheLane() => UiThread.Run(() =>
    {
        const string source = "swimlane-beta TB\n  subgraph Customer\n    one\n  end\n  subgraph Other\n    two\n    three\n  end";

        var laid = Build(source);
        var lane = Pieces(laid, FlowchartPiece.Lane).First();
        var node = Pieces(laid, FlowchartPiece.Node).First();

        Assert.AreEqual("subgraph Customer\n    one\n  end", Written(source, lane.Part),
                        "the band stands for the whole of what the lane was written as, not the line that opened it");

        Assert.IsTrue(Stands(lane, new Point(lane.Bounds.Left + 2, lane.Bounds.Bottom - 2)),
                      "a press in the band where nothing else is drawn means the lane");
        Assert.IsFalse(Stands(lane, Middle(node.Bounds)), "and where a node is drawn it means the node");
    });

    [TestMethod]
    public void WhatIsDrawnInALaneStandsForAStretchOfWhatTheLaneStandsFor() => UiThread.Run(() =>
    {
        const string source = "swimlane-beta TB\n  subgraph Team\n    subgraph Morning\n      a[Early]\n    end\n    b[Late]\n  end\n  c[Loose]";

        var laid = Build(source);
        var lane = Pieces(laid, FlowchartPiece.Lane).Single();

        foreach (var piece in Pieces(laid, FlowchartPiece.Group).Concat(Pieces(laid, FlowchartPiece.Node))
                     .Where(piece => piece.Ancestors().Any(over => over.Kind == FlowchartPiece.Lane || ReferenceEquals(over.Part, lane.Part)))
                     .Append(lane))
        {
            if (piece.Part is not { } part || ReferenceEquals(part, lane.Part)) continue;

            Assert.IsTrue(part.Start >= lane.Part!.Start && part.End() <= lane.Part.End(),
                          $"{Written(source, part)} is written inside the lane, so it stands inside what the lane stands for");
        }

        Assert.IsFalse(Pieces(laid, FlowchartPiece.Node)
                           .Where(piece => Written(source, piece.Part) == "c[Loose]")
                           .Any(piece => piece.Part!.Start >= lane.Part!.Start && piece.Part.End() <= lane.Part.End()),
                       "and a node written outside every lane stands outside them");
    });

    [TestMethod]
    public void ASwimlaneWithNoLanesIsDrawnAsAFlowchart() => UiThread.Run(() =>
    {
        var laid = Build("swimlane-beta TB\n  a --> b");
        var nodes = Nodes(laid);

        Assert.AreEqual(0, Pieces(laid, FlowchartPiece.Lane).Count, "nothing is banded");
        Assert.IsTrue(nodes["b"].Top > nodes["a"].Bottom, $"and a link reaches the next rank as it always does: {nodes["a"]} then {nodes["b"]}");
    });

    // ── What it works with ──────────────────────────────────────────────────

    private static Laid Build(string source, double room = 900) =>
        SwimlaneBuilder.Build(EditState.For(source), new DiagramLaying(MarkdownPalette.Dark, 1.0, room));

    /// <summary>Every lane drawn, by the name in its strip.</summary>
    private static Dictionary<string, Rect> Lanes(Laid laid, string source)
    {
        var lanes = new Dictionary<string, Rect>();

        foreach (var band in Pieces(laid, FlowchartPiece.Lane))
            lanes[Named(laid, source, band)] = band.Bounds;

        return lanes;
    }

    /// <summary>The lanes in the order their bands are set across the chart.</summary>
    private static string[] Across(Laid laid, string source) =>
        [.. Pieces(laid, FlowchartPiece.Lane).OrderBy(band => band.Bounds.Left).Select(band => Named(laid, source, band))];

    /// <summary>What a lane's band is called, which is what is written in the strip drawn over it.</summary>
    private static string Named(Laid laid, string source, Piece band) =>
        Pieces(laid, FlowchartPiece.Title)
            .Where(strip => band.Bounds.Contains(Middle(strip.Bounds)))
            .SelectMany(Said)
            .Select(said => said.Words!.Glyphs.Text)
            .FirstOrDefault() ?? Written(source, band.Part) ?? band.Bounds.ToString();

    /// <summary>Every node drawn, by what is written on it.</summary>
    private static Dictionary<string, Rect> Nodes(string source) => Nodes(Build(source));

    private static Dictionary<string, Rect> Nodes(Laid laid)
    {
        var nodes = new Dictionary<string, Rect>();

        foreach (var piece in Pieces(laid, FlowchartPiece.Node))
            nodes[Said(piece).Select(said => said.Words!.Glyphs.Text).FirstOrDefault() ?? piece.Bounds.ToString()] = piece.Bounds;

        return nodes;
    }

    /// <summary>The words drawn under a piece, in the order they were drawn.</summary>
    private static IEnumerable<Piece> Said(Piece piece) =>
        piece.SelfAndDescendants().Where(part => part.Kind == MermaidPiece.Words && part.Words is not null);

    private static Color? Filled(Piece piece) =>
        piece.SelfAndDescendants().Where(part => part.Kind == MermaidPiece.Shape).Select(Fill).FirstOrDefault();

    private static bool Holds(Rect over, Rect inner) =>
        inner.Left >= over.Left - 1 && inner.Right <= over.Right + 1 && inner.Top >= over.Top - 1 && inner.Bottom <= over.Bottom + 1;

    /// <summary>Whether a piece's shape stands at a point, which is where a press on it lands rather than on what is under it.</summary>
    private static bool Stands(Piece piece, Point at)
    {
        var shift = piece.Offset;
        foreach (var over in piece.Ancestors()) shift += over.Offset;

        return piece.SelfAndDescendants().First(part => part.Kind == MermaidPiece.Shape).Region?.FillContains(at - shift) == true;
    }
}

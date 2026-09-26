using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Flowchart;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>flowchart</c> block drawn on the shared layout tree: the nodes in the ranks the links put them in, the subgraphs holding
/// the nodes written inside them, and the links over all of it — each node standing for what was written for it.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("graph-flowchart")]
public class FlowchartBuilderTests : MermaidBuilderContract
{
    private const string Shapes =
        "flowchart TD\n  id1[Square] id2(Round)\n  id3([Stadium]) id4[[Subroutine]]\n  id5[(Database)] id6((Circle))\n"
        + "  id7>Asymmetric] id8{Rhombus}\n  id9{{Hexagon}} id10[/Lean right/]\n  id11[\\Lean left\\] id12[/Christmas\\]\n"
        + "  id13[\\Go shopping/] id14(((Double circle)))";

    private const string Decision =
        "flowchart TD\n  A[Start] --> B{Is it?}\n  B -- Yes --> C[OK]\n  C --> D[Rethink]\n  D --> B\n  B -- No ----> E[End]";

    private const string Grouped =
        "flowchart TB\n  c1 --> a2\n  subgraph one\n    a1 --> a2\n  end\n  subgraph two\n    b1 --> b2\n  end\n  one --> two";

    public override MermaidDiagram Diagram => MermaidDiagram.Flowchart;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("a decision and the way back", Decision),
        ("every shape there is", Shapes),
        ("subgraphs joined to each other", Grouped),
        ("laid out left to right", "flowchart LR\n  a --> b --> c"),
        ("laid out bottom up", "flowchart BT\n  a --> b --> c"),
        ("laid out right to left", "flowchart RL\n  a --> b --> c"),
        ("a subgraph running its own way",
            "flowchart LR\n  subgraph TOP\n    direction TB\n    subgraph B1\n      direction RL\n      i1 --> f1\n    end\n"
            + "    subgraph B2\n      direction BT\n      i2 --> f2\n    end\n  end\n  A --> TOP --> B\n  B1 --> B2"),
        ("links of every kind",
            "flowchart LR\n  a --> b\n  b --- c\n  c -.-> d\n  d ==> e\n  e --o f\n  f --x g\n  g <--> h\n  h ~~~ i"),
        ("links every length", "flowchart TD\n  a --> b\n  a ---> c\n  a -----> d"),
        ("a fan out and back in", "flowchart LR\n  a --> b & c --> d"),
        ("a node written again to say more about it", "flowchart LR\n  a --> b\n  a[Said later]\n  b((Round later))"),
        ("classes and styles",
            "flowchart LR\n  a:::blue --> b\n  classDef blue fill:#6e6ce6,stroke:#333,stroke-width:4px\n"
            + "  style b fill:#bbf,stroke:#f66,color:#fff,stroke-dasharray: 5 5"),
        ("a link styled by its number", "flowchart LR\n  a --> b\n  b --> c\n  linkStyle 0 stroke:#f00,stroke-width:3px"),
        ("shapes and labels named by metadata",
            "flowchart TD\n  a@{ shape: cyl, label: \"The store\" }\n  b@{ shape: text, label: \"Words alone\" }\n  a --> b"),
        ("a label long enough to wrap",
            "flowchart TD\n  wide[A label with a good deal more in it than a word or two] --> short[Brief]"),
        ("a node joined to itself", "flowchart LR\n  a --> a\n  a --> b"),
        ("a ring of nodes", "flowchart TD\n  a --> b --> c --> a"),
        ("a spacing of its own", "---\nconfig:\n  flowchart:\n    nodeSpacing: 80\n    rankSpacing: 80\n---\nflowchart LR\n  a --> b\n  a --> c"),
        ("straight lines rather than curves", "---\nconfig:\n  flowchart:\n    curve: linear\n---\nflowchart TD\n  a --> b"),
        ("where it leads and what it says", "flowchart LR\n  a --> b\n  click a \"https://example.com\" \"Go there\""),
        ("still being written", "flowchart TD\n  a[\"\"]\n  b --> "),
        ("what nobody means to write", "flowchart LR\n  a\n  end\n  style nowhere fill:#969"),
        ("nothing to draw", "flowchart TD"),
    ];

    [TestMethod]
    public void ALinkPutsWhatItReachesInTheRankBeyondWhatItLeaves() => UiThread.Run(() =>
    {
        var down = Nodes("flowchart TD\n  a --> b");
        Assert.IsTrue(down["b"].Top > down["a"].Bottom, $"b is below a: {down["a"]} then {down["b"]}");

        var right = Nodes("flowchart LR\n  a --> b");
        Assert.IsTrue(right["b"].Left > right["a"].Right, $"b is right of a: {right["a"]} then {right["b"]}");

        var up = Nodes("flowchart BT\n  a --> b");
        Assert.IsTrue(up["b"].Bottom < up["a"].Top, $"b is above a: {up["a"]} then {up["b"]}");

        var left = Nodes("flowchart RL\n  a --> b");
        Assert.IsTrue(left["b"].Right < left["a"].Left, $"b is left of a: {left["a"]} then {left["b"]}");
    });

    [TestMethod]
    public void NodesReachedFromTheSameOneShareARank() => UiThread.Run(() =>
    {
        var nodes = Nodes("flowchart TD\n  a --> b\n  a --> c");

        Assert.AreEqual(nodes["b"].Top, nodes["c"].Top, 0.01, "b and c are both one rank on from a");
        Assert.IsTrue(nodes["c"].Left >= nodes["b"].Right || nodes["b"].Left >= nodes["c"].Right, "and they stand apart");
    });

    [TestMethod]
    public void ALinkWrittenLongerReachesFurther() => UiThread.Run(() =>
    {
        var nodes = Nodes("flowchart TD\n  a --> b\n  a -----> c");

        Assert.IsTrue(nodes["c"].Top > nodes["b"].Top, $"c is further down than b: {nodes["b"]} then {nodes["c"]}");
    });

    [TestMethod]
    public void ASubgraphsNodesAreDrawnInsideIt_WithWhatIsWrittenOnItAtTheTop() => UiThread.Run(() =>
    {
        const string source = "flowchart TB\n  subgraph one\n    a1 --> a2\n  end\n  b";

        var laid = Build(source);
        var group = Pieces(laid, FlowchartPiece.Group).Single();
        var inside = Pieces(laid, FlowchartPiece.Node)
            .Where(piece => piece.Ancestors().Any(over => over.Kind == FlowchartPiece.Group))
            .ToList();

        CollectionAssert.AreEqual(new[] { "a1", "a2" }, inside.Select(piece => Written(source, piece.Part)).ToArray(),
                                  "the subgraph's own nodes hang off it, and b does not");

        foreach (var node in inside)
            Assert.IsTrue(Holds(group.Bounds, node.Bounds), $"{node.Bounds} sits inside {group.Bounds}");

        var said = Said(Pieces(laid, FlowchartPiece.Holding).Single()).Single();
        Assert.AreEqual("one", said.Words!.Glyphs.Text);
        Assert.IsTrue(said.Bounds.Top < inside[0].Bounds.Top, "what is written on it is above the nodes inside it");
    });

    [TestMethod]
    public void ASubgraphRunsTheWayItsOwnDirectionSays() => UiThread.Run(() =>
    {
        var nodes = Nodes("flowchart LR\n  subgraph one\n    direction TB\n    a --> b\n  end\n  c --> a");

        Assert.IsTrue(nodes["b"].Top > nodes["a"].Bottom, $"inside the subgraph it runs down: {nodes["a"]} then {nodes["b"]}");
        Assert.IsTrue(nodes["a"].Left > nodes["c"].Right, $"and the chart itself still runs right: {nodes["c"]} then {nodes["a"]}");
    });

    [TestMethod]
    public void ASubgraphDoesNotStandWhereALinkIsDrawnOverIt() => UiThread.Run(() =>
    {
        var laid = Build("flowchart TB\n  subgraph one\n    a -- why --> b\n  end");
        var said = Pieces(laid, FlowchartPiece.Label).Single();
        var holding = Pieces(laid, FlowchartPiece.Holding).Single();
        var at = Middle(said.Bounds);

        Assert.IsTrue(holding.Bounds.Contains(at), "what is written on the link sits over the subgraph");
        Assert.IsFalse(Stands(holding, at), "and the subgraph does not stand there, so a press there means the link");
    });

    [TestMethod]
    public void ALinkRunsFromOneNodesEdgeToTheOthers() => UiThread.Run(() =>
    {
        const string source = "flowchart TD\n  a --> b";

        var laid = Build(source);
        var nodes = Nodes(laid);
        var link = Pieces(laid, FlowchartPiece.Link).Single();

        Assert.AreEqual("-->", Written(source, link.Part), "the line stands for the link that was written, not for what it joins");
        Assert.IsTrue(link.Bounds.Top >= nodes["a"].Bottom - 1, $"the link starts at the edge of a: {link.Bounds.Top} over {nodes["a"].Bottom}");
        Assert.IsTrue(link.Bounds.Bottom <= nodes["b"].Top + 1, $"and stops at the edge of b: {link.Bounds.Bottom} under {nodes["b"].Top}");
    });

    [TestMethod]
    public void WhatIsWrittenOnALinkIsDrawnOverTheMiddleOfIt() => UiThread.Run(() =>
    {
        const string source = "flowchart TD\n  a -- why --> b";

        var laid = Build(source);
        var link = Pieces(laid, FlowchartPiece.Link).Single().Bounds;
        var said = Pieces(laid, FlowchartPiece.Label).Single();

        Assert.AreEqual("why", Said(said).Single().Words!.Glyphs.Text);
        Assert.IsTrue(said.Bounds.Contains(Middle(link)) || link.Contains(Middle(said.Bounds)),
                      $"the label {said.Bounds} sits over the middle of the line {link}");
    });

    [TestMethod]
    public void ALinkOfTildesIsNotDrawn_AndStillHoldsWhatItJoinsApart() => UiThread.Run(() =>
    {
        var laid = Build("flowchart TD\n  a ~~~ b");
        var nodes = Nodes(laid);

        Assert.AreEqual(0, Pieces(laid, FlowchartPiece.Link).Count, "nothing is drawn for it");
        Assert.IsTrue(nodes["b"].Top > nodes["a"].Bottom, "and b is still a rank on from a");
    });

    [TestMethod]
    public void EveryShapeANodeIsWrittenInIsDrawnAsItsOwn() => UiThread.Run(() =>
    {
        var drawn = Pieces(Build(Shapes), FlowchartPiece.Node).Select(Outlined).ToList();

        Assert.AreEqual(14, drawn.Count, "the documentation shows fourteen");
        Assert.AreEqual(14, drawn.Distinct().Count(),
                        "and no two of them are drawn the same: "
                        + string.Join("\n", drawn.GroupBy(said => said).Where(same => same.Count() > 1).Select(same => same.Key)));
    });

    [TestMethod]
    public void ARingOfNodesStillDraws() => UiThread.Run(() =>
    {
        var laid = Build("flowchart TD\n  a --> b --> c --> a");

        Assert.AreEqual(3, Pieces(laid, FlowchartPiece.Node).Count);
        Assert.AreEqual(3, Pieces(laid, FlowchartPiece.Link).Count, "the link back to the start is drawn like any other");
    });

    [TestMethod]
    public void ANodeAskedToBeWordsAloneIsDrawnAsItsWords() => UiThread.Run(() =>
    {
        var laid = Build("flowchart TD\n  a@{ shape: text, label: \"Words alone\" }\n  a --> b");

        var drawn = Pieces(laid, FlowchartPiece.Node)
            .ToDictionary(piece => Said(piece).Select(said => said.Words!.Glyphs.Text).FirstOrDefault() ?? string.Empty, Filled);

        Assert.IsNull(drawn["Words alone"], "nothing is filled round the words");
        Assert.IsNotNull(drawn["b"], "and a node with a shape of its own still is");
    });

    [TestMethod]
    public void ABackslashNBreaksALineAsABreakDoes() => UiThread.Run(() =>
    {
        const string source = "flowchart LR\n  a[\"one\\ntwo\"]";
        var lines = Said(Pieces(Build(source), FlowchartPiece.Node).Single()).ToList();

        CollectionAssert.AreEqual(new[] { "one", "two" }, lines.Select(line => line.Words!.Glyphs.Text).ToArray(), "the break is not drawn");
        CollectionAssert.AreEqual(new[] { "one", "two" }, lines.Select(line => Written(source, line.Part)).ToArray(),
                                  "and each line stands for its own characters");
    });

    [TestMethod]
    public void AWayOutOfADecisionAndBackFromBesideItRunsBetweenTheTwo_WithNoHook() => UiThread.Run(() =>
    {
        const string source = "flowchart TD\n  A[Start] --> B{Is it working?}\n  B -->|Yes| C[Ship it]\n  B -->|No| D[Debug]\n  D --> B\n  C --> E([Done])";
        var laid = Build(source);
        var nodes = Nodes(source);
        var links = Pieces(laid, FlowchartPiece.Link);

        // In the order written: A --> B, B --> C, B --> D, D --> B, C --> E.
        var (decision, debug) = (nodes["Is it working?"], nodes["Debug"]);
        Assert.AreEqual(decision.Top, debug.Top, debug.Height, "Debug is set beside the decision");

        foreach (var link in new[] { links[2], links[3] })
        {
            Assert.IsTrue(link.Bounds.Left >= decision.Right - 1 && link.Bounds.Right <= debug.Left + 1,
                          $"each runs in the gap between the two, rather than folding back past where it leaves: {link.Bounds} between {decision} and {debug}");
            Assert.IsTrue(link.Bounds.Top >= decision.Top && link.Bounds.Bottom <= decision.Bottom,
                          $"and meets the decision at its side, not its top or bottom: {link.Bounds}");
        }
    });

    [TestMethod]
    public void ASubgraphsNameIsSetInABandAcrossItsTop_AndEverySubgraphIsDrawnAlike() => UiThread.Run(() =>
    {
        var laid = Build("flowchart LR\n  subgraph one\n    a\n  end\n  subgraph two\n    b\n  end");
        var groups = Pieces(laid, FlowchartPiece.Holding);

        Assert.AreEqual(2, groups.Count);
        Assert.AreEqual(1, groups.Select(Filled).Distinct().Count(), "one subgraph is not told apart from another by its colour");
        Assert.IsTrue(groups.All(group => Marks(group).Count(mark => mark.Fill is not null) == 2), "each is filled, with its name's band over that");
    });

    [TestMethod]
    public void AnIconIsDrawnInItsForm_WithItsLabelWhereItIsAskedFor() => UiThread.Run(() =>
    {
        var laid = Build("flowchart LR\n  A@{ icon: \"fa:user\", form: \"circle\", label: \"User\", pos: \"t\", h: 48 }");
        var node = laid.Root.SelfAndDescendants().Single(piece => piece.Kind == FlowchartPiece.Node);
        var shape = node.SelfAndDescendants().Single(piece => piece.Kind == MermaidPiece.Shape);
        var words = node.SelfAndDescendants().Where(piece => piece.Kind == MermaidPiece.Words).MinBy(piece => piece.Bounds.Top)!;

        Assert.AreEqual(48, shape.Bounds.Height, 0.5, "as tall as it is asked to be");
        Assert.IsTrue(words.Bounds.Bottom <= shape.Bounds.Top + 0.5, "with its label above it, as pos: t asks");
        Assert.IsTrue(shape.Marks.ToArray().OfType<GeometryMark>().Any(mark => mark.Shape is EllipseGeometry), "standing in its circle");
    });

    [TestMethod]
    public void APictureIsTheOneTheHostFinds_AtTheSizeItIsAskedFor() => UiThread.Run(() =>
    {
        var picture = System.Windows.Media.Imaging.BitmapSource.Create(4, 2, 96, 96, PixelFormats.Bgra32, null, new byte[4 * 2 * 4], 4 * 4);
        var options = new DiagramRenderOptions { Palette = StyleFormat.Dark, Pictures = name => name == "found.png" ? picture : null };
        const string source = "flowchart LR\n  A@{ img: \"found.png\", label: \"Found\", h: 30 }\n  B@{ img: \"lost.png\", label: \"Lost\", w: 50, h: 40 }";

        var laid = Laying.Lay("mermaid", source, 900, options: options);
        var marks = laid.Root.SelfAndDescendants().SelectMany(piece => piece.Marks.ToArray()).ToList();

        var drawn = marks.OfType<PictureMark>().Single();
        Assert.AreSame(picture, drawn.Picture);
        Assert.AreEqual(new Size(60, 30), drawn.Bounds.Size, "as tall as it is asked to be, and as wide as its own shape makes that");
        Assert.IsTrue(marks.OfType<GeometryMark>().Any(mark => mark.Dashes is { Count: > 0 } && System.Math.Abs(mark.Shape.Bounds.Width - 50) < 0.5),
                      "and one nothing was found for is the box it would have filled, dashed");
    });

    private static Laid Build(string source, double room = 900) =>
        Laying.Lay("mermaid", source, room);

    /// <summary>Every node drawn, by what is written on it.</summary>
    private static Dictionary<string, Rect> Nodes(string source) => Nodes(Build(source));

    private static Dictionary<string, Rect> Nodes(Laid laid)
    {
        var nodes = new Dictionary<string, Rect>();

        foreach (var piece in Pieces(laid, FlowchartPiece.Node))
            nodes[Said(piece).Select(said => said.Words!.Glyphs.Text).FirstOrDefault() ?? piece.Bounds.ToString()] = piece.Bounds;

        return nodes;
    }

    /// <summary>What a node's shape comes to as a path, which is the only thing that tells two shapes of the same size apart.</summary>
    private static string Outlined(Piece piece) =>
        string.Join("|", Marks(piece).Select(mark => PathGeometry.CreateFromGeometry(mark.Shape).ToString(CultureInfo.InvariantCulture)));

    /// <summary>The words drawn under a piece, in the order they were drawn.</summary>
    private static IEnumerable<Piece> Said(Piece piece) =>
        piece.SelfAndDescendants().Where(part => part.Kind == MermaidPiece.Words && part.Words is not null);

    private static IReadOnlyList<GeometryMark> Marks(Piece piece) =>
        [.. piece.SelfAndDescendants().First(part => part.Kind == MermaidPiece.Shape).Marks.ToArray().OfType<GeometryMark>()];

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

    [TestMethod]
    public void AnIconIsTheGlyphItNames_AndAQuestionMarkWhereTheAppDrawsNone() => UiThread.Run(() =>
    {
        var named = Build("flowchart LR\n  A@{ icon: \"fa:user\", form: \"square\", label: \"User\" }");
        var unknown = Build("flowchart LR\n  A@{ icon: \"logos:aws-lambda\", form: \"square\", label: \"Lambda\" }");

        Assert.AreEqual(1, named.Root.SelfAndDescendants().Count(piece => piece.Kind == MermaidPiece.Glyph), "the person the icon names");
        Assert.AreEqual(0, unknown.Root.SelfAndDescendants().Count(piece => piece.Kind == MermaidPiece.Glyph));
        Assert.IsTrue(unknown.Root.SelfAndDescendants().Any(piece => piece.Words?.Glyphs.Text == "?"), "as Mermaid draws an icon it has no pack for");
    });
}

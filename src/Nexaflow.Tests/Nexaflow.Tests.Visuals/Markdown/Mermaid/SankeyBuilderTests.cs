using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Sankey;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A <c>sankey-beta</c> block drawn on the shared layout tree: a bar for each node as tall as it is worth, in a column as
/// far along as the flows reaching it, and a ribbon for each flow as thick as it is worth.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("sankey")]
public class SankeyBuilderTests : MermaidBuilderContract
{
    private const string Energy =
        "sankey-beta\n\nAgricultural waste,Bio-conversion,124.729\nBio-conversion,Liquid,0.597\nBio-conversion,Losses,26.862\n"
        + "Bio-conversion,Solid,280.322\nBio-conversion,Gas,81.144";

    public override MermaidDiagram Diagram => MermaidDiagram.Sankey;

    protected override IEnumerable<(string What, string Source)> Drawn =>
    [
        ("where the energy goes", Energy),
        ("a flow and no more", "sankey-beta\na,b,10"),
        ("a chain of three columns", "sankey-beta\na,b,10\nb,c,10\nc,d,10"),
        ("names in quotes", "sankey-beta\n\"Waste, agricultural\",\"Bio \"\"conversion\"\"\",124.729"),
        ("coloured by where a flow comes from",
            "---\nconfig:\n  sankey:\n    linkColor: source\n    nodeAlignment: left\n---\nsankey-beta\n\na,b,10\na,c,20"),
        ("coloured by where it goes, and outlined labels",
            "---\nconfig:\n  sankey:\n    linkColor: target\n    labelStyle: outlined\n    prefix: \"$\"\n    suffix: B\n---\nsankey-beta\n\na,b,10"),
        ("one colour for every flow, and colours of its own for the nodes",
            "---\nconfig:\n  sankey:\n    linkColor: \"#7f7f7f\"\n    nodeColors:\n      a: \"#ff0000\"\n---\nsankey-beta\n\na,b,10"),
        ("saying nothing of what anything is worth", "---\nconfig:\n  sankey:\n    showValues: false\n---\nsankey-beta\n\na,b,10\nb,c,10"),
        ("a size of its own", "---\nconfig:\n  sankey:\n    width: 400\n    height: 200\n    nodeWidth: 16\n    nodePadding: 20\n---\nsankey-beta\n\na,b,10\na,c,5"),
        ("a title over it", "---\ntitle: Where it goes\n---\nsankey-beta\n\na,b,10"),
        ("still being written", "sankey-beta\na,b,\n,,"),
        ("what nobody means to write", "sankey-beta\na,b,lots\nc,d,-4"),
        ("nothing to draw", "sankey-beta"),
    ];

    [TestMethod]
    public void ANodeIsABarAsTallAsItIsWorth() => UiThread.Run(() =>
    {
        var bars = Bars(Build("sankey-beta\na,b,10\na,c,20"));

        Assert.AreEqual(3, bars.Count);
        Assert.AreEqual(bars[1].Height * 2, bars[2].Height, 0.5, "c is worth twice what b is, so its bar is twice as tall");
        Assert.AreEqual(bars[1].Height + bars[2].Height, bars[0].Height, 0.5, "and a is worth what flows out of it");
    });

    [TestMethod]
    public void ANodeSitsAsFarAlongAsTheFlowsReachingItGo() => UiThread.Run(() =>
    {
        var bars = Bars(Build("sankey-beta\na,b,10\nb,c,10"));

        Assert.IsTrue(bars[1].Left > bars[0].Left, "b is a column past a");
        Assert.IsTrue(bars[2].Left > bars[1].Left, "and c a column past b");
        Assert.AreEqual(bars[1].Left - bars[0].Left, bars[2].Left - bars[1].Left, 0.5, "the columns being evenly spread");
    });

    [TestMethod]
    public void ARibbonRunsFromOneBarToTheOther_AsThickAsTheFlowIsWorth() => UiThread.Run(() =>
    {
        const string source = "sankey-beta\na,b,10";

        var laid = Build(source);
        var bars = Bars(laid);
        var ribbon = Pieces(laid, SankeyPiece.Flow).Single();

        Assert.AreEqual("a,b,10", Written(source, ribbon.Part), "the ribbon stands for the row it was written on");
        Assert.AreEqual(bars[0].Height, ribbon.Bounds.Height, 0.5, "and is as thick as the bars it joins");
        Assert.AreEqual(bars[0].Right, ribbon.Bounds.Left, 0.5);
        Assert.AreEqual(bars[1].Left, ribbon.Bounds.Right, 0.5);
    });

    [TestMethod]
    public void ANodeSaysWhatItIsCalledAndWhatItIsWorth_OnTheFarSideOfItFromItsFlows() => UiThread.Run(() =>
    {
        const string source = "---\nconfig:\n  sankey:\n    prefix: \"$\"\n---\nsankey-beta\n\na,b,10";

        var laid = Build(source);
        var bars = Bars(laid);
        var said = Pieces(laid, SankeyPiece.Label).Select(piece => piece.Words!.Glyphs.Text).ToList();

        CollectionAssert.AreEqual(new[] { "a", "$10", "b", "$10" }, said.ToArray());
        Assert.AreEqual("a", Written(source, Pieces(laid, SankeyPiece.Label)[0].Part), "a name is typed into where it is drawn");

        var labels = Pieces(laid, SankeyPiece.Label).Select(piece => piece.Bounds).ToList();
        Assert.IsTrue(labels[0].Left > bars[0].Right, "the first column says what it is on its right");
        Assert.IsTrue(labels[2].Right < bars[1].Left, "and the last on its left");
    });

    [TestMethod]
    public void NothingSaysWhatItIsWorthWhereTheFrontMatterSaysNotTo() => UiThread.Run(() =>
    {
        var said = Pieces(Build("---\nconfig:\n  sankey:\n    showValues: false\n---\nsankey-beta\n\na,b,10"), SankeyPiece.Label)
            .Select(piece => piece.Words!.Glyphs.Text).ToList();

        CollectionAssert.AreEqual(new[] { "a", "b" }, said.ToArray());
    });

    [TestMethod]
    public void AFlowIsColouredTheWayTheFrontMatterAsks() => UiThread.Run(() =>
    {
        var written = Marks(Build("---\nconfig:\n  sankey:\n    linkColor: \"#7f7f7f\"\n---\nsankey-beta\n\na,b,10"));
        Assert.AreEqual(Color.FromRgb(0x7F, 0x7F, 0x7F), ((SolidColorBrush)written.Fill!).Color);

        var gradient = Marks(Build("sankey-beta\na,b,10"));
        Assert.IsInstanceOfType<LinearGradientBrush>(gradient.Fill, "a flow nothing colours runs from one end's colour to the other's");

        var source = Marks(Build("---\nconfig:\n  sankey:\n    linkColor: source\n    nodeColors:\n      a: \"#ff0000\"\n---\nsankey-beta\n\na,b,10"));
        Assert.AreEqual(Color.FromRgb(0xFF, 0, 0), ((SolidColorBrush)source.Fill!).Color, "and one coloured by where it comes from takes that colour");
    });

    [TestMethod]
    public void AFlowWorthNothingIsNoRibbon() => UiThread.Run(() =>
    {
        var laid = Build("sankey-beta\na,b,10\nc,d,0");

        Assert.AreEqual(1, Pieces(laid, SankeyPiece.Flow).Count, "only what is worth something is drawn");
        Assert.AreEqual(2, Pieces(laid, SankeyPiece.Node).Count, "and only the nodes it is drawn between");
    });

    [TestMethod]
    public void TheColumnsStandAWidthOfTheirOwnApart_HoweverLongTheNamesAtTheEdges() => UiThread.Run(() =>
    {
        var bars = Bars(Build("sankey-beta\nA source with a very long name indeed,middle,10\nmiddle,A destination with a very long name too,10"));

        Assert.IsTrue(bars[1].Left - bars[0].Left >= 110, $"the middle is not squeezed by the names either side of it: {bars[0]} then {bars[1]}");
        Assert.IsTrue(bars[2].Left - bars[1].Left >= 110, $"on either side of it: {bars[1]} then {bars[2]}");
    });

    [TestMethod]
    public void ANodeGoesDownItsColumnAsFarAsWhatFlowsIntoItComesFrom() => UiThread.Run(() =>
    {
        // Written this way round, T1 comes first — but nearly all of it comes from S2, below S1, which is all T2 is from.
        const string source = "sankey-beta\nX,T1,0.1\nS1,T2,5\nS2,T1,5";
        var laid = Build(source);
        var bars = Pieces(laid, SankeyPiece.Node).ToDictionary(piece => Written(source, piece.Part), piece => piece.Bounds);

        Assert.IsTrue(bars["T2"].Top < bars["T1"].Top, $"so T2 goes above it, and the two ribbons run across rather than over one another: {bars["T2"]} and {bars["T1"]}");
    });

    [TestMethod]
    public void AColumnWithRoomToSpareSpreadsItsNodesLevelWithWhatTheyAreJoinedTo() => UiThread.Run(() =>
    {
        // The first column is full; the second has most of its height to spare. Packed about its middle, both of its nodes would sit
        // between A and B and both ribbons would slant; spread, each sits level with what feeds it and its ribbon runs flat.
        const string source = "sankey-beta\nA,W,10\nB,Y,1";
        var laid = Build(source);
        var bars = Pieces(laid, SankeyPiece.Node).ToDictionary(piece => Written(source, piece.Part), piece => piece.Bounds);

        Assert.AreEqual(Middle(bars["A"]).Y, Middle(bars["W"]).Y, 2, "W is level with A");
        Assert.AreEqual(Middle(bars["B"]).Y, Middle(bars["Y"]).Y, 2, "and Y with B, far below it");
    });

    [TestMethod]
    public void AMiddleColumnsNameIsWrittenIntoTheGapAfterItsBar() => UiThread.Run(() =>
    {
        const string source = "sankey-beta\na,b,10\nb,c,10\nb,d,4\nd,e,4";
        var laid = Build(source);
        var labels = Pieces(laid, SankeyPiece.Label).Where(piece => Written(source, piece.Part) == "d").Select(piece => piece.Bounds).ToList();
        var bar = Bars(laid)[Pieces(laid, SankeyPiece.Node).FindIndex(piece => Written(source, piece.Part) == "d")];

        Assert.IsTrue(labels[0].Left > bar.Right, $"to the right of it, where the next column's names are not: {labels[0]} after {bar}");
    });

    [TestMethod]
    public void ARibbonIsDrawnAtHalfStrength_AndTheBarsItJoinsWhole() => UiThread.Run(() =>
    {
        var laid = Build("sankey-beta\na,b,10");
        var ribbon = Marks(laid).Fill!;
        var bar = Pieces(laid, SankeyPiece.Node)[0].Marks.ToArray().OfType<GeometryMark>().First().Fill!;

        Assert.AreEqual(0.5, ribbon.Opacity, 0.01, "the ribbon is the darker body of the flow");
        Assert.AreEqual(1, bar.Opacity, 0.01, "and the bars the light ends of it");
        Assert.IsTrue(((LinearGradientBrush)ribbon).GradientStops.All(stop => stop.Color.A == 255), "faded as a whole, not stop by stop");
    });

    private static Laid Build(string source, double room = 900) =>
        Laying.Lay("mermaid", source, room);

    private static GeometryMark Marks(Laid laid) =>
        Pieces(laid, SankeyPiece.Flow).Single().Marks.ToArray().OfType<GeometryMark>().First();

    /// <summary>The bars alone, a node's piece holding what is written beside it as well.</summary>
    private static List<Rect> Bars(Laid laid) =>
        [.. Pieces(laid, SankeyPiece.Node).Select(piece =>
        {
            var bar = piece.Marks.ToArray().OfType<GeometryMark>().First().Shape.Bounds;
            return Rect.Offset(bar, piece.Bounds.X - piece.Box.X, piece.Bounds.Y - piece.Box.Y);
        })];

    [TestMethod]
    public void TheNodesAreTheNamesTheFlowsAreWrittenBetween_InTheOrderTheyAreFirstWritten() => UiThread.Run(() =>
    {
        var named = Pieces(Build(Energy), SankeyPiece.Node).Select(piece => Written(Energy, piece.Part)).ToArray();

        CollectionAssert.AreEqual(new[] { "Agricultural waste", "Bio-conversion", "Liquid", "Losses", "Solid", "Gas" }, named);
    });

    [TestMethod]
    public void ANodeIsWorthWhateverFlowsIntoItOrOutOfIt_WhicheverIsTheMore() => UiThread.Run(() =>
    {
        var said = Pieces(Build("sankey-beta\na,b,10\nb,c,4"), SankeyPiece.Label).Select(piece => piece.Words!.Glyphs.Text).ToArray();

        CollectionAssert.AreEqual(new[] { "a", "10", "b", "10", "c", "4" }, said, "b takes in ten and passes on four, so it is worth ten");
    });

    [TestMethod]
    public void ANameInQuotesSaysWhatIsBetweenThem_AQuoteWrittenTwiceStandingForOne() => UiThread.Run(() =>
    {
        string First(string source) => Pieces(Build(source), SankeyPiece.Label)[0].Words!.Glyphs.Text;

        Assert.AreEqual("Waste, agricultural", First("sankey-beta\n\"Waste, agricultural\",b,10"));
        Assert.AreEqual("Agricultural \"waste\"", First("sankey-beta\n\"Agricultural \"\"waste\"\"\",b,10"));
        Assert.AreEqual("a", First("sankey-beta\n  a , b , 10"), "and the space round a field is not part of it");
        Assert.AreEqual(3, Pieces(Build("sankey-beta\n  a , b , 10\na,c,5"), SankeyPiece.Node).Count, "so a is the one node however it is spaced");
    });

    [TestMethod]
    public void WhatAFlowWorthNothingNamesIsWorthNothingByIt() => UiThread.Run(() =>
    {
        var named = Pieces(Build("sankey-beta\na,b,10\nb,c,0\nc,d,0"), SankeyPiece.Node).Count;

        Assert.AreEqual(2, named, "c and d are named only by flows worth nothing, so neither has a bar");
    });
}

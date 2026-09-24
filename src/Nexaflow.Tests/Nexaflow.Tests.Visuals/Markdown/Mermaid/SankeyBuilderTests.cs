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

    private static Laid Build(string source, double room = 900) =>
        new SankeyBuilder(MermaidBuilders.Read(source), EditState.For(source), StyleFormat.Dark, isReadOnly: true).Lay(room);

    private static GeometryMark Marks(Laid laid) =>
        Pieces(laid, SankeyPiece.Flow).Single().Marks.ToArray().OfType<GeometryMark>().First();

    /// <summary>The bars alone, a node's piece holding what is written beside it as well.</summary>
    private static List<Rect> Bars(Laid laid) =>
        [.. Pieces(laid, SankeyPiece.Node).Select(piece =>
        {
            var bar = piece.Marks.ToArray().OfType<GeometryMark>().First().Shape.Bounds;
            return Rect.Offset(bar, piece.Bounds.X - piece.Box.X, piece.Bounds.Y - piece.Box.Y);
        })];
}
